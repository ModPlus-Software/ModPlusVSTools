using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Построение столбцов таблиц записей: имя ключа и по столбцу на каждый язык
    /// (языки определяются по именам папок исходных файлов локализации).
    /// </summary>
    internal static class EntryGridColumns
    {
        private static readonly Dictionary<string, string> KnownTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru-RU"] = "Русский",
            ["uk-UA"] = "Українська",
            ["en-US"] = "English",
            ["de-DE"] = "Deutsch",
            ["es-ES"] = "Español",
            ["zh-CN"] = "中文",
            ["fr-FR"] = "Français",
            ["pt-BR"] = "Português",
            ["uz-Latn-UZ"] = "Oʻzbekcha",
        };

        /// <summary>
        /// Пересоздаёт столбцы таблицы.
        /// </summary>
        /// <param name="grid">Таблица.</param>
        /// <param name="languages">Языки.</param>
        /// <param name="pathPrefix">Префикс пути привязки к записи (<c>""</c> или <c>"Entry"</c>).</param>
        /// <param name="resources">Элемент, в ресурсах которого есть стили ячеек.</param>
        /// <param name="isNameReadOnly">Имя ключа только для чтения.</param>
        public static void Build(
            DataGrid grid,
            IReadOnlyList<string> languages,
            string pathPrefix,
            FrameworkElement resources,
            bool isNameReadOnly = false)
        {
            var textStyle = (Style)resources.FindResource("CellTextStyle");
            var editStyle = (Style)resources.FindResource("CellEditStyle");
            var prefix = pathPrefix.TrimEnd('.');

            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Ключ",
                Binding = new Binding(prefix.Length == 0 ? "Name" : prefix + ".Name") { Mode = BindingMode.TwoWay },
                Width = new DataGridLength(170),
                MinWidth = 60,
                IsReadOnly = isNameReadOnly,
                ElementStyle = textStyle,
                EditingElementStyle = editStyle,
            });

            for (var i = 0; i < languages.Count; i++)
            {
                var index = i.ToString(CultureInfo.InvariantCulture);
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = GetTitle(languages[i]),
                    Binding = new Binding(prefix + "[" + index + "]") { Mode = BindingMode.TwoWay },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    MinWidth = 140,
                    ElementStyle = textStyle,
                    EditingElementStyle = editStyle,
                });
            }
        }

        private static string GetTitle(string language) =>
            KnownTitles.TryGetValue(language, out var title) ? title + Environment.NewLine + language : language;
    }
}
