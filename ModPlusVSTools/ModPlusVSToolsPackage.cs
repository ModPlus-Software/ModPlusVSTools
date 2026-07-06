using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using ModPlusVSTools.Commands;
using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools
{
    /// <summary>
    /// Пакет расширения ModPlus VS Tools.
    /// Добавляет меню "ModPlus" в главное меню "Расширения" (Extensions).
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("ModPlus VS Tools", "Инструменты для разработки плагинов ModPlus", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    public sealed class ModPlusVSToolsPackage : AsyncPackage
    {
        /// <summary>GUID пакета. Должен совпадать с guidModPlusVSToolsPackage в vsct-файле.</summary>
        public const string PackageGuidString = "76fec63c-bbe6-4ec2-b437-46de6ffe6749";

        /// <inheritdoc/>
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            // Команды меню регистрируются на UI-потоке
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await BuildCommand.InitializeAsync(this);
            await BuildAndArchiveCommand.InitializeAsync(this);
        }
    }
}