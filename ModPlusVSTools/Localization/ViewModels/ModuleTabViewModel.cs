using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.VisualStudio.Shell.Interop;

using ModPlusVSTools.Localization.Models;
using ModPlusVSTools.Localization.Services;

namespace ModPlusVSTools.Localization.ViewModels
{
    /// <summary>
    /// Вкладка узла локализации: таблица атрибутов и таблица строк.
    /// </summary>
    internal sealed class ModuleTabViewModel : ObservableBase
    {
        private readonly DispatcherTimer _filterTimer;
        private readonly HashSet<LocEntry> _alwaysVisible = new HashSet<LocEntry>();
        private ListCollectionView _itemsView = null!;
        private LocEntry? _selectedEntry;
        private LocEntry? _selectedAttribute;
        private AttributeOption? _selectedAttributeOption;
        private List<AttributeOption> _attributeOptions = new List<AttributeOption>();
        private string _searchText = string.Empty;
        private string _appliedFilter = string.Empty;
        private bool _isSelected;
        private bool _isAttributesExpanded;

        /// <summary>
        /// Создаёт вкладку.
        /// </summary>
        /// <param name="editor">Редактор.</param>
        /// <param name="module">Модуль.</param>
        public ModuleTabViewModel(EditorViewModel editor, LocModule module)
        {
            Editor = editor;
            Module = module;

            _filterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _filterTimer.Tick += (_, _) =>
            {
                _filterTimer.Stop();
                ApplyFilter();
            };

            Module.PropertyChanged += OnModulePropertyChanged;
            CreateViews();

            CloseCommand = new RelayCommand(() => Editor.CloseTab(this));
            AddAttributeCommand = new RelayCommand(AddAttribute, () => Module.IsLoaded && SelectedAttributeOption != null);
            RemoveAttributeCommand = new RelayCommand(RemoveAttribute, () => Module.IsLoaded && SelectedAttribute != null);
        }

        /// <summary>Событие запроса прокрутки таблицы к записи.</summary>
        public event Action<LocEntry>? ScrollRequested;

        /// <summary>Редактор.</summary>
        public EditorViewModel Editor { get; }

        /// <summary>Модуль.</summary>
        public LocModule Module { get; }

        /// <summary>Языки.</summary>
        public IReadOnlyList<string> Languages => Module.Languages;

        /// <summary>Представление строк с фильтром поиска.</summary>
        public ListCollectionView ItemsView
        {
            get => _itemsView;
            private set => SetField(ref _itemsView, value);
        }

        /// <summary>Выбранная строка.</summary>
        public LocEntry? SelectedEntry
        {
            get => _selectedEntry;
            set => SetField(ref _selectedEntry, value);
        }

        /// <summary>Выбранный атрибут.</summary>
        public LocEntry? SelectedAttribute
        {
            get => _selectedAttribute;
            set => SetField(ref _selectedAttribute, value);
        }

        /// <summary>Варианты добавления атрибутов.</summary>
        public List<AttributeOption> AttributeOptions
        {
            get => _attributeOptions;
            private set => SetField(ref _attributeOptions, value);
        }

        /// <summary>Выбранный вариант добавления атрибутов.</summary>
        public AttributeOption? SelectedAttributeOption
        {
            get => _selectedAttributeOption;
            set => SetField(ref _selectedAttributeOption, value);
        }

        /// <summary>Можно ли редактировать состав атрибутов (только узлы плагинов).</summary>
        public bool CanEditAttributes => Module.HasPluginAttributes;

        /// <summary>Развёрнута ли таблица атрибутов.</summary>
        public bool IsAttributesExpanded
        {
            get => _isAttributesExpanded;
            set => SetField(ref _isAttributesExpanded, value);
        }

        /// <summary>Заголовок блока атрибутов.</summary>
        public string AttributesHeader => string.IsNullOrEmpty(Module.ModuleIssues)
            ? $"Атрибуты ({Module.Attributes.Count})"
            : $"Атрибуты ({Module.Attributes.Count}) — ошибка: {Module.ModuleIssues}";

        /// <summary>Строка поиска по таблице (фильтр применяется с задержкой).</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetField(ref _searchText, value ?? string.Empty))
                    return;

