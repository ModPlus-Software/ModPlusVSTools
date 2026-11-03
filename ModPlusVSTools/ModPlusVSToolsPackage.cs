using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ModPlusVSTools.Commands;
using ModPlusVSTools.Localization;
using ModPlusVSTools.Localization.UI;
using ModPlusVSTools.Services;
using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools
{
    /// <summary>
    /// Пакет расширения ModPlus VS Tools.
    /// Добавляет меню "ModPlus" в главное меню "Расширения" (Extensions), редактор локализации,
    /// страницу настроек, проверку пути к файлам локализации и проверку обновлений при запуске.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(ModPlusVSToolsOptionsPage), "ModPlus VS Tools", "Основные", 0, 0, true)]
    [ProvideToolWindow(typeof(LocalizationEditorToolWindow), Style = VsDockStyle.MDI, MultiInstances = false, Transient = false)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class ModPlusVSToolsPackage : AsyncPackage
    {
        /// <summary>GUID пакета. Должен совпадать с guidModPlusVSToolsPackage в vsct-файле.</summary>
        public const string PackageGuidString = "76fec63c-bbe6-4ec2-b437-46de6ffe6749";

        /// <summary>Экземпляр пакета.</summary>
        public static ModPlusVSToolsPackage? Instance { get; private set; }

        /// <summary>Страница настроек расширения.</summary>
        public ModPlusVSToolsOptionsPage OptionsPage =>
            (ModPlusVSToolsOptionsPage)GetDialogPage(typeof(ModPlusVSToolsOptionsPage));

        /// <inheritdoc/>
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            Instance = this;

            // Команды меню регистрируются на UI-потоке
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await BuildCommand.InitializeAsync(this);
            await BuildAndArchiveCommand.InitializeAsync(this);
            await LocalizationCommands.InitializeAsync(this);

            var updateNotifier = new UpdateNotifier(this);
            await CheckForUpdatesCommand.InitializeAsync(this, updateNotifier);

            // При автозагрузке пакета хост InfoBar главного окна ещё может не существовать,
            // поэтому проверка откладывается до полной инициализации оболочки VS
            KnownUIContexts.ShellInitializedContext.WhenActivated(() =>
                JoinableTaskFactory.RunAsync(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                    CheckLocalizationPath();

                    if (OptionsPage.CheckForUpdatesOnStartup)
                        await updateNotifier.CheckOnStartupAsync();
                }));
        }

        /// <summary>
        /// При закрытии VS предлагает сохранить несохранённые изменения редактора локализации.
        /// </summary>
        /// <inheritdoc/>
        protected override int QueryClose(out bool canClose)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            canClose = true;

            var editor = LocalizationEditorSession.Current;
            if (editor == null)
                return VSConstants.S_OK;

            editor.CommitEdits();
            if (!editor.HasUnsavedChanges)
                return VSConstants.S_OK;

            var answer = Ui.AskYesNoCancel("В редакторе локализации ModPlus есть несохранённые изменения. Сохранить их?");
            if (answer == null)
            {
                canClose = false;
            }
            else if (answer == true)
            {
                // Запись файлов идёт в фоне; ожидание на UI-потоке через JoinableTaskFactory безопасно
                canClose = JoinableTaskFactory.Run(() => editor.SaveAllAsync());
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Проверяет путь к папке исходных файлов локализации и при проблеме показывает InfoBar.
        /// </summary>
        private void CheckLocalizationPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var error = LocalizationPathValidator.Validate(OptionsPage.BaseLanguageFilesPath);
            if (error == null)
            {
                return;
            }

            ShowInfoBar(error);
        }

        private void ShowInfoBar(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetService(typeof(SVsShell)) is not IVsShell shell ||
                GetService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory infoBarFactory)
            {
                System.Diagnostics.Debug.WriteLine("ModPlusVSTools: не удалось получить SVsShell или SVsInfoBarUIFactory");
                return;
            }

            if (ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject)) ||
                hostObject is not IVsInfoBarHost host)
            {
                System.Diagnostics.Debug.WriteLine("ModPlusVSTools: хост InfoBar главного окна недоступен");
                return;
            }

            var model = new InfoBarModel(
                new IVsInfoBarTextSpan[] { new InfoBarTextSpan("ModPlus VS Tools: " + message + " ") },
                new IVsInfoBarActionItem[] { new InfoBarHyperlink("Открыть настройки") },
                KnownMonikers.StatusWarning,
                isCloseButtonVisible: true);

            var infoBar = infoBarFactory.CreateInfoBar(model);
            infoBar.Advise(new InfoBarEvents(this), out _);
            host.AddInfoBar(infoBar);
        }

        private sealed class InfoBarEvents : IVsInfoBarUIEvents
        {
            private readonly ModPlusVSToolsPackage _package;

            public InfoBarEvents(ModPlusVSToolsPackage package)
            {
                _package = package;
            }

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                _package.ShowOptionPage(typeof(ModPlusVSToolsOptionsPage));
                infoBarUIElement.Close();
            }
        }
    }
}
