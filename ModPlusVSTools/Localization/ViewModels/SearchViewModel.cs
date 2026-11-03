using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Microsoft.VisualStudio.Shell;

using ModPlusVSTools.Localization.Models;

using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Localization.ViewModels
{
    /// <summary>
    /// Строка результатов глобального поиска: заголовок узла или найденная запись.
    /// </summary>
    internal sealed class SearchRow
    {
        private SearchRow(LocModule module, LocEntry? entry)
        {
            Module = module;
            Entry = entry;
        }

        /// <summary>Узел.</summary>
        public LocModule Module { get; }

        /// <summary>Запись или <c>null</c> для заголовка.</summary>
        public LocEntry? Entry { get; }

        /// <summary>Строка-заголовок узла.</summary>
        public bool IsHeader => Entry == null;

        /// <summary>Текст заголовка.</summary>
        public string HeaderText => Module.Domain.Name + "  ›  " + Module.FullTitle;

        /// <summary>Пометка вида записи.</summary>
        public string? Kind => Entry?.IsAttribute == true ? "атрибут" : null;

        /// <summary>Создаёт заголовок.</summary>
        public static SearchRow Header(LocModule module) => new SearchRow(module, null);

        /// <summary>Создаёт строку записи.</summary>
        public static SearchRow Item(LocModule module, LocEntry entry) => new SearchRow(module, entry);
    }

    /// <summary>
    /// Глобальный поиск по всем узлам файлов локализации.
    /// </summary>
    internal sealed class SearchViewModel : ObservableBase
    {
        private const int MaxResults = 5000;

        private readonly EditorViewModel _editor;
        private IReadOnlyList<SearchRow> _results = Array.Empty<SearchRow>();
        private SearchRow? _selectedRow;
        private CancellationTokenSource? _cts;
        private string _query = string.Empty;
        private string _status = "Введите текст и нажмите «Найти» (Enter). Поиск выполняется по именам ключей и значениям всех языков во всех узлах.";
        private bool _caseSensitive;
        private bool _includeAttributes = true;
        private bool _includeNames = true;
        private bool _isSearching;

        /// <summary>
        /// Создаёт модель поиска.
        /// </summary>
        public SearchViewModel(EditorViewModel editor)
        {
            _editor = editor;
            SearchCommand = new RelayCommand(() => _editor.Run(SearchAsync), () => !string.IsNullOrWhiteSpace(Query) && _editor.Root != null);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsSearching);
            OpenCommand = new RelayCommand(_ => OpenSelected(), _ => SelectedRow != null);
        }

        /// <summary>Строка поиска.</summary>
        public string Query
        {
            get => _query;
            set => SetField(ref _query, value ?? string.Empty);
        }

        /// <summary>Учитывать регистр.</summary>
        public bool CaseSensitive
        {
            get => _caseSensitive;
            set => SetField(ref _caseSensitive, value);
        }

        /// <summary>Искать в атрибутах узлов.</summary>
        public bool IncludeAttributes
        {
            get => _includeAttributes;
            set => SetField(ref _includeAttributes, value);
        }

        /// <summary>Искать в именах ключей.</summary>
        public bool IncludeNames
        {
            get => _includeNames;
            set => SetField(ref _includeNames, value);
        }

        /// <summary>Идёт поиск.</summary>
        public bool IsSearching
        {
            get => _isSearching;
            private set => SetField(ref _isSearching, value);
        }

        /// <summary>Результаты.</summary>
        public IReadOnlyList<SearchRow> Results
        {
            get => _results;
            private set => SetField(ref _results, value);
        }

        /// <summary>Выбранная строка.</summary>
        public SearchRow? SelectedRow
        {
            get => _selectedRow;
            set => SetField(ref _selectedRow, value);
        }

        /// <summary>Состояние.</summary>
        public string Status
        {
            get => _status;
            private set => SetField(ref _status, value);
        }

        /// <summary>Найти.</summary>
        public ICommand SearchCommand { get; }

        /// <summary>Отменить поиск.</summary>
        public ICommand CancelCommand { get; }

        /// <summary>Открыть узел выбранной строки во вкладке «Основное».</summary>
        public ICommand OpenCommand { get; }

        /// <summary>
        /// Сбрасывает результаты (при перечитывании файлов).
        /// </summary>
        public void Clear()
        {
            _cts?.Cancel();
            Results = Array.Empty<SearchRow>();
            SelectedRow = null;
        }

        /// <summary>
        /// Открывает узел выбранной строки и выделяет в нём запись.
        /// </summary>
        public void OpenSelected()
        {
            var row = SelectedRow;
            if (row == null)
                return;

            var tab = _editor.OpenModule(row.Module);
            if (row.Entry != null)
                tab.SelectAndScroll(row.Entry);
        }

        private async Task SearchAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var query = Query.Trim();
            if (query.Length == 0)
                return;

            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            IsSearching = true;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var total = 0;
                foreach (var domain in _editor.Domains)
                    total += domain.Modules.Count;

                Status = "Чтение файлов локализации...";
                var progress = new Progress<int>(count => Status = $"Чтение файлов локализации: {count} из {total}...");
                await _editor.LoadAllAsync(progress, cts.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                cts.Token.ThrowIfCancellationRequested();

                var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var rows = new List<SearchRow>();
                var found = 0;
                var modulesFound = 0;
                var truncated = false;

                foreach (var domain in _editor.Domains)
                {
                    foreach (var module in domain.Modules)
                    {
                        if (!module.IsLoaded)
                            continue;

                        var headerAdded = false;
                        if (IncludeAttributes)
                            AddMatches(module, module.Attributes, query, comparison, rows, ref headerAdded, ref found);

                        AddMatches(module, module.Items, query, comparison, rows, ref headerAdded, ref found);

                        if (headerAdded)
                            modulesFound++;

                        if (found >= MaxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }

                    if (truncated)
                        break;
                }

                Results = rows;
                SelectedRow = null;
                Status = found == 0
                    ? $"Ничего не найдено ({stopwatch.ElapsedMilliseconds} мс)."
                    : $"Найдено: {found} в узлах: {modulesFound} ({stopwatch.ElapsedMilliseconds} мс)." +
                      (truncated ? $" Показаны первые {MaxResults} — уточните запрос." : string.Empty) +
                      " Значения можно редактировать прямо здесь; двойной щелчок по заголовку открывает узел.";
            }
            catch (OperationCanceledException)
            {
                Status = "Поиск отменён.";
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    IsSearching = false;
            }
        }

        private void AddMatches(
            LocModule module,
            IList<LocEntry> entries,
            string query,
            StringComparison comparison,
            List<SearchRow> rows,
            ref bool headerAdded,
            ref int found)
        {
            foreach (var entry in entries)
            {
                if (!Matches(entry, query, comparison))
                    continue;

                if (!headerAdded)
                {
                    rows.Add(SearchRow.Header(module));
                    headerAdded = true;
                }

                rows.Add(SearchRow.Item(module, entry));
                found++;
            }
        }

        private bool Matches(LocEntry entry, string query, StringComparison comparison)
        {
            if (IncludeNames && entry.Name.IndexOf(query, comparison) >= 0)
                return true;

            for (var i = 0; i < entry.LanguageCount; i++)
            {
                var value = entry.GetRawValue(i);
                if (value != null && value.IndexOf(query, comparison) >= 0)
                    return true;
            }

            return false;
        }
    }
}
