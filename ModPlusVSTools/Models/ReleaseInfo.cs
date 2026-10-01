using System;

namespace ModPlusVSTools.Models
{
    /// <summary>
    /// Сведения о релизе расширения на GitHub.
    /// </summary>
    internal sealed class ReleaseInfo
    {
        public ReleaseInfo(string tag, Version version, string pageUrl, string downloadUrl)
        {
            Tag = tag;
            Version = version;
            PageUrl = pageUrl;
            DownloadUrl = downloadUrl;
        }

        /// <summary>Имя тега релиза (например, "2.1").</summary>
        public string Tag { get; }

        /// <summary>Версия, полученная из имени тега.</summary>
        public Version Version { get; }

        /// <summary>Адрес страницы релиза на GitHub.</summary>
        public string PageUrl { get; }

        /// <summary>Прямая ссылка на файл .vsix в релизе.</summary>
        public string DownloadUrl { get; }
    }
}
