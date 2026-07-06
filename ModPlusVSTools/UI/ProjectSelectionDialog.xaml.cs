using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using ModPlusVSTools.Services;

namespace ModPlusVSTools.UI
{
    /// <summary>
    /// Диалог выбора проекта плагина, когда в решении их несколько.
    /// </summary>
    public partial class ProjectSelectionDialog : DialogWindow
    {
        internal ProjectSelectionDialog(IReadOnlyList<PluginProjectCandidate> candidates)
        {
            InitializeComponent();

            ProjectsList.ItemsSource = candidates;
            if (candidates.Count > 0)
                ProjectsList.SelectedIndex = 0;

            ProjectsList.Focus();
        }

        /// <summary>Выбранный проект (null, если диалог отменён).</summary>
        internal PluginProjectCandidate SelectedProject { get; private set; }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void ProjectsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Confirm();
        }

        private void Confirm()
        {
            var selected = ProjectsList.SelectedItem as PluginProjectCandidate;
            if (selected == null)
                return;

            SelectedProject = selected;
            DialogResult = true;
            Close();
        }
    }
}
