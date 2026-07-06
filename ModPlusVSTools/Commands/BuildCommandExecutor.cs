using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using ModPlusVSTools.Services;
using ModPlusVSTools.UI;

namespace ModPlusVSTools.Commands
{
    /// <summary>
    /// Общая логика выполнения команд "Собрать" и "Собрать и архивировать".
    /// </summary>
    internal static class BuildCommandExecutor
    {
        private static readonly Guid OutputPaneGuid = new Guid("16c4594e-fff4-41b7-941d-ab9cce0e389f");

        private static bool _isRunning;

        /// <summary>
        /// Выполнить команду.
        /// </summary>
        /// <param name="package">Пакет расширения.</param>
        /// <param name="archive">true — после сборки создать zip-архив с dll.</param>
        public static void Execute(AsyncPackage package, bool archive)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_isRunning)
            {
                ShowMessage(package, "Предыдущая сборка ModPlus ещё выполняется.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null || dte.Solution == null || !dte.Solution.IsOpen)
            {
                ShowMessage(package, "Нет открытого решения.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            if (dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
            {
                ShowMessage(package, "Дождитесь завершения текущей сборки.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            // 1. Поиск проектов плагинов в решении
            var candidates = BuildService.FindPluginProjects(dte);
            if (candidates.Count == 0)
            {
                ShowMessage(
                    package,
                    "В решении не найдено проектов плагинов ModPlus.\n\n" +
                    "Проект считается проектом плагина, если его конфигурации соответствуют " +
                    "версионному шаблону (например: A2013, R2019, N25.0, R5.1).",
                    OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            // 2. Выбор проекта: если проект один — собираем сразу, иначе показываем диалог
            PluginProjectCandidate selected;
            if (candidates.Count == 1)
            {
                selected = candidates[0];
            }
            else
            {
                var dialog = new ProjectSelectionDialog(candidates);
                if (dialog.ShowModal() != true || dialog.SelectedProject == null)
                    return;

                selected = dialog.SelectedProject;
            }

            // 3. Для варианта с архивом папку запрашиваем ДО сборки,
            //    чтобы не ждать окончания долгой сборки ради одного вопроса
            string targetFolder = null;
            if (archive)
            {
                using (var folderDialog = new FolderBrowserDialog
                {
                    Description = "Выберите папку, в которую будет сохранён zip-архив \"" + selected.Name + ".zip\"",
                    ShowNewFolderButton = true,
                })
                {
                    if (folderDialog.ShowDialog() != DialogResult.OK)
                        return;

                    targetFolder = folderDialog.SelectedPath;
                }
            }

            // 4. Подготовка: MSBuild, панель вывода, сохранение файлов
            var msBuildPath = BuildService.FindMsBuildPath(dte);
            if (msBuildPath == null)
            {
                ShowMessage(
                    package,
                    "Не удалось найти MSBuild.exe в установке Visual Studio.",
                    OLEMSGICON.OLEMSGICON_CRITICAL);
                return;
            }

            var pane = package.GetOutputPane(OutputPaneGuid, "ModPlus");
            pane.Activate();
            Action<string> log = text => pane.OutputStringThreadSafe(text + Environment.NewLine);

            try
            {
                dte.ExecuteCommand("File.SaveAll");
            }
            catch
            {
                // не критично
            }

            log(string.Format(
                "=== {0}: сборка проекта \"{1}\" ({2} конфигураций) ===",
                DateTime.Now.ToString("HH:mm:ss"),
                selected.Name,
                selected.Configurations.Count));
            log("MSBuild: " + msBuildPath);
            log(string.Empty);

            dte.StatusBar.Text = string.Format("ModPlus: сборка \"{0}\" запущена...", selected.Name);

            // 5. Сборка в фоновом потоке, чтобы не замораживать интерфейс VS
            _isRunning = true;
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await TaskScheduler.Default;
                    var result = BuildService.BuildProject(msBuildPath, selected, log);

                    await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                    Complete(package, dte, selected, result, archive, targetFolder, log);
                }
                catch (Exception ex)
                {
                    log("Непредвиденная ошибка: " + ex);
                }
                finally
                {
                    _isRunning = false;
                }
            });
        }

        /// <summary>
        /// Завершение: сводка, архив, Проводник. Выполняется на UI-потоке
        /// (архивирование использует DTE для поиска выходных dll).
        /// </summary>
        private static void Complete(
            AsyncPackage package,
            DTE2 dte,
            PluginProjectCandidate selected,
            BuildRunResult result,
            bool archive,
            string targetFolder,
            Action<string> log)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            dte.StatusBar.Text = "ModPlus: сборка завершена";

            var message = new StringBuilder();
            message.AppendFormat("Проект: {0}", selected.Name).AppendLine().AppendLine();
            message.AppendFormat("Успешно: {0} из {1}", result.Succeeded.Count, selected.Configurations.Count);
            if (result.Failed.Count > 0)
            {
                message.AppendLine();
                message.AppendFormat("С ошибками: {0}", string.Join(", ", result.Failed));
                message.AppendLine();
                message.Append("Подробности — в панели \"ModPlus\" окна \"Вывод\".");
            }

            if (archive)
            {
                if (result.Succeeded.Count == 0)
                {
                    message.AppendLine().AppendLine();
                    message.Append("Архив не создан: нет успешно собранных конфигураций.");
                    log("Архив не создан: нет успешно собранных конфигураций.");
                }
                else
                {
                    try
                    {
                        var zipPath = BuildService.CreateArchive(
                            selected,
                            result.Succeeded,
                            targetFolder,
                            out var missing);

                        message.AppendLine().AppendLine();
                        message.AppendFormat("Архив: {0}", zipPath);
                        log("Архив создан: " + zipPath);

                        if (missing.Count > 0)
                        {
                            message.AppendLine();
                            message.AppendFormat(
                                "Не найдены dll для конфигураций: {0}",
                                string.Join(", ", missing));
                        }

                        // Открыть Проводник с выделенным архивом
                        System.Diagnostics.Process.Start(
                            "explorer.exe",
                            "/select,\"" + zipPath + "\"");
                    }
                    catch (Exception ex)
                    {
                        message.AppendLine().AppendLine();
                        message.AppendFormat("Ошибка создания архива: {0}", ex.Message);
                        log("Ошибка создания архива: " + ex.Message);
                    }
                }
            }

            log("=== Готово ===");
            log(string.Empty);

            ShowMessage(
                package,
                message.ToString(),
                result.Failed.Count == 0 ? OLEMSGICON.OLEMSGICON_INFO : OLEMSGICON.OLEMSGICON_WARNING);
        }

        private static void ShowMessage(AsyncPackage package, string message, OLEMSGICON icon)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                package,
                message,
                "ModPlus",
                icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
