using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

using ModPlusVSTools.Localization.Services;

namespace ModPlusVSTools.Localization.Models
{
    /// <summary>
    /// Состояние загрузки записей модуля.
    /// </summary>
    internal enum ModuleLoadState
    {
        /// <summary>Записи не загружены.</summary>
        NotLoaded,

        /// <summary>Идёт загрузка.</summary>
        Loading,

        /// <summary>Записи загружены.</summary>
        Loaded,
    }

    /// <summary>
    /// Узел локализации (узел плагина в xml-файле, например <c>mprAlignViews</c>, или раздел <c>Common</c>).
    /// </summary>
    internal sealed class LocModule : ObservableBase
    {
        private ObservableCollection<LocEntry> _items = new ObservableCollection<LocEntry>();
        private ObservableCollection<LocEntry> _attributes = new ObservableCollection<LocEntry>();
        private ModuleLoadState _loadState;
        private bool _isDirty;
        private bool _hasErrors;
        private int _entryCount;
        private string? _moduleIssues;
        private bool _validationPending;
        private bool _suppressChanges;

        /// <summary>
        /// Создаёт модуль.
        /// </summary>
        /// <param name="name">Имя узла.</param>
        /// <param name="domain">Группа (Revit, AutoCAD, Common…).</param>
        /// <param name="sourceKey">Языконезависимый ключ файла (<c>Revit_mprAlignViews</c>, <c>Common</c>).</param>
        /// <param name="languages">Языки редактора.</param>
        public LocModule(string name, LocDomain domain, string sourceKey, IReadOnlyList<string> languages)
        {
            Name = name;
            Domain = domain;
            SourceKey = sourceKey;
            Languages = languages;
            Subscribe();
            _attributes.CollectionChanged += OnCollectionChanged;
        }

        /// <summary>Имя узла.</summary>
        public string Name { get; }

        /// <summary>Группа.</summary>
        public LocDomain Domain { get; }

        /// <summary>Языконезависимый ключ файла.</summary>
        public string SourceKey { get; }

        /// <summary>Языки редактора.</summary>
        public IReadOnlyList<string> Languages { get; }

