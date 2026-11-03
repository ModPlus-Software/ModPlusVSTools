using System;
using System.Windows;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Общий словарь стилей редактора: загружается один раз и подключается ко всем окнам
    /// и вкладкам (без повторного разбора xaml для каждой вкладки).
    /// </summary>
    internal static class EditorResourceDictionary
    {
        private static ResourceDictionary? _instance;

        /// <summary>Экземпляр словаря.</summary>
        public static ResourceDictionary Instance => _instance ??= new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ModPlusVSTools;component/Localization/UI/EditorResources.xaml", UriKind.Absolute),
        };

        /// <summary>
        /// Подключает общий словарь к ресурсам элемента. Вызывается до InitializeComponent,
        /// чтобы StaticResource в разметке находили стили.
        /// </summary>
        public static void MergeInto(FrameworkElement element)
        {
            element.Resources.MergedDictionaries.Add(Instance);
        }
    }
}
