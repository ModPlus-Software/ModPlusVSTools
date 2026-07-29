using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

using ModPlusVSTools.Services;

namespace ModPlusVSTools.QuickInfo
{
    internal sealed class XamlQuickLocaleInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _textBuffer;
        private readonly Workspace _workspace;

        public XamlQuickLocaleInfoSource(ITextBuffer textBuffer, Workspace workspace)
        {
            _textBuffer = textBuffer;
            _workspace = workspace;
        }

        public void Dispose() { }

        public Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session,
            CancellationToken cancellationToken)
        {
            var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
            if (triggerPoint == null)
            {
                Debug.WriteLine("ShowLangItem.Xaml: triggerPoint is null");
                return Task.FromResult<QuickInfoItem>(null);
            }

            var snapshot = _textBuffer.CurrentSnapshot;
            var position = triggerPoint.Value.Position;
            var text = snapshot.GetText();
            var match = XamlLocalizationKeyExtractor.TryGetLocalizationMatch(text, position);

            if (match == null)
                return Task.FromResult<QuickInfoItem>(null);

            var filePath = snapshot.TextBuffer != null
                && snapshot.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument)
                ? textDocument.FilePath
                : null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Debug.WriteLine("ShowLangItem.Xaml: file path not found");
                return Task.FromResult<QuickInfoItem>(null);
            }

            var pluginName = GetPluginName(filePath);
            var localizedTexts = LocalizationTextResolver.GetLocalizedTexts(pluginName, match.Key, match.IsCommon);

            var trackingSpan = snapshot.CreateTrackingSpan(
                new Span(match.Start, match.Length),
                SpanTrackingMode.EdgeInclusive);

            var content = QuickInfoContentBuilder.Build(localizedTexts, match.Key, _textBuffer, position, isXaml: true);
            return Task.FromResult(new QuickInfoItem(trackingSpan, content));
        }

        private string GetPluginName(string xamlFilePath)
        {
            if (_workspace != null)
            {
                var csFilePath = xamlFilePath + ".cs";
                var docIds = _workspace.CurrentSolution.GetDocumentIdsWithFilePath(csFilePath);
                if (docIds.Length > 0)
                {
                    var doc = _workspace.CurrentSolution.GetDocument(docIds[0]);
                    if (doc?.Project?.Name != null)
                    {
                        Debug.WriteLine($"ShowLangItem.Xaml: resolved plugin name '{doc.Project.Name}' via code-behind");
                        return doc.Project.Name;
                    }
                }
            }

            var fallback = LocalizationTextResolver.GetPluginNameFromFilePath(xamlFilePath);
            Debug.WriteLine($"ShowLangItem.Xaml: fallback plugin name '{fallback}' from file path");
            return fallback;
        }
    }
}
