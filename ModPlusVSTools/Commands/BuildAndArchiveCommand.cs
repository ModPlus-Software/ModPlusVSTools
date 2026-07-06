using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools.Commands
{
    /// <summary>
    /// Команда "Расширения → ModPlus → Собрать и архивировать".
    /// </summary>
    internal sealed class BuildAndArchiveCommand
    {
        /// <summary>Идентификатор команды. Должен совпадать с BuildAndArchiveCommandId в vsct.</summary>
        public const int CommandId = 0x0101;

        /// <summary>GUID набора команд. Должен совпадать с guidModPlusVSToolsCmdSet в vsct.</summary>
        public static readonly Guid CommandSet = new Guid("7fd404e2-acb2-429b-987e-656d85da941c");

        private readonly AsyncPackage _package;

        private BuildAndArchiveCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService == null)
                throw new ArgumentNullException(nameof(commandService));

            var menuCommandId = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Экземпляр команды.</summary>
        public static BuildAndArchiveCommand Instance { get; private set; }

        /// <summary>Инициализация команды.</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService =
                await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new BuildAndArchiveCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            BuildCommandExecutor.Execute(_package, archive: true);
        }
    }
}
