using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;

using Microsoft.VisualStudio.Shell;

namespace ModPlusVSTools
{
    /// <summary>
    /// Страница настроек расширения ModPlus VS Tools (Сервис → Параметры → ModPlus VS Tools).
    /// </summary>
    public sealed class ModPlusVSToolsOptionsPage : DialogPage
    {
        /// <summary>
        /// Корневая папка файлов локализации ModPlus.
        /// </summary>
        [Category("Локализация")]
        [DisplayName("Путь к папке файлов локализации")]
        [Description("Корневая папка файлов локализации ModPlus. Внутри неё должны находиться подпапки языков " +
                     "(ru-RU, en-US и т.д.) с XML-файлами локализации. Используется для показа локализованных " +
                     "значений во всплывающих подсказках по ключам локализации.")]
        [Editor(typeof(LanguageFolderNameEditor), typeof(UITypeEditor))]
        public string BaseLanguageFilesPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Редактор свойства с кнопкой выбора папки.
    /// </summary>
    public sealed class LanguageFolderNameEditor : FolderNameEditor
    {
        /// <inheritdoc/>
        protected override void InitializeDialog(FolderBrowser folderBrowser)
        {
            folderBrowser.Description = "Выберите корневую папку файлов локализации ModPlus";
        }
    }
}
