using System;
using System.Runtime.CompilerServices;

using ModPlusVSTools.Localization.Services;

namespace ModPlusVSTools.Localization.Models
{
    /// <summary>
    /// Визуальное состояние строки таблицы.
    /// </summary>
    internal enum RowState
    {
        /// <summary>Обычная строка.</summary>
        None,

        /// <summary>Предупреждение (дубликат набора значений).</summary>
        Warning,

        /// <summary>Ошибка (некорректные данные, дубликат имени).</summary>
        Error,

        /// <summary>Строка помечена к удалению. Приоритетнее ошибок и предупреждений.</summary>
        Marked,
    }

    /// <summary>
    /// Запись перевода: строка (дочерний элемент узла плагина) или атрибут узла плагина
    /// со значениями по всем языкам.
    /// </summary>
    /// <remarks>
    /// Значения хранятся массивом в порядке языков редактора — таблица привязывается к индексатору
    /// (<c>[0]</c>, <c>[1]</c>, …), что заметно дешевле словаря и отдельных объектов значений.
    /// <c>null</c> — значения в файле языка нет (ключ отсутствует), пустая строка — значение пустое.
    /// </remarks>
    internal sealed class LocEntry : ObservableBase
    {
        /// <summary>Имя свойства индексатора для уведомлений WPF.</summary>
        private const string IndexerName = "Item[]";

        private readonly string?[] _values;
        private string _name;
        private string? _comment;
        private RowState _rowState;
        private string? _issues;
        private bool _hasError;
        private bool _hasWarning;

        /// <summary>
        /// Создаёт запись.
        /// </summary>
        /// <param name="name">Имя ключа.</param>
        /// <param name="languageCount">Число языков.</param>
        /// <param name="isAttribute">Запись является атрибутом узла плагина.</param>
        public LocEntry(string name, int languageCount, bool isAttribute)
        {
            _name = name ?? string.Empty;
            _values = new string?[languageCount];
            IsAttribute = isAttribute;
        }

        /// <summary>Модуль-владелец. Задаётся при добавлении записи в модуль.</summary>
        public LocModule? Owner { get; set; }

        /// <summary>Запись является атрибутом узла плагина.</summary>
        public bool IsAttribute { get; }

        /// <summary>
        /// Имя ключа в файлах на диске (до переименования в редакторе). <c>null</c> — запись ещё не сохранялась.
        /// </summary>
        public string? OriginalName { get; set; }

        /// <summary>Число языков.</summary>
        public int LanguageCount => _values.Length;

        /// <summary>Имя ключа.</summary>
        public string Name
        {
            get => _name;
            set
            {
                value ??= string.Empty;
                if (string.Equals(_name, value, StringComparison.Ordinal))
                    return;

                _name = value;
                OnPropertyChanged();
                Owner?.OnEntryChanged(this);
            }
        }

