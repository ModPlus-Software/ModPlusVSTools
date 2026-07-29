namespace ModPlusVSTools.Models
{
    internal sealed class XamlLocalizationMatch
    {
        public XamlLocalizationMatch(string key, int start, int length, bool isCommon)
        {
            Key = key;
            Start = start;
            Length = length;
            IsCommon = isCommon;
        }

        public string Key { get; }

        public int Start { get; }

        public int Length { get; }

        public bool IsCommon { get; }
    }
}
