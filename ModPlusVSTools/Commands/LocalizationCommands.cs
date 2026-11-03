using System;
using System.ComponentModel.Design;

using Microsoft.VisualStudio.Shell;

using ModPlusVSTools.Localization.Services;
using ModPlusVSTools.Localization.UI;

using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Commands
{
    /// <summary>
    /// Команды меню «Расширения → ModPlus»: «Настройки», «Локализация → Редактор», «Локализация → Собрать».
    /// </summary>
    internal static class LocalizationCommands
    {
        /// <summary>Идентификатор команды «Настройки». Должен совпадать с OpenSettingsCommandId в vsct.</summary>
        public const int OpenSettingsCommandId = 0x0103;

        /// <summary>Идентификатор команды «Локализация → Редактор». Должен совпадать с LocalizationEditorCommandId в vsct.</summary>
        public const int LocalizationEditorCommandId = 0x0104;

        /// <summary>Идентификатор команды «Локализация → Собрать». Должен совпадать с LocalizationBuildCommandId в vsct.</summary>
        public const int LocalizationBuildCommandId = 0x0105;

        /// <summary>GUID набора команд. Должен совпадать с guidModPlusVSToolsCmdSet в vsct.</summary>
        public static readonly Guid CommandSet = new Guid("7fd404e2-acb2-429b-987e-656d85da941c");

        /// <summary>
        /// Регистрирует команды.
        /// </summary>
        public static async Task InitializeAsync(ModPlusVSToolsPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
                return;

            commandService.AddCommand(new MenuCommand(
                (_, _) => package.ShowOptionPage(typeof(ModPlusVSToolsOptionsPage)),
                new CommandID(CommandSet, OpenSettingsCommandId)));

            commandService.AddCommand(new MenuCommand(
                (_, _) => package.JoinableTaskFactory
                    .RunAsync(async () =>
                    {
                        await package.ShowToolWindowAsync(typeof(LocalizationEditorToolWindow), 0, true, package.DisposalToken);
                    })
                    .FileAndForget("ModPlusVSTools/LocalizationEditor"),
                new CommandID(CommandSet, LocalizationEditorCommandId)));

            var buildCommand = new OleMenuCommand(
                (_, _) => package.JoinableTaskFactory
                    .RunAsync(async () => await LocalizationBuildService.BuildAsync(package))
                    .FileAndForget("ModPlusVSTools/LocalizationBuild"),
                new CommandID(CommandSet, LocalizationBuildCommandId));
            buildCommand.BeforeQueryStatus += (sender, _) =>
            {
                if (sender is OleMenuCommand command)
                    command.Enabled = !LocalizationBuildService.IsRunning;
            };
            commandService.AddCommand(buildCommand);
        }
    }
}
