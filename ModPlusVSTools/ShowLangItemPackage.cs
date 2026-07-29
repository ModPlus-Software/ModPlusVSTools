using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

using Task = System.Threading.Tasks.Task;

namespace ModPlusVSTools
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideOptionPage(typeof(ShowLangItemOptionsPage), "ShowLangItem", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(ShowLangItemPackage.PackageGuidString)]
    public sealed class ShowLangItemPackage : AsyncPackage
    {
        public const string PackageGuidString = "301437b2-9d3d-4f8a-8d72-fff6f86179e9";

        public static ShowLangItemPackage Instance { get; private set; }

        public ShowLangItemOptionsPage OptionsPage
        {
            get
            {
                return (ShowLangItemOptionsPage)GetDialogPage(typeof(ShowLangItemOptionsPage));
            }
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            Debug.WriteLine("ShowLangItem: InitializeAsync started");
        }
    }
}
