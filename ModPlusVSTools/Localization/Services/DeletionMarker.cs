using System;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Разбор и сборка комментария-пометки «todo remove after X» (как в LangFilesEditor).
    /// </summary>
    /// <remarks>
    /// Разбор строгий: границы диапазонов <c>todo remove after 1.2.3 start</c> / <c>… end</c>
    /// пометкой отдельной записи не являются.
    /// </remarks>
    internal static class DeletionMarker
    {
        /// <summary>Префикс пометки.</summary>
        public const string Prefix = "todo remove after ";

        /// <summary>
        /// Извлекает версию из комментария-пометки.
        /// </summary>
        public static bool TryGetVersion(string? comment, out string? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(comment))
                return false;

            var prefix = Prefix.TrimEnd();
            var trimmed = comment!.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = trimmed.Substring(prefix.Length);
            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
                return false;

            rest = rest.Trim();
            if (rest.Length == 0 || !Version.TryParse(rest, out _))
                return false;

            version = rest;
            return true;
        }

        /// <summary>
        /// Является ли комментарий пометкой к удалению.
        /// </summary>
        public static bool IsMarked(string? comment) => TryGetVersion(comment, out _);

        /// <summary>
        /// Собирает текст пометки.
        /// </summary>
        public static string Build(string version) => Prefix + version;
    }
}
