using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using ModPlusVSTools.Localization.Models;
using ModPlusVSTools.Localization.Services;
using ModPlusVSTools.Localization.UI;
using ModPlusVSTools.Services;

using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Localization.ViewModels
{
    /// <summary>
    /// Модель редактора исходных файлов локализации.
    /// </summary>
    internal sealed class EditorViewModel : ObservableBase
    {
        private readonly ModPlusVSToolsPackage _package;
        private readonly Dictionary<LocModule, Task> _loadingTasks = new Dictionary<LocModule, Task>();
        private readonly DispatcherTimer _treeFilterTimer;
        private ModuleTabViewModel? _selectedTab;
        private IReadOnlyList<string> _languages = Array.Empty<string>();
        private string? _root;
        private string _status = string.Empty;
        private string _treeFilter = string.Empty;
        private bool _isBusy;
        private bool _isSearchPageActive;
        private string? _lastDeletionVersion;
        private CancellationTokenSource? _catalogCts;
        private Task<bool>? _currentSave;

        /// <summary>
        /// Создаёт модель редактора.
        /// </summary>
        /// <param name="package">Пакет расширения.</param>
        public EditorViewModel(ModPlusVSToolsPackage package)
        {
            _package = package;
            Search = new SearchViewModel(this);

            _treeFilterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _treeFilterTimer.Tick += (_, _) =>
            {
                _treeFilterTimer.Stop();
                ApplyTreeFilter();
            };

            ReloadCommand = new RelayCommand(() => Run(() => ReloadAsync(askOnUnsaved: true)), () => !IsBusy);
            SaveCommand = new RelayCommand(() => Run(async () => await SaveAllAsync()), () => !IsBusy && HasUnsavedChanges);
            NewModuleCommand = new RelayCommand(NewModule, () => !IsBusy && _root != null);
            BuildCommand = new RelayCommand(
                () => Run(async () => await LocalizationBuildService.BuildAsync(_package)),
                () => !LocalizationBuildService.IsRunning);
            OpenSettingsCommand = new RelayCommand(() => _package.ShowOptionPage(typeof(ModPlusVSToolsOptionsPage)));
            ShowMainPageCommand = new RelayCommand(() => IsSearchPageActive = false);
            ShowSearchPageCommand = new RelayCommand(() => IsSearchPageActive = true);

            InsertBeforeCommand = TabCommand(tab => tab.InsertBefore());
            InsertAfterCommand = TabCommand(tab => tab.InsertAfter());
            ImportAfterCommand = TabCommand(ImportAfter);
            ImportAutoCommand = TabCommand(ImportAuto);
            CopyCommand = TabCommand(tab => tab.Copy(), tab => tab.SelectedEntry != null);
            PasteCommand = TabCommand(tab => tab.Paste());
            RemoveCommand = TabCommand(tab => tab.Remove(), tab => tab.SelectedEntry != null);
            MarkForDeletionCommand = TabCommand(MarkForDeletion, tab => tab.SelectedEntry != null);
            MoveUpCommand = TabCommand(tab => tab.Move(-1), tab => tab.CanMove(-1));
            MoveDownCommand = TabCommand(tab => tab.Move(1), tab => tab.CanMove(1));
        }

        /// <summary>
        /// Запрос завершить редактирование ячеек таблиц: кнопки панели инструментов не забирают фокус,
        /// и без этого значение, набираемое в ячейке, не попало бы в сохранение или команду.
        /// </summary>
        public event Action? CommitEditsRequested;

        /// <summary>Группы узлов (дерево).</summary>
        public ObservableCollection<LocDomain> Domains { get; } = new ObservableCollection<LocDomain>();

        /// <summary>Открытые вкладки узлов.</summary>
        public ObservableCollection<ModuleTabViewModel> OpenTabs { get; } = new ObservableCollection<ModuleTabViewModel>();

        /// <summary>Глобальный поиск.</summary>
        public SearchViewModel Search { get; }

        /// <summary>Языки (столбцы таблиц).</summary>
        public IReadOnlyList<string> Languages
        {
            get => _languages;
            private set => SetField(ref _languages, value);
        }

        /// <summary>Папка исходных файлов локализации.</summary>
        public string? Root
        {
            get => _root;
            private set => SetField(ref _root, value);
        }

        /// <summary>Активная вкладка.</summary>
        public ModuleTabViewModel? SelectedTab
        {
            get => _selectedTab;
            set
            {
                var previous = _selectedTab;
                if (!SetField(ref _selectedTab, value))
                    return;

                if (previous != null)
                    previous.IsSelected = false;
                if (value != null)
                    value.IsSelected = true;
            }
        }

        /// <summary>Строка состояния.</summary>
        public string Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        /// <summary>Идёт длительная операция.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        /// <summary>Активна вкладка «Поиск».</summary>
        public bool IsSearchPageActive
        {
            get => _isSearchPageActive;
            set => SetField(ref _isSearchPageActive, value);
        }

        /// <summary>Фильтр дерева по имени узла.</summary>
        public string TreeFilter
        {
            get => _treeFilter;
            set
            {
                if (!SetField(ref _treeFilter, value ?? string.Empty))
                    return;

                _treeFilterTimer.Stop();
                _treeFilterTimer.Start();
            }
        }

        /// <summary>Есть несохранённые изменения.</summary>
        public bool HasUnsavedChanges => Domains.Any(d => d.Modules.Any(m => m.IsDirty));

        /// <summary>Модули всех групп.</summary>
        public IEnumerable<LocModule> AllModules => Domains.SelectMany(d => d.Modules);

#pragma warning disable SA1600 // команды описаны подсказками кнопок
        public ICommand ReloadCommand { get; }

        public ICommand SaveCommand { get; }

        public ICommand NewModuleCommand { get; }

        public ICommand BuildCommand { get; }

        public ICommand OpenSettingsCommand { get; }

        public ICommand ShowMainPageCommand { get; }

        public ICommand ShowSearchPageCommand { get; }

        public ICommand InsertBeforeCommand { get; }

        public ICommand InsertAfterCommand { get; }

        public ICommand ImportAfterCommand { get; }

        public ICommand ImportAutoCommand { get; }

        public ICommand CopyCommand { get; }

        public ICommand PasteCommand { get; }

        public ICommand RemoveCommand { get; }

        public ICommand MarkForDeletionCommand { get; }

        public ICommand MoveUpCommand { get; }

        public ICommand MoveDownCommand { get; }
#pragma warning restore SA1600

        /// <summary>
        /// Завершает редактирование ячеек во всех таблицах.
        /// </summary>
        public void CommitEdits() => CommitEditsRequested?.Invoke();

        /// <summary>
        /// Первая загрузка (при открытии окна).
        /// </summary>
        public void Initialize()
        {
            if (Root == null && !IsBusy)
                Run(() => ReloadAsync(askOnUnsaved: false));
        }

        /// <summary>
        /// Перечитывает каталог с диска. Открытые вкладки восстанавливаются.
        /// </summary>
        /// <param name="askOnUnsaved">Спрашивать, если есть несохранённые изменения.</param>
        public async Task ReloadAsync(bool askOnUnsaved)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (askOnUnsaved && HasUnsavedChanges &&
                !Ui.AskYesNo("Есть несохранённые изменения. Перечитать файлы с диска и потерять изменения?"))
            {
                return;
            }

            var configured = _package.OptionsPage.BaseLanguageFilesPath;
            var error = LocalizationPathValidator.Validate(configured);
            if (error != null)
            {
                Status = error + " Укажите папку исходных файлов локализации в настройках.";
                Root = null;
                return;
            }

            var root = LocalizationFolder.NormalizeRoot(configured);
            var reopen = OpenTabs.Select(t => (t.Module.SourceKey, t.Module.Name)).ToList();
            var selected = SelectedTab != null ? (SelectedTab.Module.SourceKey, SelectedTab.Module.Name) : default;

            _catalogCts?.Cancel();
            var cts = _catalogCts = new CancellationTokenSource();

            IsBusy = true;
            Status = "Чтение файлов локализации...";
            try
            {
                var catalog = await Task.Run(() => LocalizationRepository.LoadCatalog(root, cts.Token), cts.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (cts.IsCancellationRequested)
                    return;

                ApplyCatalog(root, catalog);

                foreach (var key in reopen)
                {
                    var module = FindModule(key.SourceKey, key.Name);
                    if (module != null)
                        OpenModule(module, select: key == selected);
                }

                Status = catalog.Errors.Count == 0
                    ? $"Узлов: {AllModules.Count()}, языков: {Languages.Count}. {root}"
                    : $"Не удалось прочитать файлов: {catalog.Errors.Count} — {catalog.Errors[0]}";
            }
            catch (OperationCanceledException)
            {
                // перезапущено новым чтением
            }
            finally
            {
                if (ReferenceEquals(_catalogCts, cts))
                    IsBusy = false;
            }
        }

        /// <summary>
        /// Открывает узел во вкладке (или активирует уже открытую).
        /// </summary>
        /// <param name="module">Узел.</param>
        /// <param name="select">Сделать вкладку активной.</param>
        public ModuleTabViewModel OpenModule(LocModule module, bool select = true)
        {
            var tab = OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Module, module));
            if (tab == null)
            {
                tab = new ModuleTabViewModel(this, module);
                OpenTabs.Add(tab);
            }

            if (select)
            {
                SelectedTab = tab;
                IsSearchPageActive = false;
            }

            Run(() => EnsureLoadedAsync(module));
            return tab;
        }

        /// <summary>
        /// Закрывает вкладку. Несохранённые изменения узла остаются в памяти (узел помечен «*» в дереве).
        /// </summary>
        /// <param name="tab">Вкладка.</param>
        public void CloseTab(ModuleTabViewModel tab)
        {
            var index = OpenTabs.IndexOf(tab);
            if (index < 0)
                return;

            OpenTabs.RemoveAt(index);
            tab.Detach();

            if (ReferenceEquals(SelectedTab, tab))
                SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
        }

        /// <summary>
        /// Загружает записи узла, если они ещё не загружены.
        /// </summary>
        /// <param name="module">Узел.</param>
        public async Task EnsureLoadedAsync(LocModule module)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (module.LoadState == ModuleLoadState.Loaded)
                return;

            if (_loadingTasks.TryGetValue(module, out var pending))
            {
                await pending;
                return;
            }

            // Узел загружается пакетно для глобального поиска — данные придут оттуда
            if (module.LoadState == ModuleLoadState.Loading)
                return;

            var task = LoadModuleAsync(module);
            _loadingTasks[module] = task;
            await task;
        }

        /// <summary>
        /// Загружает записи всех узлов (для глобального поиска). Файлы читаются параллельно,
        /// записи передаются в UI-поток порциями.
        /// </summary>
        /// <param name="progress">Прогресс: число загруженных узлов.</param>
        /// <param name="cancellationToken">Отмена.</param>
        public async Task LoadAllAsync(IProgress<int>? progress, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var pending = AllModules.Where(m => m.LoadState == ModuleLoadState.NotLoaded).ToList();
            var waiting = _loadingTasks.Values.ToList();
            if (pending.Count == 0)
            {
                await Task.WhenAll(waiting);
                return;
            }

            foreach (var module in pending)
                module.LoadState = ModuleLoadState.Loading;

            var languages = Languages;
            var requests = pending
                .Select(m => (Module: m, Files: new Dictionary<string, string>(m.FilesByLanguage, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            (LocModule Module, List<LocEntry>? Attributes, List<LocEntry>? Items, string? Error)[] loaded;
            try
            {
                loaded = await Task.Run(
                () =>
                {
                    var result = new (LocModule Module, List<LocEntry>? Attributes, List<LocEntry>? Items, string? Error)[requests.Count];
                    Parallel.For(
                        0,
                        requests.Count,
                        new ParallelOptions { CancellationToken = cancellationToken },
                        i =>
                        {
                            var request = requests[i];
                            try
                            {
                                LocalizationRepository.LoadModule(request.Module.Name, request.Files, languages, out var attributes, out var items);
                                result[i] = (request.Module, attributes, items, null);
                            }
                            catch (Exception exception)
                            {
                                result[i] = (request.Module, null, null, exception.Message);
                            }
                        });
                    return result;
                },
                cancellationToken);
            }
            catch
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                foreach (var module in pending)
                {
                    if (module.LoadState == ModuleLoadState.Loading)
                        module.LoadState = ModuleLoadState.NotLoaded;
                }

                throw;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var count = 0;
            foreach (var item in loaded)
            {
                if (item.Module == null)
                    continue;

                if (item.Error != null || item.Items == null || item.Attributes == null)
                {
                    item.Module.LoadState = ModuleLoadState.NotLoaded;
                    Status = $"Ошибка чтения узла {item.Module.FullTitle}: {item.Error}";
                }
                else if (item.Module.LoadState == ModuleLoadState.Loading)
                {
                    item.Module.SetLoadedData(item.Attributes, item.Items);
                }

                if (++count % 20 == 0)
                {
                    progress?.Report(count);

                    // Даём интерфейсу отрисоваться между порциями
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            await Task.WhenAll(waiting);
            progress?.Report(count);
        }

        /// <summary>
        /// Сохраняет все изменённые узлы. Узлы с ошибками не сохраняются.
        /// </summary>
        /// <returns><c>true</c>, если сохранены все изменения.</returns>
        public async Task<bool> SaveAllAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Сохранения выполняются строго по очереди: иначе два фоновых сохранения
            // (кнопка, сборка, закрытие VS) переписывали бы одни и те же файлы одновременно
            while (_currentSave != null)
            {
                await _currentSave;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }

            var save = SaveAllCoreAsync();
            _currentSave = save;
            try
            {
                return await save;
            }
            finally
            {
                if (ReferenceEquals(_currentSave, save))
                    _currentSave = null;
            }
        }

        private async Task<bool> SaveAllCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var root = Root;
            if (root == null)
                return false;

            CommitEdits();
            var dirty = AllModules.Where(m => m.IsDirty && m.IsLoaded).ToList();
            if (dirty.Count == 0)
                return true;

            foreach (var module in dirty)
                module.ValidateNow();

            var invalid = dirty.Where(m => m.HasErrors).ToList();
            var valid = dirty.Where(m => !m.HasErrors).ToList();
            var snapshots = valid.Select(LocalizationRepository.CreateSnapshot).ToList();
            var languages = Languages;

            IsBusy = true;
            Status = "Сохранение...";
            var errors = new List<string>();
            var written = 0;
            try
            {
                var keys = valid.Select(m => (m.UsesSharedFileName, m.SourceKey, m.Name)).ToList();
                written = await Task.Run(() =>
                {
                    var total = 0;
                    for (var i = 0; i < snapshots.Count; i++)
                    {
                        try
                        {
                            total += LocalizationRepository.Save(root, snapshots[i], languages, keys[i].UsesSharedFileName, keys[i].SourceKey, keys[i].Name);
                        }
                        catch (Exception exception)
                        {
                            lock (errors)
                                errors.Add($"{keys[i].SourceKey}/{keys[i].Name}: {exception.Message}");
                            snapshots[i] = null!;
                        }
                    }

                    return total;
                });

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                foreach (var snapshot in snapshots)
                {
                    if (snapshot == null)
                        continue;

                    foreach (var entry in snapshot.Items.Concat(snapshot.Attributes))
                        entry.Entry.OriginalName = entry.Name;

                    foreach (var pair in snapshot.SavedFiles)
                        snapshot.Module.FilesByLanguage[pair.Key] = pair.Value;

                    snapshot.Module.MarkSaved(snapshot.Version, snapshot.RemovedItems);
                }
            }
            finally
            {
                IsBusy = false;
            }

            var message = $"Сохранено узлов: {valid.Count - errors.Count}, перезаписано файлов: {written}.";
            if (invalid.Count > 0)
                message += " Не сохранены (есть ошибки): " + string.Join(", ", invalid.Select(m => m.Name)) + ".";
            if (errors.Count > 0)
                message += " Ошибки записи: " + string.Join("; ", errors);

            Status = message;

            if (invalid.Count > 0 || errors.Count > 0)
            {
                Ui.ShowMessage(
                    message + Environment.NewLine + Environment.NewLine +
                    "Строки с ошибками подсвечены красным, описание ошибки — в подсказке строки.",
                    OLEMSGICON.OLEMSGICON_WARNING);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Файлы изменены вне редактора (сборка исправила кавычки): редактор без изменений перечитывает их.
        /// </summary>
        public void OnSourcesChangedExternally()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Root == null)
                return;

            if (HasUnsavedChanges)
            {
                Status = "Файлы локализации могли измениться при сборке (исправление кавычек). " +
                         "В редакторе есть несохранённые изменения — перечитайте файлы после сохранения.";
                return;
            }

            Run(() => ReloadAsync(askOnUnsaved: false));
        }

        /// <summary>
        /// Находит узел.
        /// </summary>
        public LocModule? FindModule(string sourceKey, string name) =>
            AllModules.FirstOrDefault(m => m.SourceKey == sourceKey && m.Name == name);

        /// <summary>
        /// Выполняет асинхронную операцию с выводом ошибки.
        /// </summary>
        internal void Run(Func<Task> action)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await action();
                }
                catch (OperationCanceledException)
                {
                    // отменено
                }
                catch (Exception exception)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    Status = "Ошибка: " + exception.Message;
                    Ui.ShowMessage(exception.ToString(), OLEMSGICON.OLEMSGICON_CRITICAL);
                }
            }).FileAndForget("ModPlusVSTools/LocalizationEditor");
        }

        private async Task LoadModuleAsync(LocModule module)
        {
            module.LoadState = ModuleLoadState.Loading;
            var files = new Dictionary<string, string>(module.FilesByLanguage, StringComparer.OrdinalIgnoreCase);
            var languages = Languages;
            try
            {
                List<LocEntry> attributes = null!;
                List<LocEntry> items = null!;
                await Task.Run(() => LocalizationRepository.LoadModule(module.Name, files, languages, out attributes, out items));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (module.LoadState == ModuleLoadState.Loading)
                    module.SetLoadedData(attributes, items);
            }
            catch
            {
                module.LoadState = ModuleLoadState.NotLoaded;
                throw;
            }
            finally
            {
                _loadingTasks.Remove(module);
            }
        }

        private void ApplyCatalog(string root, CatalogData catalog)
        {
            foreach (var tab in OpenTabs)
                tab.Detach();

            OpenTabs.Clear();
            SelectedTab = null;
            _loadingTasks.Clear();
            Search.Clear();

            Root = root;
            Languages = catalog.Languages;

            var domains = new Dictionary<string, LocDomain>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in catalog.Modules)
            {
                if (!domains.TryGetValue(info.DomainName, out var domain))
                {
                    domain = new LocDomain(info.DomainName);
                    domains[info.DomainName] = domain;
                }

                var module = new LocModule(info.Name, domain, info.SourceKey, catalog.Languages)
                {
                    UsesSharedFileName = info.UsesSharedFileName,
                    EntryCount = info.EntryCount,
                };

                foreach (var file in info.Files)
                    module.FilesByLanguage[file.Key] = file.Value;

                domain.Modules.Add(module);
            }

            Domains.Clear();
            foreach (var domain in domains.Values
                         .OrderBy(d => LocalizationFolder.GetDomainOrder(d.Name))
                         .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var sorted = domain.Modules.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
                domain.Modules.Clear();
                foreach (var module in sorted)
                    domain.Modules.Add(module);

                Domains.Add(domain);
            }

            ApplyTreeFilter();
        }

        private void ApplyTreeFilter()
        {
            var filter = _treeFilter.Trim();
            foreach (var domain in Domains)
            {
                var view = CollectionViewSource.GetDefaultView(domain.Modules);
                view.Filter = filter.Length == 0
                    ? null
                    : item => item is LocModule module && module.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

                if (filter.Length > 0)
                    domain.IsExpanded = true;
            }
        }

        private ICommand TabCommand(Action<ModuleTabViewModel> execute, Func<ModuleTabViewModel, bool>? canExecute = null)
        {
            return new RelayCommand(
                () =>
                {
                    var tab = SelectedTab;
                    if (tab == null || !tab.Module.IsLoaded)
                        return;

                    CommitEdits();
                    execute(tab);
                },
                () =>
                {
                    var tab = SelectedTab;
                    return !IsSearchPageActive && tab != null && tab.Module.IsLoaded && (canExecute == null || canExecute(tab));
                });
        }

        private void ImportAfter(ModuleTabViewModel tab)
        {
            var dialog = new ImportDialog(tab.Languages, withTags: false);
            if (dialog.ShowModal() == true)
                tab.ImportAfter(dialog.ImportText);
        }

        private void ImportAuto(ModuleTabViewModel tab)
        {
            var dialog = new ImportDialog(tab.Languages, withTags: true);
            if (dialog.ShowModal() != true)
                return;

            var notCreated = tab.ImportAuto(dialog.ImportText, dialog.AutoNumerate);
            if (notCreated.Count > 0)
            {
                Ui.ShowMessage(
                    "Не найдено место для вставки строк с тегами (в узле нет строк с таким же базовым именем): " +
                    string.Join(", ", notCreated),
                    OLEMSGICON.OLEMSGICON_WARNING);
            }
        }

        private void MarkForDeletion(ModuleTabViewModel tab)
        {
            var entry = tab.SelectedEntry;
            if (entry == null)
                return;

            var dialog = new MarkForDeletionDialog(entry, _lastDeletionVersion);
            if (dialog.ShowModal() != true)
                return;

            if (dialog.Unmark)
            {
                tab.SetDeletionMark(null);
                return;
            }

            _lastDeletionVersion = dialog.Version;
            tab.SetDeletionMark(dialog.Version);
        }

        private void NewModule()
        {
            var root = Root;
            if (root == null)
                return;

            var dialog = new NewModuleDialog(name =>
            {
                if (AllModules.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return $"Узел «{name}» уже существует.";

                return null;
            });

            if (dialog.ShowModal() != true)
                return;

            var program = dialog.Program;
            var name = dialog.ModuleName;
            var sourceKey = program + LocalizationFolder.DomainSeparator + name;

            if (LocalizationRepository.SourceExists(root, Languages, sourceKey))
            {
                Ui.ShowMessage($"Файлы {sourceKey}_*.xml уже существуют. Перечитайте файлы локализации.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var domain = Domains.FirstOrDefault(d => string.Equals(d.Name, program, StringComparison.OrdinalIgnoreCase));
            if (domain == null)
            {
                domain = new LocDomain(program);
                var index = 0;
                while (index < Domains.Count && LocalizationFolder.GetDomainOrder(Domains[index].Name) <= LocalizationFolder.GetDomainOrder(program))
                    index++;

                Domains.Insert(index, domain);
            }

            var module = new LocModule(name, domain, sourceKey, Languages) { IsNew = true };

            var attributes = new List<LocEntry>();
            foreach (var attributeName in new[] { AttributeRules.LocalName, AttributeRules.Description })
            {
                var attribute = new LocEntry(attributeName, Languages.Count, isAttribute: true);
                for (var i = 0; i < Languages.Count; i++)
                    attribute.SetRawValue(i, string.Empty);

                attributes.Add(attribute);
            }

            module.SetLoadedData(attributes, new List<LocEntry>());
            domain.InsertSorted(module);
            domain.IsExpanded = true;

            var tab = OpenModule(module);
            tab.IsAttributesExpanded = true;
            Status = $"Создан узел {sourceKey}. Заполните атрибуты LocalName и Description и сохраните — " +
                     $"файлы будут созданы во всех языках ({string.Join(", ", Languages.Select(l => Path.Combine(l, sourceKey + "_" + LocalizationFolder.GetLanguageSuffix(l) + ".xml")).Take(2))}, …).";
        }
    }
}
