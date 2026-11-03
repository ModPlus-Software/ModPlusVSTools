using System;
using System.Windows;

using Microsoft.VisualStudio.PlatformUI;

using ModPlusVSTools.Localization.Models;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Пометка строки комментарием «todo remove after X» (как в LangFilesEditor).
    /// </summary>
    public partial class MarkForDeletionDialog : DialogWindow
    {
        internal MarkForDeletionDialog(LocEntry entry, string? lastVersion)
        {
            InitializeComponent();

            // Снимать можно только реально записанную пометку, поэтому состояние определяет комментарий
            var version = entry.DeletionVersion;
            var isMarked = version != null;

            VersionBox.Text = version ?? lastVersion ?? string.Empty;
            UnmarkButton.IsEnabled = isMarked;

            if (isMarked)
                StateText.Text = $"Строка «{entry.Name}» помечена к удалению после версии {version}.";
            else if (!string.IsNullOrWhiteSpace(entry.Comment))
                StateText.Text = $"У строки «{entry.Name}» есть комментарий: {entry.Comment!.Trim()}" + Environment.NewLine +
                                 "Пометка заменит этот комментарий.";
            else
                StateText.Text = $"Строка «{entry.Name}» не помечена к удалению.";

            Loaded += (_, _) =>
            {
                VersionBox.Focus();
                VersionBox.SelectAll();
            };
        }

        /// <summary>Версия (при пометке).</summary>
        internal string Version { get; private set; } = string.Empty;

        /// <summary>Снять пометку.</summary>
        internal bool Unmark { get; private set; }

        private void Mark_OnClick(object sender, RoutedEventArgs e)
        {
            if (!System.Version.TryParse(VersionBox.Text.Trim(), out var version))
            {
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            Version = version.ToString();
            DialogResult = true;
        }

        private void Unmark_OnClick(object sender, RoutedEventArgs e)
        {
            Unmark = true;
            DialogResult = true;
        }
    }
}
