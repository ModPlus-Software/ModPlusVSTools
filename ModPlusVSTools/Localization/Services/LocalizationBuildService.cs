using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

using ModPlus.LanguageTools;
using ModPlus.LanguageTools.Checks;

using ModPlusVSTools.Services;

using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Сборка языковых файлов — тот же процесс, что в ModPlusBuilder и LangFilesEditor:
    /// исправление кавычек в исходных файлах, проверка и слияние в файлы <c>{язык}.xml</c>
    /// в папке Languages установленного ModPlus с версией из Langs.xml.
    /// </summary>
    /// <remarks>
    /// Вызывается библиотека ModPlus.LanguageTools (сборка netstandard2.0) — тот же код, что
    /// у ModPlusBuilder, LangFilesEditor и утилиты в GitHub Actions. Ход сборки выводится
    /// в окно «Вывод», ошибки и предупреждения проверки — дополнительно в «Список ошибок»
    /// с переходом к строке файла.
    /// </remarks>
    internal static class LocalizationBuildService
    {
        private static readonly Guid OutputPaneGuid = new Guid("5b3e4f7a-3c1d-4d8e-9a77-2f1f6c4b8e21");
        private static ErrorListProvider? _errorList;
        private static int _isRunning;

        /// <summary>Идёт ли сборка.</summary>
        public static bool IsRunning => Volatile.Read(ref _isRunning) != 0;

        /// <summary>
        /// Запускает сборку. Вызывается в UI-потоке.
        /// </summary>
        /// <param name="package">Пакет расширения.</param>
        /// <returns><c>true</c>, если сборка выполнена успешно.</returns>
        public static async Task<bool> BuildAsync(ModPlusVSToolsPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                Ui.ShowMessage("Сборка файлов локализации уже выполняется.", OLEMSGICON.OLEMSGICON_INFO);
                return false;
            }

            try
            {
                return await RunAsync(package);
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
            }
        }

        private static async Task<bool> RunAsync(ModPlusVSToolsPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var pane = package.GetOutputPane(OutputPaneGuid, "ModPlus — локализация");
            pane.Activate();
            void Write(string text) => pane.OutputStringThreadSafe(text + Environment.NewLine);

            var errorList = GetErrorList(package);
            errorList.Tasks.Clear();

            var configuredRoot = package.OptionsPage.BaseLanguageFilesPath;
            var pathError = LocalizationPathValidator.Validate(configuredRoot);
            if (pathError != null)
            {
                Write(pathError);
                Ui.ShowMessage(pathError + Environment.NewLine + "Проверьте настройки ModPlus VS Tools.", OLEMSGICON.OLEMSGICON_WARNING);
                return false;
            }

            var root = LocalizationFolder.NormalizeRoot(configuredRoot);
            var output = LocalizationFolder.GetInstalledLanguagesFolder(out var outputSource);
            if (output == null)
            {
                const string message = "Не найден установленный ModPlus: в реестре (HKCU\\Software\\ModPlus, TopDir) " +
                                       "нет папки с подпапкой Languages, и нет папки %AppData%\\ModPlus. " +
                                       "Языковые файлы собираются в папку Languages установленного ModPlus.";
                Write(message);
                Ui.ShowMessage(message, OLEMSGICON.OLEMSGICON_WARNING);
                return false;
            }

            // Несохранённые изменения редактора не попадут в сборку — предлагается сохранить
            var editor = LocalizationEditorSession.Current;
            editor?.CommitEdits();
            if (editor != null && editor.HasUnsavedChanges)
            {
                var answer = Ui.AskYesNoCancel(
                    "В редакторе локализации есть несохранённые изменения. Сохранить их перед сборкой?");
                if (answer == null)
                    return false;

                if (answer == true && !await editor.SaveAllAsync())
                {
                    Write("Сборка отменена: не все изменения редактора удалось сохранить.");
                    return false;
                }
            }

            Write(string.Empty);
            Write($"=== {DateTime.Now:HH:mm:ss}: сборка файлов локализации ===");
            Write("Исходные файлы: " + root);
            Write("Папка языковых файлов: " + output + " (" + outputSource + ")");
            Write(string.Empty);

            SetStatusBar("ModPlus: сборка файлов локализации...");

            var log = new OutputPaneLog(Write);
            BuildResult? result = null;
            CheckResult? check = null;
            Version? version = null;
            string? failure = null;

            await TaskScheduler.Default;
            try
            {
                version = LanguagesVersionFile.Read(root);
                result = LanguageBuilder.Build(root, output, version, log);
                check = result.Check;
            }
            catch (LanguageToolsException exception)
            {
                // Проверка не пройдена или ошибка в данных: сообщение показывается как есть, без стека
                check = exception.CheckResult;
                failure = exception.Message;
            }
            catch (Exception exception)
            {
                failure = "Непредвиденная ошибка: " + exception;
            }

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (check != null)
                FillErrorList(errorList, check);

            var errors = check?.Errors.Count() ?? 0;
            var warnings = check?.Warnings.Count() ?? 0;
            var success = failure == null && result != null;

            string summary;
            if (success)
            {
                summary = $"Файлы локализации собраны успешно.{Environment.NewLine}{Environment.NewLine}" +
                          $"Версия: {version}{Environment.NewLine}" +
                          $"Языковых файлов: {result!.Files.Count}{Environment.NewLine}" +
                          $"Папка: {output}";
                if (result.QuoteFixedFiles.Count > 0)
                    summary += $"{Environment.NewLine}Исправлены кавычки в исходных файлах: {result.QuoteFixedFiles.Count}";
                if (warnings > 0)
                    summary += $"{Environment.NewLine}{Environment.NewLine}Предупреждений: {warnings} — см. «Список ошибок» (сборку не блокируют).";
            }
            else
            {
                summary = $"Файлы локализации НЕ собраны.{Environment.NewLine}{Environment.NewLine}{failure}";
                if (errors > 0)
                    summary += $"{Environment.NewLine}{Environment.NewLine}Ошибок: {errors}, предупреждений: {warnings} — см. «Список ошибок».";
                summary += $"{Environment.NewLine}Ход сборки — в окне «Вывод» (панель «ModPlus — локализация»).";
            }

            Write(string.Empty);
            Write(success
                ? $"========== Сборка локализации: УСПЕШНО ({DateTime.Now:HH:mm:ss}). Версия {version}, файлов: {result!.Files.Count}, предупреждений: {warnings} =========="
                : $"========== Сборка локализации: ОШИБКА ({DateTime.Now:HH:mm:ss}). Ошибок: {errors}, предупреждений: {warnings} ==========");

            SetStatusBar(success
                ? $"ModPlus: файлы локализации собраны (версия {version}, предупреждений: {warnings})"
                : "ModPlus: файлы локализации НЕ собраны — см. «Список ошибок» и окно «Вывод»");

            if (LocalizationEditorSession.Current is { } session)
                session.Status = success ? $"Сборка выполнена успешно: версия {version}, файлов {result!.Files.Count}, предупреждений {warnings}." : "Сборка не выполнена: " + failure;

            // «Список ошибок» выводится на передний план только при ошибках:
            // при успешной сборке с предупреждениями он закрывал бы окно «Вывод» с итогом
            if (success || errorList.Tasks.Count == 0)
                ShowOutputWindow(pane);
            else
                errorList.Show();

            // Итог сборки — явным сообщением, как у команды «Собрать» для проектов плагинов
            Ui.ShowMessage(summary, success ? OLEMSGICON.OLEMSGICON_INFO : OLEMSGICON.OLEMSGICON_CRITICAL);

            // Сборка исправляет кавычки прямо в исходных файлах — редактор перечитывает их.
            // При ошибке кавычки тоже могли быть исправлены (исправление идёт до проверки)
            if (result == null || result.QuoteFixedFiles.Count > 0)
                LocalizationEditorSession.Current?.OnSourcesChangedExternally();

            return failure == null;
        }

        private static ErrorListProvider GetErrorList(IServiceProvider serviceProvider)
        {
            return _errorList ??= new ErrorListProvider(serviceProvider)
            {
                ProviderName = "ModPlus — локализация",
                ProviderGuid = new Guid("b1c6a3f2-7e4d-4a59-8f0e-2d9c5b7a1e63"),
            };
        }

        /// <summary>
        /// Ошибки и предупреждения проверки в «Список ошибок»: двойной щелчок открывает файл на нужной строке.
        /// </summary>
        private static void FillErrorList(ErrorListProvider errorList, CheckResult check)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            errorList.SuspendRefresh();
            try
            {
                foreach (var issue in check.Issues.OrderBy(i => i.Severity))
                {
                    var task = new ErrorTask
                    {
                        Category = TaskCategory.BuildCompile,
                        ErrorCategory = issue.Severity == CheckSeverity.Error
                            ? TaskErrorCategory.Error
                            : TaskErrorCategory.Warning,
                        Text = issue.Category + ": " + issue.Message,
                        Document = issue.File ?? string.Empty,

                        // Строки в списке ошибок нумеруются с нуля
                        Line = issue.Line.HasValue ? issue.Line.Value - 1 : 0,
                    };

                    if (!string.IsNullOrEmpty(issue.File))
                    {
                        task.Navigate += (sender, _) =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            if (sender is ErrorTask errorTask && File.Exists(errorTask.Document))
                                errorList.Navigate(errorTask, VSConstants.LOGVIEWID_Primary);
                        };
                    }

                    errorList.Tasks.Add(task);
                }
            }
            finally
            {
                errorList.ResumeRefresh();
            }
        }

        /// <summary>
        /// Показывает окно «Вывод» с панелью локализации (Activate панели само окно не открывает).
        /// </summary>
        private static void ShowOutputWindow(IVsOutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                var outputWindowGuid = VSConstants.StandardToolWindows.Output;
                if (ErrorHandler.Succeeded(shell.FindToolWindow(
                        (uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref outputWindowGuid, out var frame)) &&
                    frame != null)
                {
                    frame.Show();
                }
            }

            pane.Activate();
        }

        private static void SetStatusBar(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                statusBar.SetText(text);
        }

        /// <summary>
        /// Лог библиотеки ModPlus.LanguageTools в панель окна «Вывод».
        /// </summary>
        private sealed class OutputPaneLog : ILanguageToolsLog
        {
            private readonly Action<string> _write;

            public OutputPaneLog(Action<string> write)
            {
                _write = write;
            }

            public void Info(string message) => _write(message);

            public void Warning(string message) => _write("Предупреждение: " + message);

            public void Error(string message) => _write("Ошибка: " + message);
        }
    }
}
