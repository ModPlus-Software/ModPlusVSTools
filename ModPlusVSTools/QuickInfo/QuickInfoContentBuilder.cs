using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

using ModPlusVSTools.Models;

namespace ModPlusVSTools.QuickInfo
{
    internal static class QuickInfoContentBuilder
    {
        public static ContainerElement Build(
            IReadOnlyList<LocalizedTextEntry> texts,
            string key,
            ITextBuffer textBuffer,
            int insertPosition,
            bool isXaml = false)
        {
            var elements = new List<object>(texts.Count + 1);

            elements.Add(new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, "Локаль"),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, ": "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, key)));

            string ruText = null;
            foreach (var entry in texts)
            {
                if (entry.Locale == "ru-RU" && !string.IsNullOrEmpty(entry.Text))
                    ruText = entry.Text;

                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, entry.Locale),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, ": "),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.String, entry.DisplayText)));
            }

            var commentValue = ruText ?? key;
            var commentPreview = isXaml ? "<!-- " + commentValue + " -->" : "// " + commentValue;
            elements.Add(new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, string.Empty)));
            elements.Add(new ClassifiedTextElement(
                new ClassifiedTextRun(
                    PredefinedClassificationTypeNames.Keyword,
                    "Добавить комментарий",
                    () => InsertComment(textBuffer, insertPosition, commentValue, isXaml),
                    commentPreview)));

            return new ContainerElement(ContainerElementStyle.Stacked, elements.ToArray());
        }

        private static void InsertComment(ITextBuffer textBuffer, int position, string text, bool isXaml)
        {
            try
            {
                var snapshot = textBuffer.CurrentSnapshot;
                var line = snapshot.GetLineFromPosition(position);
                var insertPos = line.Start.Position;

                var lineText = line.GetText();
                var indent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

                var commentText = isXaml
                    ? indent + "<!-- " + text + " -->"
                    : indent + "// " + text;

                using (var edit = textBuffer.CreateEdit())
                {
                    edit.Insert(insertPos, commentText + Environment.NewLine);
                    edit.Apply();
                    Debug.WriteLine("ModPlusVSTools: Inserted '" + commentText + "' at position " + insertPos);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ModPlusVSTools: Error inserting comment - " + ex.Message);
            }
        }
    }
}
