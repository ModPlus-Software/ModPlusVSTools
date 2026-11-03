using System.Runtime.InteropServices;

using Microsoft.VisualStudio.Shell;

using ModPlusVSTools.Localization.ViewModels;

namespace ModPlusVSTools.Localization.UI
{
    /// <summary>
    /// Окно редактора исходных файлов локализации. Открывается вкладкой в области документов
    /// (как ResXManager) и может быть откреплено или прикреплено к любой панели VS.
    /// </summary>
    [Guid(WindowGuidString)]
    public sealed class LocalizationEditorToolWindow : ToolWindowPane
    {
        /// <summary>GUID окна.</summary>
        public const string WindowGuidString = "0dc654a1-7dc5-433c-ae7c-f4c33d9e7105";

        /// <summary>
        /// Создаёт окно.
        /// </summary>
        public LocalizationEditorToolWindow()
            : base(null)
        {
            Caption = "ModPlus — локализация";

            var control = new LocalizationEditorControl();
            var package = ModPlusVSToolsPackage.Instance;
            if (package != null)
            {
                // Модель живёт всё время работы VS: закрытие окна не теряет несохранённые изменения
                LocalizationEditorSession.Current ??= new EditorViewModel(package);
                control.Attach(LocalizationEditorSession.Current);
            }

            Content = control;
        }
    }
}
