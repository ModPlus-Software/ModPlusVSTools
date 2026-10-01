using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using ModPlusVSTools.Services;
using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Commands
{
    /// <summary>
    /// Команда "Расширения → ModPlus → Проверить обновления".
    /// </summary>
    internal sealed class CheckForUpdatesCommand
    {
        /// <summary>Идентификатор команды. Должен совпадать с CheckForUpdatesCommandId в vsct.</summary>
        public const int CommandId = 0x0102;

        /// <summary>GUID набора команд. Должен совпадать с guidModPlusVSToolsCmdSet в vsct.</summary>
        public static readonly Guid CommandSet = new Guid("7fd404e2-acb2-429b-987e-656d85da941c");

        private readonly AsyncPackage _package;
        private readonly UpdateNotifier _updateNotifier;

        private CheckForUpdatesCommand(AsyncPackage package, OleMenuCommandService commandService, UpdateNotifier updateNotifier)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _updateNotifier = updateNotifier ?? throw new ArgumentNullException(nameof(updateNotifier));
            if (commandService == null)
                throw new ArgumentNullException(nameof(commandService));

            var menuCommandId = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Экземпляр команды.</summary>
        public static CheckForUpdatesCommand? Instance { get; private set; }

        /// <summary>Инициализация команды.</summary>
        public static async Task InitializeAsync(AsyncPackage package, UpdateNotifier updateNotifier)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService =
                await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CheckForUpdatesCommand(package, commandService!, updateNotifier);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _package.JoinableTaskFactory
                .RunAsync(_updateNotifier.CheckManuallyAsync)
                .FileAndForget("ModPlusVSTools/CheckForUpdates");
        }
    }
}
