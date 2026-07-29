using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

using ModPlusVSTools.Services;

namespace ModPlusVSTools.QuickInfo
{
    internal sealed class QuickLocaleInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _textBuffer;

        public QuickLocaleInfoSource(ITextBuffer textBuffer)
        {
            _textBuffer = textBuffer;
        }

        public void Dispose() { }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session,
            CancellationToken cancellationToken)
        {
            var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
            if (triggerPoint == null)
            {
                Debug.WriteLine("ModPlusVSTools: triggerPoint is null");
                return null;
            }

            var document = GetDocument();
            if (document == null)
            {
                Debug.WriteLine("ModPlusVSTools: document lookup failed");
                return null;
            }

            int position = triggerPoint.Value.Position;

            var extractionResult = await LocalizationKeyExtractor.TryGetLocalizationKeyAsync(
                document, position, cancellationToken).ConfigureAwait(false);

            if (extractionResult == null)
                return null;

            var pluginName = LocalizationTextResolver.GetPluginName(document);
            var localizedTexts = LocalizationTextResolver.GetLocalizedTexts(pluginName, extractionResult.Key, extractionResult.IsCommon);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var token = root.FindToken(position);
            if (token.Kind() != SyntaxKind.StringLiteralToken && position > 0)
                token = root.FindToken(position - 1);

            var tokenSpan = new Span(token.Span.Start, token.Span.Length);
            var trackingSpan = _textBuffer.CurrentSnapshot.CreateTrackingSpan(
                tokenSpan, SpanTrackingMode.EdgeInclusive);

            var content = QuickInfoContentBuilder.Build(localizedTexts, extractionResult.Key, _textBuffer, position);

            return new QuickInfoItem(trackingSpan, content);
        }

        private Document GetDocument()
        {
            var textContainer = _textBuffer.AsTextContainer();

            if (!Workspace.TryGetWorkspace(textContainer, out var workspace))
            {
                Debug.WriteLine("ModPlusVSTools: TryGetWorkspace returned false – buffer not registered with a Roslyn workspace");
                return null;
            }

            var documentId = workspace.GetDocumentIdInCurrentContext(textContainer);
            if (documentId == null)
            {
                Debug.WriteLine("ModPlusVSTools: GetDocumentIdInCurrentContext returned null");
                return null;
            }

            var document = workspace.CurrentSolution.GetDocument(documentId);
            if (document == null)
                Debug.WriteLine($"ModPlusVSTools: GetDocument({documentId}) returned null");

            return document;
        }
    }
}
