using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ModPlusVSTools.Models;

namespace ModPlusVSTools.Services
{
    /// <summary>
    /// Проверка, загрузка и запуск установки обновлений расширения из раздела Releases на GitHub.
    /// </summary>
    /// <remarks>
    /// Последний релиз определяется по редиректу страницы /releases/latest на /releases/tag/{tag}.
    /// Это не расходует лимит запросов GitHub API (60 в час для анонимных запросов с одного IP).
    /// Черновики и пре-релизы GitHub в /releases/latest не попадают.
    /// </remarks>
    internal static class UpdateService
    {
        private const string RepositoryUrl = "https://github.com/ModPlus-Software/ModPlusVSTools";

        /// <summary>Имя файла расширения, прикрепляемого к каждому релизу.</summary>
        private const string AssetFileName = "ModPlusVSTools.vsix";

        private static readonly Lazy<Version?> InstalledVersion = new Lazy<Version?>(ReadInstalledVersion);

        private static readonly Lazy<HttpClient> CheckClient = new Lazy<HttpClient>(() =>
            CreateClient(allowAutoRedirect: false, TimeSpan.FromSeconds(15)));

        private static readonly Lazy<HttpClient> DownloadClient = new Lazy<HttpClient>(() =>
            CreateClient(allowAutoRedirect: true, TimeSpan.FromMinutes(5)));

        /// <summary>
        /// Версия установленного расширения из extension.vsixmanifest или null, если её не удалось определить.
        /// </summary>
        public static Version? GetInstalledVersion() => InstalledVersion.Value;

        /// <summary>
        /// Возвращает сведения о последнем релизе или null, если релизов нет или тег не является версией.
        /// </summary>
        public static async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, RepositoryUrl + "/releases/latest"))
            using (var response = await CheckClient.Value
                       .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
                {
                    throw new HttpRequestException(
                        $"Неожиданный ответ GitHub: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var location = response.Headers.Location;
                if (!location.IsAbsoluteUri)
                    location = new Uri(new Uri(RepositoryUrl), location);

                // Ожидается .../releases/tag/{tag}. Если релизов нет, GitHub перенаправляет на .../releases
                const string tagMarker = "/releases/tag/";
                var path = location.AbsolutePath;
                var markerIndex = path.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                    return null;

                var tag = Uri.UnescapeDataString(path.Substring(markerIndex + tagMarker.Length).TrimEnd('/'));
                var version = ParseVersion(tag);
                if (version == null)
                    return null;

                var escapedTag = Uri.EscapeDataString(tag);
                return new ReleaseInfo(
                    tag,
                    version,
                    $"{RepositoryUrl}/releases/tag/{escapedTag}",
                    $"{RepositoryUrl}/releases/download/{escapedTag}/{AssetFileName}");
            }
        }

        /// <summary>
        /// Загружает файл .vsix релиза во временную папку и возвращает путь к нему.
        /// </summary>
        public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            var folder = Path.Combine(Path.GetTempPath(), "ModPlusVSTools");
            Directory.CreateDirectory(folder);

            var safeTag = string.Concat(release.Tag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var targetPath = Path.Combine(folder, $"ModPlusVSTools-{safeTag}.vsix");
            var tempPath = targetPath + ".download";

            using (var response = await DownloadClient.Value
                       .GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException(
                        $"В релизе {release.Tag} нет файла {AssetFileName}.", AssetFileName);
                }

                response.EnsureSuccessStatusCode();

                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await source.CopyToAsync(target, 81920, cancellationToken).ConfigureAwait(false);
                }
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);

            return targetPath;
        }

        /// <summary>
        /// Запускает установщик VSIX для указанного файла.
        /// Установщик сам дождётся закрытия Visual Studio.
        /// </summary>
        public static void LaunchInstaller(string vsixPath)
        {
            var installerPath = GetVsixInstallerPath();
            var startInfo = installerPath != null
                ? new ProcessStartInfo(installerPath, "\"" + vsixPath + "\"")
                : new ProcessStartInfo(vsixPath);
            startInfo.UseShellExecute = true;

            Process.Start(startInfo);
        }

        /// <summary>
        /// Открывает страницу релиза в браузере.
        /// </summary>
        public static void OpenReleasePage(ReleaseInfo release)
        {
            Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
        }

        /// <summary>
        /// Разбирает версию из имени тега. Допускается префикс "v" ("v2.1" → 2.1).
        /// Недостающие компоненты дополняются нулями, чтобы "2.0" и "2.0.0.0" считались одинаковыми.
        /// </summary>
        internal static Version? ParseVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var value = text!.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(1);

            if (!Version.TryParse(value, out var version))
                return null;

            return new Version(
                version.Major,
                version.Minor,
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0));
        }

        private static Version? ReadInstalledVersion()
        {
            try
            {
                var folder = Path.GetDirectoryName(typeof(UpdateService).Assembly.Location);
                if (folder == null)
                    return null;

                var manifestPath = Path.Combine(folder, "extension.vsixmanifest");
                if (!File.Exists(manifestPath))
                {
                    Debug.WriteLine("ModPlusVSTools: не найден " + manifestPath);
                    return null;
                }

                var identity = XDocument.Load(manifestPath)
                    .Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Identity");

                return ParseVersion(identity?.Attribute("Version")?.Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ModPlusVSTools: не удалось прочитать версию расширения: " + ex);
                return null;
            }
        }

        private static string? GetVsixInstallerPath()
        {
            try
            {
                // VSIXInstaller.exe лежит рядом с devenv.exe (Common7\IDE)
                var devenvPath = Process.GetCurrentProcess().MainModule?.FileName;
                var folder = devenvPath != null ? Path.GetDirectoryName(devenvPath) : null;
                if (folder == null)
                    return null;

                var installerPath = Path.Combine(folder, "VSIXInstaller.exe");
                return File.Exists(installerPath) ? installerPath : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ModPlusVSTools: не удалось найти VSIXInstaller.exe: " + ex);
                return null;
            }
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code >= 300 && code < 400;
        }

        private static HttpClient CreateClient(bool allowAutoRedirect, TimeSpan timeout)
        {
            // Системный прокси используется по умолчанию (WebRequest.DefaultWebProxy)
            var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect };

            var client = new HttpClient(handler) { Timeout = timeout };
            var version = GetInstalledVersion()?.ToString() ?? "unknown";
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ModPlusVSTools/" + version);
            return client;
        }
    }
}
