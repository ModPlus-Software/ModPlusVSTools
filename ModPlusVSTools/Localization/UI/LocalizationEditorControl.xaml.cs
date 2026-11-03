using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using ModPlusVSTools.Localization.Models;
using ModPlusVSTools.Localization.ViewModels;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Содержимое окна редактора локализации.
    /// </summary>
    public partial class LocalizationEditorControl : UserControl
    {
        private EditorViewModel? _viewModel;

        /// <summary>
        /// Создаёт контрол.
        /// </summary>
        public LocalizationEditorControl()
        {
            EditorResourceDictionary.MergeInto(this);
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Подключает модель редактора.
        /// </summary>
        internal void Attach(EditorViewModel viewModel)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.CommitEditsRequested -= OnCommitEditsRequested;
            }

            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.CommitEditsRequested += OnCommitEditsRequested;
            BuildSearchColumns();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel?.Initialize();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditorViewModel.Languages))
                BuildSearchColumns();
            else if (e.PropertyName == nameof(EditorViewModel.IsSearchPageActive) && _viewModel?.IsSearchPageActive == true)
                Dispatcher.BeginInvoke(new System.Action(() => GlobalSearchBox.Focus()));
        }

        private void OnCommitEditsRequested()
        {
            SearchGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void BuildSearchColumns()
        {
            if (_viewModel == null)
                return;

            EntryGridColumns.Build(SearchGrid, _viewModel.Languages, "Entry.", this);
        }

        private void NavTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is LocModule module)
                _viewModel?.OpenModule(module);
        }

        /// <summary>
        /// Средняя кнопка мыши закрывает вкладку (как в редакторе VS).
        /// </summary>
        private void Tabs_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is ModuleTabViewModel tab)
            {
                tab.Editor.CloseTab(tab);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Имена атрибутов не переименовываются, заголовки узлов не редактируются.
        /// </summary>
        private void SearchGrid_OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is not SearchRow row || row.Entry == null)
            {
                e.Cancel = true;
                return;
            }

            if (row.Entry.IsAttribute && e.Column.DisplayIndex == 0)
                e.Cancel = true;
        }

        private void SearchGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.Item is SearchRow searchRow && searchRow.IsHeader && _viewModel != null)
            {
                // Строка-заголовок без ячеек щелчком не выделяется — выделяем явно
                _viewModel.Search.SelectedRow = searchRow;
                _viewModel.Search.OpenSelected();
                e.Handled = true;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? element)
            where T : DependencyObject
        {
            while (element != null && element is not T)
            {
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return element as T;
        }
    }
}
