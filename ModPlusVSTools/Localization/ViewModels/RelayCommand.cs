using System;
using System.Windows.Input;

namespace ModPlusVSTools.Localization.ViewModels
{
    /// <summary>
    /// Команда с делегатами выполнения и доступности.
    /// </summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        /// <summary>
        /// Создаёт команду без параметра.
        /// </summary>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : new Func<object?, bool>(_ => canExecute()))
        {
        }

        /// <summary>
        /// Создаёт команду с параметром.
        /// </summary>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        /// <summary>
        /// Доступность пересчитывается через <see cref="CommandManager"/>: WPF переспрашивает её
        /// при вводе и смене фокуса, явных уведомлений из моделей не нужно.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        /// <inheritdoc/>
        public void Execute(object? parameter) => _execute(parameter);
    }
}
