using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using ModPlusVSTools.Localization.Models;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Вариант добавления атрибутов узла плагина.
    /// </summary>
    internal sealed class AttributeOption
    {
        /// <summary>
        /// Создаёт вариант.
        /// </summary>
        /// <param name="names">Имена добавляемых атрибутов.</param>
        public AttributeOption(params string[] names)
        {
            Names = names;
        }

        /// <summary>Имена добавляемых атрибутов.</summary>
        public IReadOnlyList<string> Names { get; }

        /// <summary>Текст для списка.</summary>
        public string Title => string.Join(" + ", Names);

        /// <inheritdoc/>
        public override string ToString() => Title;
    }

    /// <summary>
    /// Правила именования атрибутов узла плагина.
    /// </summary>
    /// <remarks>
    /// Обязательные атрибуты — <c>LocalName</c> и <c>Description</c>. Необязательные —
    /// <c>FullDescription</c> и пары <c>LocalName1</c> – <c>Description1</c>, <c>LocalName2</c> – <c>Description2</c> и т.д.
    /// (имена и описания отдельных функций плагина). Других атрибутов быть не должно.
    /// </remarks>
    internal static class AttributeRules
    {
        /// <summary>Имя плагина.</summary>
        public const string LocalName = "LocalName";

        /// <summary>Описание плагина.</summary>
        public const string Description = "Description";

        /// <summary>Полное описание плагина.</summary>
        public const string FullDescription = "FullDescription";

        private static readonly Regex NameRegex = new Regex(
            @"^(?<kind>LocalName|Description)(?<number>[1-9]\d*)?$|^FullDescription$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Разбирает имя атрибута.
        /// </summary>
        /// <param name="name">Имя.</param>
        /// <param name="kind"><c>LocalName</c>, <c>Description</c> или <c>FullDescription</c>.</param>
        /// <param name="number">Номер пары; 0 — без номера.</param>
        /// <returns><c>true</c>, если имя соответствует правилам.</returns>
        public static bool TryParse(string name, out string kind, out int number)
        {
            kind = string.Empty;
            number = 0;

            var match = NameRegex.Match(name ?? string.Empty);
            if (!match.Success)
                return false;

            if (!match.Groups["kind"].Success)
            {
                kind = FullDescription;
                return true;
            }

            kind = match.Groups["kind"].Value;
            if (match.Groups["number"].Success)
                number = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);

            return true;
        }

        /// <summary>
        /// Порядок атрибута в узле: LocalName, Description, FullDescription, затем пары по номерам.
        /// </summary>
        /// <param name="name">Имя атрибута.</param>
        public static int GetSortKey(string name)
        {
            if (!TryParse(name, out var kind, out var number))
                return int.MaxValue;

            if (kind == FullDescription)
                return 2;

            var offset = kind == LocalName ? 0 : 1;
            return number == 0 ? offset : 10 + (number * 2) + offset;
        }

        /// <summary>
        /// Проверяет атрибуты узла плагина.
        /// </summary>
        /// <param name="attributes">Атрибуты.</param>
        /// <param name="entryIssues">Проблемы отдельных атрибутов.</param>
        /// <returns>Ошибки уровня узла (отсутствующие обязательные атрибуты).</returns>
        public static List<string> Validate(IReadOnlyList<LocEntry> attributes, Dictionary<LocEntry, List<string>> entryIssues)
        {
            var moduleIssues = new List<string>();
            var names = new HashSet<string>(attributes.Select(a => a.Name), StringComparer.Ordinal);

            if (!names.Contains(LocalName))
                moduleIssues.Add("нет обязательного атрибута LocalName");
            if (!names.Contains(Description))
                moduleIssues.Add("нет обязательного атрибута Description");

            foreach (var attribute in attributes)
            {
                if (!TryParse(attribute.Name, out var kind, out var number))
                {
                    AddIssue(entryIssues, attribute,
                        "недопустимое имя атрибута (допустимы LocalName, Description, FullDescription, LocalNameN, DescriptionN)");
                    continue;
                }

                if (number == 0)
                    continue;

                var pairName = (kind == LocalName ? Description : LocalName) + number.ToString(CultureInfo.InvariantCulture);
                if (!names.Contains(pairName))
                    AddIssue(entryIssues, attribute, $"нет парного атрибута {pairName}");
            }

            return moduleIssues;
        }

        /// <summary>
        /// Возвращает варианты добавления атрибутов.
        /// </summary>
        /// <param name="attributes">Текущие атрибуты.</param>
        public static List<AttributeOption> GetAddOptions(IEnumerable<LocEntry> attributes)
        {
            var names = new HashSet<string>(attributes.Select(a => a.Name), StringComparer.Ordinal);
            var options = new List<AttributeOption>();

            if (!names.Contains(LocalName))
                options.Add(new AttributeOption(LocalName));
            if (!names.Contains(Description))
                options.Add(new AttributeOption(Description));
            if (!names.Contains(FullDescription))
                options.Add(new AttributeOption(FullDescription));

            var maxNumber = 0;
            foreach (var name in names)
            {
                if (TryParse(name, out _, out var number) && number > maxNumber)
                    maxNumber = number;
            }

            // Недостающая половина неполной пары
            for (var number = 1; number <= maxNumber; number++)
            {
                var localName = LocalName + number.ToString(CultureInfo.InvariantCulture);
                var description = Description + number.ToString(CultureInfo.InvariantCulture);
                var hasLocalName = names.Contains(localName);
                var hasDescription = names.Contains(description);

                if (hasLocalName && !hasDescription)
                    options.Add(new AttributeOption(description));
                else if (!hasLocalName && hasDescription)
                    options.Add(new AttributeOption(localName));
                else if (!hasLocalName)
                    options.Add(new AttributeOption(localName, description));
            }

            var next = (maxNumber + 1).ToString(CultureInfo.InvariantCulture);
            options.Add(new AttributeOption(LocalName + next, Description + next));

            return options;
        }

        /// <summary>
        /// Возвращает атрибуты, удаляемые вместе с указанным, или пустой список, если удалять нельзя.
        /// </summary>
        /// <param name="attributes">Текущие атрибуты.</param>
        /// <param name="attribute">Удаляемый атрибут.</param>
        /// <param name="reason">Причина запрета.</param>
        public static List<LocEntry> GetRemovalSet(IReadOnlyList<LocEntry> attributes, LocEntry attribute, out string? reason)
        {
            reason = null;
            var result = new List<LocEntry>();

            if (!TryParse(attribute.Name, out var kind, out var number))
            {
                // Атрибут с недопустимым именем удалить можно всегда
                result.Add(attribute);
                return result;
            }

            if (kind == FullDescription)
            {
                result.Add(attribute);
                return result;
            }

            if (number == 0)
            {
                reason = $"Атрибут {attribute.Name} обязателен для узла плагина и не может быть удалён.";
                return result;
            }

            var maxNumber = attributes
                .Select(a => TryParse(a.Name, out _, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();

            if (number != maxNumber)
            {
                reason = $"Удалить можно только последнюю пару атрибутов (LocalName{maxNumber} – Description{maxNumber}): " +
                         "номера пар соответствуют функциям плагина, и пропуск номера нарушит их нумерацию.";
                return result;
            }

            var suffix = number.ToString(CultureInfo.InvariantCulture);
            result.AddRange(attributes.Where(a => a.Name == LocalName + suffix || a.Name == Description + suffix));
            return result;
        }

        /// <summary>
        /// Индекс вставки атрибута с сохранением канонического порядка.
        /// </summary>
        /// <param name="attributes">Текущие атрибуты.</param>
        /// <param name="name">Имя нового атрибута.</param>
        public static int GetInsertIndex(IList<LocEntry> attributes, string name)
        {
            var key = GetSortKey(name);
            var index = 0;
            while (index < attributes.Count && GetSortKey(attributes[index].Name) <= key)
                index++;

            return index;
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
