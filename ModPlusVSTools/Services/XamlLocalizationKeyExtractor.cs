using System;
using System.Text.RegularExpressions;

using ModPlusVSTools.Models;

namespace ModPlusVSTools.Services
{
    internal static class XamlLocalizationKeyExtractor
    {
        private static readonly Regex BindingRegex = new Regex(
            @"Source\s*=\s*\{StaticResource\s+(?<resource>Lang|LangCommon)\}\s*,\s*XPath\s*=\s*(?<key>[A-Za-z_][A-Za-z0-9_\-\.]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static XamlLocalizationMatch TryGetLocalizationMatch(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0 || position > text.Length)
                return null;

            foreach (Match match in BindingRegex.Matches(text))
            {
                var keyGroup = match.Groups["key"];
                if (!keyGroup.Success)
                    continue;

                if (position < keyGroup.Index || position > keyGroup.Index + keyGroup.Length)
                    continue;

                var resourceGroup = match.Groups["resource"];
                var isCommon = resourceGroup.Success &&
                               string.Equals(resourceGroup.Value, "LangCommon", StringComparison.OrdinalIgnoreCase);

                return new XamlLocalizationMatch(keyGroup.Value, keyGroup.Index, keyGroup.Length, isCommon);
            }

            return null;
        }
    }
}
