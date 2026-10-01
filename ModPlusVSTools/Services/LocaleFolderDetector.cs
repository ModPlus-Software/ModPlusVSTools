using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ModPlusVSTools.Services
{
    /// <summary>
    /// Определение доступных локалей по именам подпапок базовой папки файлов локализации.
    /// </summary>
    internal static class LocaleFolderDetector
    {
        /// <summary>
        /// Базовая локаль, которая всегда выводится первой.
        /// </summary>
        public const string BaseLocale = "ru-RU";

        private static readonly object SyncRoot = new object();
        private static readonly Lazy<HashSet<string>> KnownCultureNames = new Lazy<HashSet<string>>(LoadKnownCultureNames);

        private static string? _cachedBasePath;
        private static DateTime _cachedLastWriteTimeUtc;
        private static IReadOnlyList<string> _cachedLocales = Array.Empty<string>();

        /// <summary>
        /// Возвращает список локалей (канонические имена культур, например ru-RU), для которых
        /// в базовой папке есть подпапка с именем, соответствующим культуре.
        /// Базовая локаль идёт первой, остальные — по алфавиту.
        /// </summary>
        /// <param name="basePath">Путь к базовой папке файлов локализации.</param>
        public static IReadOnlyList<string> GetLocales(string? basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return Array.Empty<string>();

            basePath = basePath!.Trim();

            try
            {
                if (!Directory.Exists(basePath))
                    return Array.Empty<string>();

                // Добавление, удаление и переименование подпапок меняет дату изменения родительской папки
                var lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(basePath);

                lock (SyncRoot)
                {
                    if (string.Equals(_cachedBasePath, basePath, StringComparison.OrdinalIgnoreCase) &&
                        _cachedLastWriteTimeUtc == lastWriteTimeUtc)
                    {
                        return _cachedLocales;
                    }

                    var locales = Directory.EnumerateDirectories(basePath)
                        .Select(dir => TryGetCultureName(Path.GetFileName(dir)))
                        .Where(name => name != null)
                        .Select(name => name!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => string.Equals(name, BaseLocale, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    _cachedBasePath = basePath;
                    _cachedLastWriteTimeUtc = lastWriteTimeUtc;
                    _cachedLocales = locales;

                    return locales;
                }
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Возвращает каноническое имя культуры, если имя папки соответствует известной культуре
        /// (например, папка «ru-ru» → «ru-RU»), иначе null.
        /// </summary>
        /// <param name="folderName">Имя папки.</param>
        public static string? TryGetCultureName(string? folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return null;

            // Проверка по списку культур системы. CultureInfo.GetCultureInfo в Windows 10+
            // может принять произвольное корректно оформленное имя как пользовательскую культуру,
            // поэтому только на него полагаться нельзя
            if (!KnownCultureNames.Value.Contains(folderName!))
                return null;

            try
            {
                var culture = CultureInfo.GetCultureInfo(folderName!);
                return string.IsNullOrEmpty(culture.Name) ? null : culture.Name;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        private static HashSet<string> LoadKnownCultureNames()
        {
            return new HashSet<string>(
                CultureInfo.GetCultures(CultureTypes.AllCultures)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name)),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
