using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

using Microsoft.VisualStudio.PlatformUI;

using ModPlusVSTools.Localization.Services;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Создание узла плагина: программа (префикс файла) и уникальное имя узла.
    /// </summary>
    public partial class NewModuleDialog : DialogWindow
    {
        // Имя узла — имя xml-элемента и часть имени файла: латиница и цифры, начинается с буквы.
        // Подчёркивание запрещено: оно разделяет части имени файла (Revit_mprName_ru.xml)
        private static readonly Regex NameRegex = new Regex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

        private readonly Func<string, string?> _validateUnique;
        private string _lastPrefix = string.Empty;

        internal NewModuleDialog(Func<string, string?> validateUnique)
        {
            _validateUnique = validateUnique;
            InitializeComponent();

            ProgramBox.ItemsSource = LocalizationFolder.PluginPrograms.Select(p => p.Key).ToList();
            ProgramBox.SelectedIndex = 0;

            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.CaretIndex = NameBox.Text.Length;
            };
        }

        /// <summary>Программа (префикс файла): Revit, AutoCAD, nanoCAD, Renga.</summary>
        internal string Program => ProgramBox.SelectedItem as string ?? string.Empty;

        /// <summary>Имя узла.</summary>
        internal string ModuleName => NameBox.Text.Trim();

        /// <summary>
        /// При смене программы подставляется принятый префикс имени узла (mpr, mp, mnc, mr),
        /// если пользователь ещё не ввёл своё имя.
        /// </summary>
        private void ProgramBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var prefix = LocalizationFolder.PluginPrograms.FirstOrDefault(p => p.Key == Program).Value ?? string.Empty;
            var name = NameBox.Text.Trim();
            if (name.Length == 0 || name == _lastPrefix)
                NameBox.Text = prefix;
            else if (_lastPrefix.Length > 0 && name.StartsWith(_lastPrefix, StringComparison.Ordinal))
                NameBox.Text = prefix + name.Substring(_lastPrefix.Length);

            _lastPrefix = prefix;
            NameBox.CaretIndex = NameBox.Text.Length;
            Validate();
        }

        private void NameBox_OnTextChanged(object sender, TextChangedEventArgs e) => Validate();

        private bool Validate()
        {
            var name = ModuleName;
            string? error = null;

            if (name.Length == 0 || name == _lastPrefix)
                error = "Введите имя узла.";
            else if (!NameRegex.IsMatch(name))
                error = "Имя может содержать только латинские буквы и цифры и должно начинаться с буквы.";
            else
                error = _validateUnique(name);

            ErrorText.Text = error ?? string.Empty;
            FilesText.Text = error == null && Program.Length > 0
                ? $"Будут созданы файлы {Program}_{name}_<язык>.xml во всех папках языков с узлом <{name}> и атрибутами LocalName и Description."
                : string.Empty;

            if (OkButton != null)
                OkButton.IsEnabled = error == null;

            return error == null;
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (Validate())
                DialogResult = true;
        }
    }
}
