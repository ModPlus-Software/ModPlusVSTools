using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using ModPlusVSTools.Models;

namespace ModPlusVSTools.Services
{
    internal static class LocalizationKeyExtractor
    {
        public static async Task<LocalizationExtractionResult> TryGetLocalizationKeyAsync(
            Document document,
            int position,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
                return null;

            var token = root.FindToken(position);
            if (token.Kind() != SyntaxKind.StringLiteralToken && position > 0)
                token = root.FindToken(position - 1);
            if (token.Kind() != SyntaxKind.StringLiteralToken)
                return null;

            if (!(token.Parent is LiteralExpressionSyntax literalExpr))
                return null;

            if (!(literalExpr.Parent is ArgumentSyntax argument))
                return null;

            if (!(argument.Parent is ArgumentListSyntax argumentList))
                return null;

            if (argumentList.Arguments.Count == 0 || argumentList.Arguments[0] != argument)
                return null;

            if (!(argumentList.Parent is InvocationExpressionSyntax invocation))
                return null;

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
                return null;

            var methodName = memberAccess.Name.Identifier.Text;
            bool isCommon;
            if (methodName == "GetItem")
                isCommon = false;
            else if (methodName == "GetCommonItem")
                isCommon = true;
            else
                return null;

            ExpressionSyntax receiver = memberAccess.Expression;
            if (receiver is IdentifierNameSyntax directName)
            {
                if (directName.Identifier.Text != "Language")
                    return null;
            }
            else if (receiver is MemberAccessExpressionSyntax qualifiedAccess)
            {
                if (qualifiedAccess.Name.Identifier.Text != "Language" ||
                    !(qualifiedAccess.Expression is IdentifierNameSyntax nsName) ||
                    nsName.Identifier.Text != "ModPlusAPI")
                    return null;
            }
            else
            {
                return null;
            }

            return new LocalizationExtractionResult(token.ValueText, isCommon);
        }
    }
}
