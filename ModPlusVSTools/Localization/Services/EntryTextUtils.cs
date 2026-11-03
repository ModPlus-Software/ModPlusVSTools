using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using ModPlusVSTools.Localization.Models;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Имена новых записей, разбор импортируемого текста и формат буфера обмена
    /// (поведение повторяет LangFilesEditor).
    /// </summary>
    internal static class EntryTextUtils
    {
        /// <summary>Префикс собственного формата буфера обмена.</summary>
        public const string ClipboardPrefix = "LANG_";

        private static readonly string[] LineSeparators = { "\r\n", "\n" };
        private static readonly Regex TrailingNumberRegex = new Regex(@"\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex OpenTagRegex = new Regex("^<[^>]+>", RegexOptions.CultureInvariant);
        private static readonly Regex CloseTagRegex = new Regex("<[^>]+>$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Следующее имя: увеличивает числовой суффикс (<c>t12</c> → <c>t13</c>), без суффикса добавляет <c>1</c>.
        /// </summary>
        /// <param name="name">Имя предыдущей записи.</param>
        /// <param name="existing">Существующие записи: при передаче имя подбирается уникальным.</param>
        public static string GetNextName(string? name, IEnumerable<LocEntry>? existing = null)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var candidate = Increment(name!);
            if (existing == null)
                return candidate;

            var taken = new HashSet<string>(existing.Select(e => e.Name), StringComparer.Ordinal);
            while (taken.Contains(candidate))
                candidate = Increment(candidate);

            return candidate;
        }

        /// <summary>
        /// Разделяет имя на базовую часть и числовой суффикс: <c>t12</c> → <c>t</c>, 12.
        /// </summary>
        public static void SplitTag(string? tag, out string value, out int number)
        {
            number = 0;
            value = tag ?? string.Empty;
            var match = TrailingNumberRegex.Match(value);
            if (!match.Success)
                return;

            if (!int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                number = 0;
                return;
            }

            value = value.Substring(0, match.Index);
        }

        /// <summary>
        /// Разбор текста ручного импорта: значения каждой строки идут подряд по одному на строку
        /// в порядке языков; пустые строки игнорируются, неполный хвост отбрасывается.
        /// </summary>
        /// <param name="text">Текст.</param>
        /// <param name="languageCount">Число языков.</param>
        public static List<string[]> ParseManualImport(string text, int languageCount)
        {
            var result = new List<string[]>();
            if (languageCount == 0 || string.IsNullOrEmpty(text))
                return result;

            var lines = text.Split(LineSeparators, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            for (var i = 0; i + languageCount <= lines.Count; i += languageCount)
                result.Add(lines.Skip(i).Take(languageCount).ToArray());

            return result;
        }

        /// <summary>
        /// Разбор текста автоимпорта: строки вида <c>&lt;tag&gt;значение&lt;/tag&gt;</c>, по одной на язык
        /// в порядке языков. Теги с неполным набором значений отбрасываются.
        /// </summary>
        /// <param name="text">Текст.</param>
        /// <param name="languageCount">Число языков.</param>
        /// <returns>Пары «тег → значения» в порядке первого появления тега.</returns>
        public static List<KeyValuePair<string, List<string>>> ParseTaggedImport(string text, int languageCount)
        {
            var order = new List<string>();
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var raw in text.Split(LineSeparators, StringSplitOptions.None))
            {
                var row = raw.Trim();
                var match = OpenTagRegex.Match(row);
                if (!match.Success)
                    continue;

                var tag = match.Value.Substring(1, match.Value.Length - 2);
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                var content = CloseTagRegex.Replace(OpenTagRegex.Replace(row, string.Empty), string.Empty);
                if (!map.TryGetValue(tag, out var values))
                {
                    values = new List<string>();
                    map[tag] = values;
                    order.Add(tag);
                }

                values.Add(content);
            }

            return order
                .Where(tag => map[tag].Count == languageCount)
                .Select(tag => new KeyValuePair<string, List<string>>(tag, map[tag]))
                .ToList();
        }

        /// <summary>
        /// Текст записи для буфера обмена: <c>LANG_имя|язык$значение|язык$значение</c>.
        /// </summary>
        /// <param name="entry">Запись.</param>
        /// <param name="languages">Языки.</param>
        public static string ToClipboardText(LocEntry entry, IReadOnlyList<string> languages)
        {
            var builder = new StringBuilder(ClipboardPrefix).Append(entry.Name);
            for (var i = 0; i < languages.Count; i++)
            {
                var value = entry.GetRawValue(i);
                if (value == null)
                    continue;

                builder.Append('|').Append(languages[i]).Append('$').Append(value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Разбирает текст буфера обмена.
        /// </summary>
        /// <param name="text">Текст.</param>
        /// <param name="languages">Языки.</param>
        /// <param name="name">Имя записи.</param>
        /// <param name="values">Значения по индексам языков (<c>null</c> — языка нет в буфере).</param>
        /// <returns><c>true</c>, если текст в формате редактора.</returns>
        public static bool TryParseClipboardText(string? text, IReadOnlyList<string> languages, out string name, out string?[] values)
        {
            name = string.Empty;
            values = new string?[languages.Count];

            if (string.IsNullOrEmpty(text) || !text!.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
                return false;

            var parts = text.Substring(ClipboardPrefix.Length).Split('|');
            if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
                return false;

            name = parts[0];
            foreach (var part in parts.Skip(1))
            {
                var separator = part.IndexOf('$');
                if (separator <= 0)
                    continue;

                var language = part.Substring(0, separator);
                for (var i = 0; i < languages.Count; i++)
                {
                    if (string.Equals(languages[i], language, StringComparison.OrdinalIgnoreCase))
                    {
                        values[i] = part.Substring(separator + 1);
                        break;
                    }
                }
            }

            return true;
        }

        private static string Increment(string name)
        {
            var match = TrailingNumberRegex.Match(name);
            if (!match.Success || !long.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return name + "1";

            return name.Substring(0, match.Index) + (number + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
