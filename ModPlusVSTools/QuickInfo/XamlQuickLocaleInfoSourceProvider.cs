using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace ModPlusVSTools.QuickInfo
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("ShowLangItem XAML QuickInfo")]
    [Order(Before = "default")]
    [ContentType("XAML")]
    internal sealed class XamlQuickLocaleInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal VisualStudioWorkspace Workspace { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new XamlQuickLocaleInfoSource(textBuffer, Workspace);
        }
    }
}
