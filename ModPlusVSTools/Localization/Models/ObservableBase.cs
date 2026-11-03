using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModPlusVSTools.Localization.Models
{
    /// <summary>
    /// Базовый класс с уведомлением об изменении свойств.
    /// </summary>
    internal abstract class ObservableBase : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Уведомляет об изменении свойства.
        /// </summary>
        /// <param name="propertyName">Имя свойства.</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Устанавливает значение поля и уведомляет об изменении, если значение изменилось.
        /// </summary>
        /// <returns><c>true</c>, если значение изменилось.</returns>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
