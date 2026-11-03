using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using ModPlusVSTools.Localization.Models;
using ModPlusVSTools.Localization.ViewModels;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Содержимое вкладки узла: таблица атрибутов и таблица строк.
    /// </summary>
    public partial class ModuleView : UserControl
    {
        private ModuleTabViewModel? _tab;
        private bool _isSubscribed;

        /// <summary>
        /// Создаёт вкладку.
        /// </summary>
        public ModuleView()
        {
            EditorResourceDictionary.MergeInto(this);
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => Subscribe();
            Unloaded += (_, _) => Unsubscribe();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Unsubscribe();
            _tab = e.NewValue as ModuleTabViewModel;
            if (_tab == null)
                return;

            EntryGridColumns.Build(ItemsGrid, _tab.Languages, string.Empty, this);
            EntryGridColumns.Build(AttributesGrid, _tab.Languages, string.Empty, this, isNameReadOnly: true);

            if (IsLoaded)
                Subscribe();
        }

        private void Subscribe()
        {
            if (_isSubscribed || _tab == null)
                return;

            _tab.ScrollRequested += OnScrollRequested;
            _tab.Editor.CommitEditsRequested += OnCommitEditsRequested;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed || _tab == null)
                return;

            _tab.ScrollRequested -= OnScrollRequested;
            _tab.Editor.CommitEditsRequested -= OnCommitEditsRequested;
            _isSubscribed = false;
        }

        private void OnCommitEditsRequested()
        {
            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            AttributesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>
        /// Прокрутка откладывается: строка только что добавлена, и контейнер для неё ещё не создан.
        /// </summary>
        private void OnScrollRequested(LocEntry entry)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                ItemsGrid.ScrollIntoView(entry);
                ItemsGrid.SelectedItem = entry;
            }));
        }
    }
}
