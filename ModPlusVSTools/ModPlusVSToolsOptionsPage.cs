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
        /// Папка исходных файлов локализации ModPlus (папка LanguageFiles\LanguageFiles репозитория LanguageFiles).
        /// </summary>
        /// <remarks>
        /// Имя свойства не меняется: под ним значение сохранено в настройках VS у пользователей.
        /// </remarks>
        [Category("Локализация")]
        [DisplayName("Путь к папке исходных файлов локализации")]
        [Description("Папка ИСХОДНЫХ файлов локализации ModPlus из репозитория LanguageFiles — " +
                     "LanguageFiles\\LanguageFiles (рядом с ней лежат проекты LangFilesEditor и ModPlus.LanguageTools.Cli). " +
                     "Внутри неё должны находиться подпапки языков (ru-RU, en-US и т.д.) с XML-файлами плагинов " +
                     "(Revit_mprAlignViews_ru.xml, Common.xml и т.д.) и файл Langs.xml. " +
                     "Не указывайте здесь папку Languages установленного ModPlus — там лежат уже собранные файлы. " +
                     "Используется всплывающими подсказками по ключам локализации, редактором локализации и командой " +
                     "Расширения → ModPlus → Локализация → Собрать.")]
        [Editor(typeof(LanguageFolderNameEditor), typeof(UITypeEditor))]
        public string BaseLanguageFilesPath { get; set; } = string.Empty;

        /// <summary>
        /// Проверять наличие новой версии расширения при запуске Visual Studio.
        /// </summary>
        [Category("Обновления")]
        [DisplayName("Проверять обновления при запуске")]
        [Description("При запуске Visual Studio проверять наличие новой версии расширения в разделе Releases " +
                     "репозитория ModPlus-Software/ModPlusVSTools на GitHub. Проверить вручную можно командой " +
                     "Расширения → ModPlus → Проверить обновления.")]
        [DefaultValue(true)]
        public bool CheckForUpdatesOnStartup { get; set; } = true;
    }

    /// <summary>
    /// Редактор свойства с кнопкой выбора папки.
    /// </summary>
    public sealed class LanguageFolderNameEditor : FolderNameEditor
    {
        /// <inheritdoc/>
        protected override void InitializeDialog(FolderBrowser folderBrowser)
        {
            folderBrowser.Description = "Выберите папку исходных файлов локализации ModPlus (LanguageFiles\\LanguageFiles)";
        }
    }
}
