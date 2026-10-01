using System.IO;
using System.Linq;

namespace ModPlusVSTools.Services
{
    /// <summary>
    /// Проверка корректности пути к папке файлов локализации.
    /// </summary>
    internal static class LocalizationPathValidator
    {
        /// <summary>
        /// Проверяет путь к папке файлов локализации.
        /// </summary>
        /// <param name="path">Проверяемый путь.</param>
        /// <returns>Текст ошибки или null, если путь корректен.</returns>
        public static string? Validate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Не задан путь к папке файлов локализации ModPlus. " +
                       "Всплывающие подсказки по ключам локализации работать не будут.";
            }

            if (!Directory.Exists(path))
            {
                return $"Папка файлов локализации не найдена: {path}";
            }

            if (!HasLocalizationFiles(path))
            {
                return $"В папке «{path}» не найдены файлы локализации " +
                       "(ожидаются подпапки с именами языков, например ru-RU, с XML-файлами).";
            }

            return null;
        }

        private static bool HasLocalizationFiles(string path)
        {
            try
            {
                return LocaleFolderDetector.GetLocales(path)
                    .Any(locale => Directory.EnumerateFiles(Path.Combine(path, locale), "*.xml", SearchOption.TopDirectoryOnly).Any());
            }
            catch (IOException)
            {
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
