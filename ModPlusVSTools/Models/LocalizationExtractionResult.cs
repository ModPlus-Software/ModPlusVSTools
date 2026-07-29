namespace ModPlusVSTools.Models
{
    internal sealed class LocalizationExtractionResult
    {
        public LocalizationExtractionResult(string key, bool isCommon)
        {
            Key = key;
            IsCommon = isCommon;
        }

        public string Key { get; }

        public bool IsCommon { get; }
    }
}
