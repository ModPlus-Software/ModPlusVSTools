using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Win32;

using ModPlus.LanguageTools;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Устройство папки исходных файлов локализации для редактора. Имена языков, суффиксы
    /// и ключи источников берутся из ModPlus.LanguageTools.LanguageFolder — так же, как при сборке.
    /// </summary>
    /// <remarks>
    /// В подпапке языка соседствуют общий файл домена (<c>Common.xml</c>) и вынесенные файлы
    /// плагинов с суффиксом языка (<c>Revit_mprAlignViews_ru.xml</c>). Оба приводятся к одному
    /// языконезависимому ключу источника (<c>Revit_mprAlignViews</c>).
    /// </remarks>
    internal static class LocalizationFolder
    {
        /// <summary>Имя общего домена.</summary>
        public const string CommonDomainName = LanguageFolder.CommonDomainName;

        /// <summary>Разделитель частей имени файла.</summary>
        public const char DomainSeparator = '_';

        /// <summary>Базовый язык: всегда первый столбец.</summary>
        public const string BaseLanguage = "ru-RU";

        /// <summary>Порядок групп в дереве.</summary>
        public static IReadOnlyList<string> DomainOrder { get; } = new[] { CommonDomainName, "AutoCAD", "nanoCAD", "Revit", "Renga" };

        /// <summary>
        /// Программы, для которых можно создать узел плагина, и принятый префикс имени узла.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> PluginPrograms { get; } = new[]
        {
            new KeyValuePair<string, string>("Revit", "mpr"),
            new KeyValuePair<string, string>("AutoCAD", "mp"),
            new KeyValuePair<string, string>("nanoCAD", "mnc"),
            new KeyValuePair<string, string>("Renga", "mr"),
        };

        /// <summary>
        /// Возвращает языки — те же, что при сборке (<see cref="LanguageFolder.GetLanguageNames"/>),
        /// но базовый язык первым, остальные по алфавиту.
        /// </summary>
        /// <param name="root">Папка исходных файлов локализации.</param>
        /// <exception cref="LanguageToolsException">В папке нет подпапок языков.</exception>
        public static IReadOnlyList<string> GetLanguages(string root)
        {
            return LanguageFolder.GetLanguageNames(root)
                .OrderBy(name => string.Equals(name, BaseLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Суффикс имени файла для языка: <c>ru-RU</c> → <c>ru</c>.
        /// </summary>
        /// <param name="language">Язык.</param>
        public static string GetLanguageSuffix(string language) => LanguageFolder.GetLanguageSuffix(language);

        /// <summary>
        /// Приводит имя файла языка к ключу источника: <c>Revit_mprAlignViews_ru</c> → <c>Revit_mprAlignViews</c>.
        /// </summary>
        /// <param name="fileNameWithoutExtension">Имя файла без расширения.</param>
        /// <param name="language">Язык папки.</param>
        public static string GetSourceKey(string fileNameWithoutExtension, string language) =>
            LanguageFolder.GetSourceKey(fileNameWithoutExtension, language);

        /// <summary>
        /// Имя группы по ключу источника: часть до первого разделителя.
        /// </summary>
        /// <param name="sourceKey">Ключ источника.</param>
        public static string GetDomainName(string sourceKey)
        {
            var index = sourceKey.IndexOf(DomainSeparator);
            return index > 0 ? sourceKey.Substring(0, index) : sourceKey;
        }

        /// <summary>
        /// Путь к файлу источника для языка, в котором файла ещё нет.
        /// </summary>
        /// <param name="root">Папка исходных файлов локализации.</param>
        /// <param name="language">Язык.</param>
        /// <param name="sourceKey">Ключ источника.</param>
        /// <param name="sharedFileName">Файл без суффикса языка (<c>Common.xml</c>).</param>
        public static string BuildSourceFilePath(string root, string language, string sourceKey, bool sharedFileName)
        {
            var fileName = sharedFileName
                ? sourceKey + ".xml"
                : sourceKey + DomainSeparator + GetLanguageSuffix(language) + ".xml";

            return Path.Combine(root, language, fileName);
        }

        /// <summary>
        /// Порядок группы в дереве.
        /// </summary>
        /// <param name="domainName">Имя группы.</param>
        public static int GetDomainOrder(string domainName)
        {
            for (var i = 0; i < DomainOrder.Count; i++)
            {
                if (string.Equals(DomainOrder[i], domainName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return DomainOrder.Count;
        }

        /// <summary>
        /// Папка Languages установленного ModPlus, в которую собираются языковые файлы.
        /// </summary>
        /// <remarks>
        /// Сначала берётся папка установки из реестра (<c>HKCU\Software\ModPlus</c>, значение <c>TopDir</c>):
        /// путь принимается, только если папка существует и в ней есть подпапка Languages — так отсекается
        /// мусорное значение (например, путь в кэш XAML-дизайнера VS, который туда может записать ModPlusAPI).
        /// Если из реестра получить папку не удалось — <c>%AppData%\ModPlus\Languages</c>,
        /// как в LangFilesEditor.
        /// </remarks>
        /// <param name="source">Откуда взят путь (для лога).</param>
        /// <returns>Путь или <c>null</c>, если установленный ModPlus не найден.</returns>
        public static string? GetInstalledLanguagesFolder(out string source)
        {
            var topDir = ReadRegistryTopDir();
            if (!string.IsNullOrWhiteSpace(topDir))
            {
                try
                {
                    var languages = Path.Combine(topDir!.Trim(), "Languages");
                    if (Directory.Exists(languages))
                    {
                        source = $"реестр, {RegistryKeyPath}\\{RegistryValueName} = {topDir}";
                        return Path.GetFullPath(languages);
                    }
                }
                catch (ArgumentException)
                {
                    // Некорректный путь в реестре — используется путь по умолчанию
                }
                catch (NotSupportedException)
                {
                    // То же
                }
            }

            var appDataTopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModPlus");
            source = string.IsNullOrWhiteSpace(topDir)
                ? "по умолчанию %AppData%\\ModPlus (в реестре путь не задан)"
                : $"по умолчанию %AppData%\\ModPlus (в папке из реестра «{topDir}» нет подпапки Languages)";

            return Directory.Exists(appDataTopDir) ? Path.Combine(appDataTopDir, "Languages") : null;
        }

        private const string RegistryKeyPath = @"HKEY_CURRENT_USER\Software\ModPlus";
        private const string RegistryValueName = "TopDir";

        private static string? ReadRegistryTopDir()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\ModPlus");
                return key?.GetValue(RegistryValueName) as string;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Полный путь без завершающего разделителя.
        /// </summary>
        /// <param name="root">Путь.</param>
        public static string NormalizeRoot(string root) =>
            Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