                _filterTimer.Stop();
                _filterTimer.Start();
            }
        }

        /// <summary>Вкладка активна.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        /// <summary>Закрыть вкладку.</summary>
        public ICommand CloseCommand { get; }

        /// <summary>Добавить атрибут(ы).</summary>
        public ICommand AddAttributeCommand { get; }

        /// <summary>Удалить атрибут.</summary>
        public ICommand RemoveAttributeCommand { get; }

        /// <summary>
        /// Выбирает запись и прокручивает к ней таблицу.
        /// </summary>
        /// <param name="entry">Запись.</param>
        public void SelectAndScroll(LocEntry entry)
        {
            if (entry.IsAttribute)
            {
                IsAttributesExpanded = true;
                SelectedAttribute = entry;
                return;
            }

            if (!string.IsNullOrEmpty(_appliedFilter) && !_itemsView.PassesFilter(entry))
            {
                _alwaysVisible.Add(entry);
                _itemsView.Refresh();
            }

            SelectedEntry = entry;
            ScrollRequested?.Invoke(entry);
        }

        #region Команды строк

        /// <summary>Вставить пустую строку перед выбранной.</summary>
        public void InsertBefore()
        {
            var index = SelectedEntry != null ? Module.Items.IndexOf(SelectedEntry) : 0;
            InsertNew(Math.Max(index, 0), string.Empty);
        }

        /// <summary>Вставить строку после выбранной (имя — следующий номер, если свободен).</summary>
        public void InsertAfter()
        {
            var selected = SelectedEntry;
            var index = selected != null ? Module.Items.IndexOf(selected) : -1;
            if (index < 0)
            {
                InsertNew(0, string.Empty);
                return;
            }

            var name = EntryTextUtils.GetNextName(selected!.Name);
            if (Module.Items.Any(e => e.Name == name))
                name = string.Empty;

            InsertNew(index + 1, name);
        }

        /// <summary>Импорт строк после выбранной: значения по одному на строку в порядке языков.</summary>
        public void ImportAfter(string text)
        {
            var rows = EntryTextUtils.ParseManualImport(text, Languages.Count);
            if (rows.Count == 0)
                return;

            var index = SelectedEntry != null ? Module.Items.IndexOf(SelectedEntry) + 1 : Module.Items.Count;
            if (index <= 0)
                index = Module.Items.Count;

            LocEntry? last = null;
            foreach (var row in rows)
            {
                var previousName = index > 0 ? Module.Items[index - 1].Name : string.Empty;
                var entry = CreateEntry(EntryTextUtils.GetNextName(previousName, Module.Items), row);
                _alwaysVisible.Add(entry);
                Module.Items.Insert(index++, entry);
                last = entry;
            }

            if (last != null)
                SelectAndScroll(last);
        }

        /// <summary>
        /// Автоимпорт строк с тегами: существующие ключи обновляются, новые вставляются
        /// после последней строки с тем же базовым именем.
        /// </summary>
        /// <returns>Теги, которые не удалось разместить.</returns>
        public List<string> ImportAuto(string text, bool autoNumerate)
        {
            var notCreated = new List<string>();
            LocEntry? last = null;

            foreach (var pair in EntryTextUtils.ParseTaggedImport(text, Languages.Count))
            {
                var values = pair.Value.Cast<string?>().ToArray();
                var existing = Module.Items.FirstOrDefault(e => e.Name == pair.Key);
                if (existing != null)
                {
                    existing.SetValues(values);
                    last = existing;
                    continue;
                }

                EntryTextUtils.SplitTag(pair.Key, out var baseName, out _);
                var index = FindLastIndexWithBaseName(baseName, out var number);
                if (index < 0)
                {
                    notCreated.Add(pair.Key);
                    continue;
                }

                var name = autoNumerate
                    ? EntryTextUtils.GetNextName(baseName + number, Module.Items)
                    : pair.Key;

                var entry = CreateEntry(name, values);
                _alwaysVisible.Add(entry);
                Module.Items.Insert(index + 1, entry);
                last = entry;
            }

            if (last != null)
                SelectAndScroll(last);

            return notCreated;
        }

        /// <summary>Копировать выбранную строку в буфер обмена.</summary>
        public void Copy()
        {
            if (SelectedEntry != null)
                Ui.SetClipboardText(EntryTextUtils.ToClipboardText(SelectedEntry, Languages));
        }

        /// <summary>
        /// Вставить строку из буфера обмена: если строка выбрана — значения записываются в неё
        /// (имя меняется, только если у выбранной строки оно пустое), иначе добавляется новая строка в конец.
        /// </summary>
        public void Paste()
        {
            if (!EntryTextUtils.TryParseClipboardText(Ui.GetClipboardText(), Languages, out var name, out var values))
            {
                Ui.ShowMessage("В буфере обмена нет скопированной строки перевода.");
                return;
            }

            var selected = SelectedEntry;
            if (selected != null)
            {
                if (selected.Name.Length == 0)
                    selected.Name = name;

                selected.SetValues(values);
                return;
            }

            var entry = CreateEntry(name, values);
            _alwaysVisible.Add(entry);
            Module.Items.Add(entry);
            SelectAndScroll(entry);
        }

        /// <summary>Удалить выбранную строку.</summary>
        public void Remove()
        {
            var selected = SelectedEntry;
            if (selected == null)
                return;

            if (!string.IsNullOrEmpty(selected.Comment))
            {
                Ui.ShowMessage("Позиции, отмеченные комментарием, удалять нельзя!", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            if (selected.OriginalName != null &&
                !Ui.AskYesNo("Нельзя удалять строки из локализации, если плагин уже в релизе! " +
                             "Такие строки следует отмечать комментарием с пометкой об удалении." +
                             Environment.NewLine + "Точно удалить?"))
            {
                return;
            }

            var index = Module.Items.IndexOf(selected);
            Module.RemoveItem(selected);
            _alwaysVisible.Remove(selected);

            if (Module.Items.Count > 0)
                SelectedEntry = Module.Items[Math.Min(index, Module.Items.Count - 1)];
        }

        /// <summary>Пометить выбранную строку на удаление.</summary>
        /// <param name="version">Версия или <c>null</c> — снять пометку.</param>
        public void SetDeletionMark(string? version)
        {
            var selected = SelectedEntry;
            if (selected == null)
                return;

            if (version != null)
            {
                selected.Comment = DeletionMarker.Build(version);
            }
            else if (selected.IsMarked)
            {
                // Снимается только пометка: чужой комментарий не трогаем
                selected.Comment = null;
            }
        }

        /// <summary>Переместить выбранную строку.</summary>
        /// <param name="direction">-1 — вверх, 1 — вниз.</param>
        public void Move(int direction)
        {
            var selected = SelectedEntry;
            if (selected == null)
                return;

            var from = Module.Items.IndexOf(selected);
            var to = FindMoveTarget(from, direction);
            if (to < 0)
                return;

            Module.Items.Move(from, to);
            SelectedEntry = selected;
            ScrollRequested?.Invoke(selected);
        }

        /// <summary>Можно ли переместить выбранную строку.</summary>
        /// <param name="direction">-1 — вверх, 1 — вниз.</param>
        public bool CanMove(int direction)
        {
            var selected = SelectedEntry;
            return selected != null && FindMoveTarget(Module.Items.IndexOf(selected), direction) >= 0;
        }

        #endregion

        /// <summary>
        /// Отписка при закрытии вкладки.
        /// </summary>
        public void Detach()
        {
            _filterTimer.Stop();
            Module.PropertyChanged -= OnModulePropertyChanged;
            Module.Attributes.CollectionChanged -= OnAttributesChanged;
        }

        private void CreateViews()
        {
            _alwaysVisible.Clear();
            var view = new ListCollectionView(Module.Items);
            if (!string.IsNullOrEmpty(_appliedFilter))
                view.Filter = FilterEntry;

            ItemsView = view;
            Module.Attributes.CollectionChanged -= OnAttributesChanged;
            Module.Attributes.CollectionChanged += OnAttributesChanged;
            UpdateAttributeOptions();
            OnPropertyChanged(nameof(AttributesHeader));
        }

        private void OnModulePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(LocModule.Items):
                    CreateViews();
                    break;
                case nameof(LocModule.Attributes):
                    CreateViews();
                    OnPropertyChanged(nameof(Module));
                    break;
                case nameof(LocModule.ModuleIssues):
                    OnPropertyChanged(nameof(AttributesHeader));
                    break;
            }
        }

        private void OnAttributesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateAttributeOptions();
            OnPropertyChanged(nameof(AttributesHeader));
        }

        private void UpdateAttributeOptions()
        {
            AttributeOptions = Module.HasPluginAttributes
                ? AttributeRules.GetAddOptions(Module.Attributes)
                : new List<AttributeOption>();
            SelectedAttributeOption = AttributeOptions.FirstOrDefault();
        }

        private void ApplyFilter()
        {
            var filter = _searchText.Trim();
            if (string.Equals(filter, _appliedFilter, StringComparison.Ordinal))
                return;

            _appliedFilter = filter;
            _alwaysVisible.Clear();

            using (_itemsView.DeferRefresh())
                _itemsView.Filter = filter.Length == 0 ? null : new Predicate<object>(FilterEntry);
        }

        private bool FilterEntry(object item)
        {
            if (item is not LocEntry entry)
                return false;

            return _alwaysVisible.Contains(entry) || Matches(entry, _appliedFilter);
        }

        /// <summary>
        /// Совпадение записи со строкой поиска: имя, значения и комментарий без учёта регистра.
        /// </summary>
        internal static bool Matches(LocEntry entry, string text, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (entry.Name.IndexOf(text, comparison) >= 0)
                return true;

            for (var i = 0; i < entry.LanguageCount; i++)
            {
                var value = entry.GetRawValue(i);
                if (value != null && value.IndexOf(text, comparison) >= 0)
                    return true;
            }

            return false;
        }

        private LocEntry CreateEntry(string name, string?[]? values)
        {
            var entry = new LocEntry(name, Languages.Count, isAttribute: false);
            for (var i = 0; i < Languages.Count; i++)
                entry.SetRawValue(i, values != null && i < values.Length ? values[i] ?? string.Empty : string.Empty);

            return entry;
        }

        private void InsertNew(int index, string name)
        {
            var entry = CreateEntry(name, null);
            _alwaysVisible.Add(entry);
            Module.Items.Insert(Math.Min(index, Module.Items.Count), entry);
            SelectAndScroll(entry);
        }

        private int FindLastIndexWithBaseName(string baseName, out int number)
        {
            number = -1;
            var index = -1;
            if (string.IsNullOrWhiteSpace(baseName))
                return -1;

            for (var i = 0; i < Module.Items.Count; i++)
            {
                EntryTextUtils.SplitTag(Module.Items[i].Name, out var value, out var n);
                if (!string.Equals(value, baseName, StringComparison.Ordinal))
                    continue;

                number = n;
                index = i;
            }

            return index;
        }

        /// <summary>
        /// Позиция для перемещения: соседняя видимая строка (при активном фильтре скрытые строки
        /// перескакиваются, иначе перемещение выглядело бы как «ничего не произошло»).
        /// </summary>
        private int FindMoveTarget(int from, int direction)
        {
            if (from < 0)
                return -1;

            var filterActive = _itemsView.Filter != null;
            for (var i = from + direction; i >= 0 && i < Module.Items.Count; i += direction)
            {
                if (!filterActive || _itemsView.PassesFilter(Module.Items[i]))
                    return i;
            }

            return -1;
        }

        private void AddAttribute()
        {
            var option = SelectedAttributeOption;
            if (option == null)
                return;

            LocEntry? last = null;
            foreach (var name in option.Names)
            {
                if (Module.Attributes.Any(a => a.Name == name))
                    continue;

                var entry = new LocEntry(name, Languages.Count, isAttribute: true);
                for (var i = 0; i < Languages.Count; i++)
                    entry.SetRawValue(i, string.Empty);

                Module.Attributes.Insert(AttributeRules.GetInsertIndex(Module.Attributes, name), entry);
                last = entry;
            }

            IsAttributesExpanded = true;
            if (last != null)
                SelectedAttribute = last;
        }

        private void RemoveAttribute()
        {
            var attribute = SelectedAttribute;
            if (attribute == null)
                return;

            var removal = AttributeRules.GetRemovalSet(Module.Attributes, attribute, out var reason);
            if (removal.Count == 0)
            {
                Ui.ShowMessage(reason ?? "Атрибут не может быть удалён.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var names = string.Join(", ", removal.Select(a => a.Name));
            if (!Ui.AskYesNo($"Удалить атрибут(ы) {names} из всех языков?"))
                return;

            foreach (var entry in removal)
                Module.Attributes.Remove(entry);
        }
    }
}
