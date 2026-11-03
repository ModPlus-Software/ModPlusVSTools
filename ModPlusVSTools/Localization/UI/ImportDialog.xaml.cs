using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.VisualStudio.PlatformUI;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Импорт строк: ручной (значения по одному на строку в порядке языков)
    /// или автоматический (строки с тегами).
    /// </summary>
    public partial class ImportDialog : DialogWindow
    {
        internal ImportDialog(IReadOnlyList<string> languages, bool withTags)
        {
            InitializeComponent();

            var order = string.Join(", ", languages);
            if (withTags)
            {
                Title = "Импорт строк (авто)";
                NoteText.Text =
                    $"Вставьте текст вместе с тегами (<tag>значение</tag>), содержащий перевод фраз в порядке: {order}. " +
                    "Перевод для каждого языка — на новой строке. Можно добавлять сразу несколько строк. " +
                    "Существующие ключи обновляются, новые вставляются после последней строки с тем же базовым именем. " +
                    "Пустые строки игнорируются.";
            }
            else
            {
                Title = "Импорт строк после выбранной";
                NoteText.Text =
                    $"Вставьте текст, содержащий перевод фраз в порядке: {order}. " +
                    "Перевод для каждого языка — на новой строке. Можно добавлять перевод сразу для нескольких новых строк. " +
                    "Пустые строки игнорируются.";
                AutoNumerateBox.Visibility = Visibility.Collapsed;
            }

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (_, _) => InputBox.Focus();
        }

        /// <summary>Введённый текст.</summary>
        internal string ImportText => InputBox.Text;

        /// <summary>Автонумерация (автоимпорт).</summary>
        internal bool AutoNumerate => AutoNumerateBox.IsChecked == true;

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && OkButton.IsEnabled)
            {
                DialogResult = true;
                e.Handled = true;
            }
        }

        private void InputBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = InputBox.Text.Any(c => !char.IsWhiteSpace(c));
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