        /// <summary>
        /// Пути к файлам по языкам (только существующие на диске). Новый модуль файлов не имеет
        /// до первого сохранения.
        /// </summary>
        public Dictionary<string, string> FilesByLanguage { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Файл источника называется без суффикса языка (<c>Common.xml</c>), а не <c>{ключ}_{язык}.xml</c>.
        /// </summary>
        public bool UsesSharedFileName { get; set; }

        /// <summary>Модуль создан в редакторе и ещё не сохранён.</summary>
        public bool IsNew { get; set; }

        /// <summary>Правила атрибутов узла плагина применяются (не для разделов Common).</summary>
        public bool HasPluginAttributes => !Domain.IsCommon;

        /// <summary>Строки (дочерние элементы узла).</summary>
        public ObservableCollection<LocEntry> Items
        {
            get => _items;
            private set
            {
                Unsubscribe();
                _items = value;
                Subscribe();
                OnPropertyChanged();
            }
        }

        /// <summary>Атрибуты узла.</summary>
        public ObservableCollection<LocEntry> Attributes
        {
            get => _attributes;
            private set
            {
                _attributes.CollectionChanged -= OnCollectionChanged;
                _attributes = value;
                _attributes.CollectionChanged += OnCollectionChanged;
                OnPropertyChanged();
            }
        }

        /// <summary>Состояние загрузки.</summary>
        public ModuleLoadState LoadState
        {
            get => _loadState;
            set
            {
                if (SetField(ref _loadState, value))
                    OnPropertyChanged(nameof(IsLoaded));
            }
        }

        /// <summary>Записи загружены.</summary>
        public bool IsLoaded => _loadState == ModuleLoadState.Loaded;

        /// <summary>Есть несохранённые изменения.</summary>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetField(ref _isDirty, value))
                    OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary>Счётчик изменений: по нему после сохранения понятно, не менялся ли модуль во время записи.</summary>
        public int ChangeVersion { get; private set; }

        /// <summary>Есть ошибки (сохранение модуля невозможно).</summary>
        public bool HasErrors
        {
            get => _hasErrors;
            private set => SetField(ref _hasErrors, value);
        }

        /// <summary>Ошибки уровня узла (обязательные атрибуты и т.п.) или <c>null</c>.</summary>
        public string? ModuleIssues
        {
            get => _moduleIssues;
            private set => SetField(ref _moduleIssues, value);
        }

        /// <summary>Число строк (до загрузки — по данным каталога).</summary>
        public int EntryCount
        {
            get => _entryCount;
            set => SetField(ref _entryCount, value);
        }

        /// <summary>Заголовок для дерева и вкладки.</summary>
        public string Title => _isDirty ? Name + " *" : Name;

        /// <summary>Полное имя для подсказок: группа и ключ файла.</summary>
        public string FullTitle => SourceKey == Name ? SourceKey : SourceKey + " / " + Name;

        /// <summary>Имена строк, удалённых в редакторе (по именам на диске).</summary>
        public HashSet<string> RemovedItemNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Устанавливает загруженные с диска записи. Коллекции заменяются целиком —
        /// таблица получает одно уведомление Reset вместо тысяч Add.
        /// </summary>
        /// <param name="attributes">Атрибуты.</param>
        /// <param name="items">Строки.</param>
        public void SetLoadedData(List<LocEntry> attributes, List<LocEntry> items)
        {
            _suppressChanges = true;
            try
            {
                foreach (var entry in attributes)
                    entry.Owner = this;
                foreach (var entry in items)
                    entry.Owner = this;

                Attributes = new ObservableCollection<LocEntry>(attributes);
                Items = new ObservableCollection<LocEntry>(items);
                RemovedItemNames.Clear();
                EntryCount = items.Count;
                LoadState = ModuleLoadState.Loaded;
                IsDirty = IsNew;
                ValidateNow();
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        /// <summary>
        /// Сбрасывает загруженные данные (перечитывание с диска).
        /// </summary>
        public void Unload()
        {
            _suppressChanges = true;
            try
            {
                Attributes = new ObservableCollection<LocEntry>();
                Items = new ObservableCollection<LocEntry>();
                RemovedItemNames.Clear();
                LoadState = ModuleLoadState.NotLoaded;
                IsDirty = false;
                HasErrors = false;
                ModuleIssues = null;
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        /// <summary>
        /// Удаляет строку. Строка, существующая на диске, удаляется из файлов при сохранении.
        /// </summary>
        /// <param name="entry">Строка.</param>
        public void RemoveItem(LocEntry entry)
        {
            if (!_items.Remove(entry))
                return;

            if (!string.IsNullOrEmpty(entry.OriginalName))
                RemovedItemNames.Add(entry.OriginalName!);

            entry.Owner = null;
        }

        /// <summary>
        /// Вызывается при изменении записи модуля.
        /// </summary>
        /// <param name="entry">Изменённая запись.</param>
        public void OnEntryChanged(LocEntry entry)
        {
            if (_suppressChanges)
                return;

            MarkChanged();
        }

        /// <summary>
        /// Отмечает успешное сохранение.
        /// </summary>
        /// <param name="savedVersion">Значение <see cref="ChangeVersion"/> на момент снимка.</param>
        /// <param name="removedNames">Удалённые строки, записанные в файлы (удалённые во время записи остаются).</param>
        public void MarkSaved(int savedVersion, IEnumerable<string> removedNames)
        {
            IsNew = false;
            foreach (var name in removedNames)
                RemovedItemNames.Remove(name);

            if (ChangeVersion == savedVersion)
                IsDirty = false;
        }

        /// <summary>
        /// Выполняет проверку записей немедленно.
        /// </summary>
        public void ValidateNow()
        {
            _validationPending = false;
            var result = EntryValidator.Validate(this);
            ModuleIssues = result.ModuleIssues;
            HasErrors = result.HasErrors;
        }

        private void MarkChanged()
        {
            ChangeVersion++;
            IsDirty = true;
            ScheduleValidation();
        }

        /// <summary>
        /// Проверка откладывается и объединяется: импорт десятков строк или вставка значений
        /// по всем языкам вызывают одну проверку, а не проверку на каждое изменение.
        /// </summary>
        private void ScheduleValidation()
        {
            if (_validationPending)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ValidateNow();
                return;
            }

            _validationPending = true;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_validationPending)
                    ValidateNow();
            }));
        }

        private void Subscribe()
        {
            _items.CollectionChanged += OnCollectionChanged;
        }

        private void Unsubscribe()
        {
            _items.CollectionChanged -= OnCollectionChanged;
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LocEntry entry in e.NewItems)
                    entry.Owner = this;
            }

            if (ReferenceEquals(sender, _items))
                EntryCount = _items.Count;

            if (_suppressChanges)
                return;

            MarkChanged();
        }
    }
}
