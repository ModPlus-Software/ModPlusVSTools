namespace ModPlusVSTools.Models
{
    internal sealed class LocaleSource
    {
        public LocaleSource(string locale, string path)
        {
            Locale = locale;
            Path = path;
        }

        public string Locale { get; }

        public string Path { get; }
    }
}