        /// <summary>Комментарий перед элементом (в том числе пометка «todo remove after X»).</summary>
        public string? Comment
        {
            get => _comment;
            set
            {
                if (string.Equals(_comment, value, StringComparison.Ordinal))
                    return;

                _comment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMarked));
                OnPropertyChanged(nameof(DeletionVersion));
                UpdateRowState();
                Owner?.OnEntryChanged(this);
            }
        }

        /// <summary>Помечена ли запись к удалению.</summary>
        public bool IsMarked => DeletionMarker.IsMarked(_comment);

        /// <summary>Версия, после которой запись удаляется, или <c>null</c>.</summary>
        public string? DeletionVersion => DeletionMarker.TryGetVersion(_comment, out var version) ? version : null;

        /// <summary>
        /// Значение перевода для языка с указанным индексом. Для таблицы отсутствующее значение
        /// показывается пустой строкой.
        /// </summary>
        /// <param name="index">Индекс языка.</param>
        [IndexerName("Item")]
        public string this[int index]
        {
            get => _values[index] ?? string.Empty;
            set
            {
                value ??= string.Empty;

                // Пустой ввод в ячейку отсутствующего значения не создаёт ключ в файле языка
                if (_values[index] == null && value.Length == 0)
                    return;

                if (string.Equals(_values[index], value, StringComparison.Ordinal))
                    return;

                _values[index] = value;
                OnPropertyChanged(IndexerName);
                Owner?.OnEntryChanged(this);
            }
        }

        /// <summary>Есть ли ошибка.</summary>
        public bool HasError => _hasError;

        /// <summary>Есть ли предупреждение.</summary>
        public bool HasWarning => _hasWarning;

        /// <summary>Визуальное состояние строки.</summary>
        public RowState RowState
        {
            get => _rowState;
            private set => SetField(ref _rowState, value);
        }

        /// <summary>Описание проблем записи (подсказка строки) или <c>null</c>.</summary>
        public string? Issues
        {
            get => _issues;
            private set => SetField(ref _issues, value);
        }

        /// <summary>Подсказка строки: проблемы и комментарий.</summary>
        public string? ToolTip
        {
            get
            {
                if (string.IsNullOrEmpty(_issues) && string.IsNullOrWhiteSpace(_comment))
                    return null;

                if (string.IsNullOrWhiteSpace(_comment))
                    return _issues;

                var comment = "Комментарий: " + _comment!.Trim();
                return string.IsNullOrEmpty(_issues) ? comment : _issues + Environment.NewLine + comment;
            }
        }

        /// <summary>
        /// Возвращает значение языка как есть: <c>null</c>, если значения нет.
        /// </summary>
        /// <param name="index">Индекс языка.</param>
        public string? GetRawValue(int index) => _values[index];

        /// <summary>
        /// Устанавливает значение без уведомлений (загрузка с диска).
        /// </summary>
        /// <param name="index">Индекс языка.</param>
        /// <param name="value">Значение.</param>
        public void SetRawValue(int index, string? value) => _values[index] = value;

        /// <summary>
        /// Устанавливает комментарий без уведомлений (загрузка с диска).
        /// </summary>
        /// <param name="comment">Комментарий.</param>
        public void SetRawComment(string? comment)
        {
            _comment = comment;
            UpdateRowStateSilently();
        }

        /// <summary>
        /// Устанавливает значения всех языков (вставка из буфера, импорт).
        /// </summary>
        /// <param name="values">Значения по индексам языков; <c>null</c> — язык не меняется.</param>
        public void SetValues(string?[] values)
        {
            var changed = false;
            for (var i = 0; i < _values.Length && i < values.Length; i++)
            {
                if (values[i] == null || string.Equals(_values[i], values[i], StringComparison.Ordinal))
                    continue;

                _values[i] = values[i];
                changed = true;
            }

            if (!changed)
                return;

            OnPropertyChanged(IndexerName);
            Owner?.OnEntryChanged(this);
        }

        /// <summary>
        /// Обновляет результат проверки записи.
        /// </summary>
        /// <param name="hasError">Есть ошибка.</param>
        /// <param name="hasWarning">Есть предупреждение.</param>
        /// <param name="issues">Описание проблем.</param>
        public void SetDiagnostics(bool hasError, bool hasWarning, string? issues)
        {
            var toolTipChanged = !string.Equals(_issues, issues, StringComparison.Ordinal);

            _hasError = hasError;
            _hasWarning = hasWarning;
            Issues = issues;
            UpdateRowState();

            if (toolTipChanged)
                OnPropertyChanged(nameof(ToolTip));
        }

        private void UpdateRowState()
        {
            RowState = CalculateRowState();
            OnPropertyChanged(nameof(ToolTip));
        }

        private void UpdateRowStateSilently() => _rowState = CalculateRowState();

        // Пометка к удалению приоритетнее ошибок и предупреждений
        private RowState CalculateRowState() =>
            IsMarked ? RowState.Marked
            : _hasError ? RowState.Error
            : _hasWarning ? RowState.Warning
            : RowState.None;
    }
}
