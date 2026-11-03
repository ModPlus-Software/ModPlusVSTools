using System;
using System.Collections.Generic;
using System.Xml;

using ModPlus.LanguageTools.Checks;

using ModPlusVSTools.Localization.Models;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Результат проверки модуля.
    /// </summary>
    internal sealed class ModuleValidationResult
    {
        /// <summary>Есть ошибки.</summary>
        public bool HasErrors { get; set; }

        /// <summary>Ошибки уровня узла.</summary>
        public string? ModuleIssues { get; set; }
    }

    /// <summary>
    /// Проверка записей модуля по правилам <see cref="EntryRules"/> из ModPlus.LanguageTools — тем же,
    /// по которым проверяются файлы при сборке и публикации:
    /// <list type="bullet">
    /// <item>ошибка — пустое имя ключа, имя с цифры или недопустимое для xml, пустое значение перевода, дубликат имени;</item>
    /// <item>предупреждение — у записей без комментария полностью совпадают наборы значений по всем языкам.</item>
    /// </list>
    /// Для атрибутов узла плагина дополнительно проверяются правила именования (<see cref="AttributeRules"/>).
    /// </summary>
    /// <remarks>
    /// Дополнительно к правилам сборки редактор отмечает имена и символы, недопустимые в xml:
    /// с ними файл нельзя записать.
    /// </remarks>
    internal static class EntryValidator
    {
        /// <summary>
        /// Проверяет модуль и обновляет состояние записей.
        /// </summary>
        /// <param name="module">Модуль.</param>
        public static ModuleValidationResult Validate(LocModule module)
        {
            var result = new ModuleValidationResult();
            var issues = new Dictionary<LocEntry, List<string>>();
            var warnings = new HashSet<LocEntry>();

            CheckEntries(module.Items, issues, warnings);
            CheckEntries(module.Attributes, issues, warnings);

            if (module.HasPluginAttributes && module.IsLoaded)
            {
                var moduleIssues = AttributeRules.Validate(module.Attributes, issues);
                if (moduleIssues.Count > 0)
                {
                    result.HasErrors = true;
                    result.ModuleIssues = string.Join("; ", moduleIssues);
                }
            }

            Apply(module.Items, issues, warnings, result);
            Apply(module.Attributes, issues, warnings, result);

            return result;
        }

        /// <summary>
        /// Является ли имя допустимым именем xml-элемента/атрибута.
        /// </summary>
        /// <param name="name">Имя.</param>
        public static bool IsValidXmlName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            try
            {
                XmlConvert.VerifyNCName(name);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static void CheckEntries(
            IList<LocEntry> entries,
            Dictionary<LocEntry, List<string>> issues,
            HashSet<LocEntry> warnings)
        {
            var emptyLanguages = new List<int>();

            foreach (var entry in entries)
            {
                var name = entry.Name;

                if (EntryRules.IsEmptyName(name))
                    AddIssue(issues, entry, "пустое имя ключа");
                else if (EntryRules.StartsWithDigit(name))
                    AddIssue(issues, entry, "имя ключа не должно начинаться с цифры");
                else if (!IsValidXmlName(name))
                    AddIssue(issues, entry, "имя ключа недопустимо для xml");

                emptyLanguages.Clear();
                var hasInvalidChars = false;
                for (var language = 0; language < entry.LanguageCount; language++)
                {
                    // null — значения в этом языке нет; такие языки не проверяются (как в EntriesCheck)
                    var value = entry.GetRawValue(language);
                    if (value == null)
                        continue;

                    if (EntryRules.IsEmptyValue(value))
                        emptyLanguages.Add(language);
                    else if (!hasInvalidChars && HasInvalidXmlChars(value))
                        hasInvalidChars = true;
                }

                if (emptyLanguages.Count > 0)
                    AddIssue(issues, entry, "пустое значение перевода (" + FormatLanguages(entry.Owner, emptyLanguages) + ")");

                if (hasInvalidChars)
                    AddIssue(issues, entry, "значение содержит символы, недопустимые в xml (управляющие символы)");
            }

            foreach (var entry in EntryRules.FindDuplicateNames(entries, e => e.Name))
                AddIssue(issues, entry, "дубликат имени ключа");

            foreach (var entry in EntryRules.FindDuplicateValues(entries, e => e.Comment, PresentValues))
                warnings.Add(entry);
        }

        /// <summary>
        /// Значения языков, в которых запись есть (как в EntriesCheck: отсутствующий язык в сравнении не участвует).
        /// </summary>
        private static IEnumerable<string> PresentValues(LocEntry entry)
        {
            for (var i = 0; i < entry.LanguageCount; i++)
            {
                var value = entry.GetRawValue(i);
                if (value != null)
                    yield return value;
            }
        }

        private static bool HasInvalidXmlChars(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                if (!XmlConvert.IsXmlChar(c))
                    return true;
            }

            return false;
        }

        private static void Apply(
            IList<LocEntry> entries,
            Dictionary<LocEntry, List<string>> issues,
            HashSet<LocEntry> warnings,
            ModuleValidationResult result)
        {
            foreach (var entry in entries)
            {
                var hasError = issues.TryGetValue(entry, out var entryIssues);
                var hasWarning = warnings.Contains(entry);

                string? text = null;
                if (hasError)
                {
                    result.HasErrors = true;
                    text = "Ошибка: " + string.Join("; ", entryIssues);
                }

                if (hasWarning)
                {
                    const string warning = "Предупреждение: дубликат набора значений перевода";
                    text = text == null ? warning : text + Environment.NewLine + warning;
                }

                entry.SetDiagnostics(hasError, hasWarning, text);
            }
        }

        private static string FormatLanguages(LocModule? module, List<int> indexes)
        {
            if (module == null)
                return string.Join(", ", indexes);

            var names = new List<string>(indexes.Count);
            foreach (var index in indexes)
                names.Add(module.Languages[index]);

            return string.Join(", ", names);
        }

        private static void AddIssue(Dictionary<LocEntry, List<string>> issues, LocEntry entry, string issue)
        {
            if (!issues.TryGetValue(entry, out var list))
            {
                list = new List<string>();
                issues[entry] = list;
            }

            list.Add(issue);
        }
    }
}
