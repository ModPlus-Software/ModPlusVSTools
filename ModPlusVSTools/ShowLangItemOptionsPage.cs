using System.ComponentModel;

using Microsoft.VisualStudio.Shell;

namespace ModPlusVSTools
{
    public sealed class ShowLangItemOptionsPage : DialogPage
    {
        [Category("Localization")]
        [DisplayName("Base LanguageFiles Path")]
        [Description("Root path to language files (locale subdirectories with XML localizations)")]
        public string BaseLanguageFilesPath { get; set; } = string.Empty;
    }
}
