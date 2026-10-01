using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using ModPlusVSTools.Models;
using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Services
{
    /// <summary>
    /// Пользовательская часть проверки обновлений: InfoBar при запуске, ручная проверка из меню,
    /// загрузка и запуск установщика.
    /// </summary>
    internal sealed class UpdateNotifier
    {
        private const string MessageTitle = "ModPlus VS Tools";
        private const string InstallAction = "install";
        private const string ReleaseNotesAction = "notes";

        private readonly AsyncPackage _package;
        private IVsInfoBarUIElement? _infoBar;
        private bool _isBusy;

        public UpdateNotifier(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>
        /// Фоновая проверка при запуске. Ошибки сети не показываются пользователю.
        /// При наличии новой версии показывается InfoBar.
        /// </summary>
        public async Task CheckOnStartupAsync()
        {
            var installed = UpdateService.GetInstalledVersion();
            if (installed == null)
                return;

            await TaskScheduler.Default;

            ReleaseInfo? release;
            try
            {
                release = await UpdateService.GetLatestReleaseAsync(_package.DisposalToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ModPlusVSTools: проверка обновлений не удалась: " + ex);
                return;
            }

            if (release == null || release.Version <= installed)
                return;

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            ShowUpdateInfoBar(release, installed);
        }

        /// <summary>
        /// Ручная проверка из меню. Результат (в том числе ошибка) всегда сообщается пользователю.
        /// </summary>
        public async Task CheckManuallyAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (_isBusy)
                return;

            _isBusy = true;
            try
            {
                var installed = UpdateService.GetInstalledVersion();

                ReleaseInfo? release;
                try
                {
                    await TaskScheduler.Default;
                    release = await UpdateService.GetLatestReleaseAsync(_package.DisposalToken);
                }
                catch (OperationCanceledException) when (_package.DisposalToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                    ShowMessage("Не удалось проверить наличие обновлений:\n" + GetErrorText(ex), OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }

                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                if (release == null)
                {
                    ShowMessage("Не удалось определить последнюю версию: в репозитории нет релизов с номером версии в имени тега.",
                        OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }

                if (installed != null && release.Version <= installed)
                {
                    ShowMessage($"Установлена последняя версия ({FormatVersion(installed)}).", OLEMSGICON.OLEMSGICON_INFO);
                    return;
                }

                var text = installed != null
                    ? $"Доступна новая версия {release.Tag} (установлена {FormatVersion(installed)}).\n\nСкачать и установить?"
                    : $"Последняя версия: {release.Tag}. Не удалось определить установленную версию.\n\nСкачать и установить?";

                var result = ShowMessage(text, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO);
                if (result == VSConstants.MessageBoxResult.IDYES)
                    await InstallCoreAsync(release);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (_isBusy)
                return;

            _isBusy = true;
            try
            {
                await InstallCoreAsync(release);
            }
            finally
            {
                _isBusy = false;
            }
        }

        /// <summary>
        /// Загружает .vsix с окном ожидания (с возможностью отмены) и запускает установщик.
        /// Вызывается на UI-потоке.
        /// </summary>
        private async Task InstallCoreAsync(ReleaseInfo release)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dialogFactory = await _package.GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            string vsixPath;
            var session = dialogFactory?.StartWaitDialog(
                MessageTitle,
                new ThreadedWaitDialogProgressData($"Загрузка версии {release.Tag}...", isCancelable: true),
                TimeSpan.FromMilliseconds(500));
            var userCancellationToken = session?.UserCancellationToken ?? CancellationToken.None;
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_package.DisposalToken, userCancellationToken))
                {
                    await TaskScheduler.Default;
                    vsixPath = await UpdateService.DownloadAsync(release, cts.Token);
                }
            }
            catch (OperationCanceledException) when (
                userCancellationToken.IsCancellationRequested || _package.DisposalToken.IsCancellationRequested)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                session?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                session?.Dispose();
                ShowMessage(
                    $"Не удалось загрузить обновление:\n{GetErrorText(ex)}\n\nСкачайте файл вручную со страницы релиза:\n{release.PageUrl}",
                    OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            session?.Dispose();

            try
            {
                UpdateService.LaunchInstaller(vsixPath);
            }
            catch (Exception ex)
            {
                ShowMessage(
                    $"Не удалось запустить установщик:\n{ex.Message}\n\nФайл обновления сохранён:\n{vsixPath}",
                    OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            ShowMessage(
                "Установщик обновления запущен.\n\nЧтобы завершить установку, закройте все окна Visual Studio.",
                OLEMSGICON.OLEMSGICON_INFO);
        }

        private void ShowUpdateInfoBar(ReleaseInfo release, Version installed)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Не дублировать InfoBar, если он уже показан
            _infoBar?.Close();
            _infoBar = null;

            if (Package.GetGlobalService(typeof(SVsShell)) is not IVsShell shell ||
                Package.GetGlobalService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory infoBarFactory)
            {
                Debug.WriteLine("ModPlusVSTools: не удалось получить SVsShell или SVsInfoBarUIFactory");
                return;
            }

            if (ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject)) ||
                hostObject is not IVsInfoBarHost host)
            {
                Debug.WriteLine("ModPlusVSTools: хост InfoBar главного окна недоступен");
                return;
            }

            var model = new InfoBarModel(
                new IVsInfoBarTextSpan[]
                {
                    new InfoBarTextSpan(
                        $"ModPlus VS Tools: доступна новая версия {release.Tag} (установлена {FormatVersion(installed)}). ")
                },
                new IVsInfoBarActionItem[]
                {
                    new InfoBarHyperlink("Скачать и установить", InstallAction),
                    new InfoBarHyperlink("Что нового", ReleaseNotesAction),
                },
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            var infoBar = infoBarFactory.CreateInfoBar(model);
            infoBar.Advise(new InfoBarEvents(this, release), out _);
            host.AddInfoBar(infoBar);
            _infoBar = infoBar;
        }

        private VSConstants.MessageBoxResult ShowMessage(
            string text,
            OLEMSGICON icon,
            OLEMSGBUTTON buttons = OLEMSGBUTTON.OLEMSGBUTTON_OK)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return (VSConstants.MessageBoxResult)VsShellUtilities.ShowMessageBox(
                _package,
                text,
                MessageTitle,
                icon,
                buttons,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>
        /// Текст ошибки для пользователя. Таймаут HttpClient приходит как TaskCanceledException
        /// с малоинформативным сообщением, поэтому он заменяется понятным текстом.
        /// </summary>
        private static string GetErrorText(Exception ex)
        {
            if (ex is OperationCanceledException)
                return "Превышено время ожидания ответа от GitHub.";

            var message = ex.Message;
            if (ex is System.Net.Http.HttpRequestException && ex.InnerException != null)
                message += "\n" + ex.InnerException.Message;
            return message;
        }

        /// <summary>
        /// Версия без незначащих нулевых компонентов: 2.0.0.0 → 2.0, 2.1.3.0 → 2.1.3.
        /// </summary>
        private static string FormatVersion(Version version)
        {
            if (version.Revision > 0)
                return version.ToString(4);
            return version.Build > 0 ? version.ToString(3) : version.ToString(2);
        }

        private sealed class InfoBarEvents : IVsInfoBarUIEvents
        {
            private readonly UpdateNotifier _owner;
            private readonly ReleaseInfo _release;

            public InfoBarEvents(UpdateNotifier owner, ReleaseInfo release)
            {
                _owner = owner;
                _release = release;
            }

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
                if (ReferenceEquals(_owner._infoBar, infoBarUIElement))
                    _owner._infoBar = null;
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                switch (actionItem.ActionContext as string)
                {
                    case ReleaseNotesAction:
                        try
                        {
                            UpdateService.OpenReleasePage(_release);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("ModPlusVSTools: не удалось открыть страницу релиза: " + ex);
                        }

                        break;

                    case InstallAction:
                        infoBarUIElement.Close();
                        _owner._package.JoinableTaskFactory
                            .RunAsync(() => _owner.InstallAsync(_release))
                            .FileAndForget("ModPlusVSTools/UpdateInstall");
                        break;
                }
            }
        }
    }
}
