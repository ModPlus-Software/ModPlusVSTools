namespace ModPlusVSTools.Models
{
    internal sealed class LocalizedTextEntry
    {
        public LocalizedTextEntry(string locale, string text, string status)
        {
            Locale = locale;
            Text = text;
            Status = status;
        }

        public string Locale { get; }

        public string Text { get; }

        public string Status { get; }

        public string DisplayText => !string.IsNullOrEmpty(Text) ? Text : "ПЕРЕВОД ОТСУТСТВУЕТ";
    }
}
