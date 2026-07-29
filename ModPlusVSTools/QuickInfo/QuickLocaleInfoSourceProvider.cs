using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace ModPlusVSTools.QuickInfo
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("ModPlusVSTools QuickInfo")]
    [Order(Before = "default")]
    [ContentType("CSharp")]
    internal sealed class QuickLocaleInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new QuickLocaleInfoSource(textBuffer);
        }
    }
}
