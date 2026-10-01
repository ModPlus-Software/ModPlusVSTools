using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

using ModPlusVSTools.Models;

namespace ModPlusVSTools.Services
{
    internal static class LocalizationTextResolver
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, CachedDocument> Cache = new Dictionary<string, CachedDocument>(StringComparer.OrdinalIgnoreCase);

        public static string GetPluginName(Document document)
        {
            return document?.Project?.Name;
        }

        public static string GetPluginNameFromFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            return Path.GetFileName(directory);
        }

        public static IReadOnlyList<LocalizedTextEntry> GetLocalizedTexts(string pluginName, string localizationKey, bool isCommon = false)
        {
            var basePath = GetBaseLanguageFilesPath();
            if (string.IsNullOrEmpty(basePath))
                return new List<LocalizedTextEntry>();

            var locales = LocaleFolderDetector.GetLocales(basePath);
            var results = new List<LocalizedTextEntry>(locales.Count);

            foreach (var locale in locales)
            {
                var localeFolder = Path.Combine(basePath, locale);
                string text;
                string status;

                if (isCommon)
                {
                    var commonSource = new LocaleSource(locale, Path.Combine(localeFolder, "Common.xml"));
                    text = TryResolveCommonText(commonSource, localizationKey, out status);
                }
                else
                {
                    text = TryResolveFromFolder(localeFolder, locale, pluginName, localizationKey, out status);
                }

                results.Add(new LocalizedTextEntry(locale, text, status));
            }

            return results;
        }

        private static string TryResolveCommonText(LocaleSource localeSource, string localizationKey, out string status)
        {
            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                status = "localization key not found";
                return null;
            }

            var cachedDocument = GetCachedDocument(localeSource.Path);
            if (cachedDocument.ErrorMessage != null)
            {
                status = cachedDocument.ErrorMessage;
                return null;
            }

            var root = cachedDocument.Document?.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "ModPlus", StringComparison.Ordinal))
            {
                status = "invalid XML root";
                return null;
            }

            foreach (var section in root.Elements())
            {
                var keyElement = section.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, localizationKey, StringComparison.OrdinalIgnoreCase));

                if (keyElement != null)
                {
                    status = null;
                    return keyElement.Value?.Trim();
                }
            }

            status = $"key '{localizationKey}' not found";
            return null;
        }

        private static string TryResolveFromFolder(string localeFolder, string locale, string pluginName, string localizationKey, out string status)
        {
            if (!Directory.Exists(localeFolder))
            {
                status = $"folder not found: {localeFolder}";
                return null;
            }

            var xmlFiles = Directory.GetFiles(localeFolder, "*.xml", SearchOption.TopDirectoryOnly);
            if (xmlFiles.Length == 0)
            {
                status = $"no xml files in: {localeFolder}";
                return null;
            }

            string lastError = null;
            foreach (var xmlFile in xmlFiles)
            {
                var source = new LocaleSource(locale, xmlFile);
                var text = TryResolveText(source, pluginName, localizationKey, out var fileStatus);
                if (text != null)
                {
                    status = null;
                    return text;
                }

                if (fileStatus != null && !fileStatus.StartsWith("plugin '"))
                    lastError = fileStatus;
                else if (lastError == null)
                    lastError = fileStatus;
            }

            status = lastError ?? $"key '{localizationKey}' not found";
            return null;
        }

        private static string GetBaseLanguageFilesPath()
        {
            var configuredPath = ModPlusVSToolsPackage.Instance?.OptionsPage?.BaseLanguageFilesPath;
            return string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath.Trim();
        }

        private static string TryResolveText(LocaleSource localeSource, string pluginName, string localizationKey, out string status)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                status = "project name not found";
                return null;
            }

            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                status = "localization key not found";
                return null;
            }

            var cachedDocument = GetCachedDocument(localeSource.Path);
            if (cachedDocument.ErrorMessage != null)
            {
                status = cachedDocument.ErrorMessage;
                return null;
            }

            var root = cachedDocument.Document?.Root;
            if (root == null || !string.Equals(root.Name.LocalName, "ModPlus", StringComparison.Ordinal))
            {
                status = "invalid XML root";
                return null;
            }

            var pluginElement = root.Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, pluginName, StringComparison.OrdinalIgnoreCase));

            if (pluginElement == null)
            {
                status = $"plugin '{pluginName}' not found";
                return null;
            }

            var keyElement = pluginElement.Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, localizationKey, StringComparison.OrdinalIgnoreCase));

            if (keyElement == null)
            {
                status = $"key '{localizationKey}' not found";
                return null;
            }

            status = null;
            return keyElement.Value?.Trim();
        }

        private static CachedDocument GetCachedDocument(string path)
        {
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(path, out var cachedDocument))
                {
                    var lastWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    if (cachedDocument.LastWriteTimeUtc == lastWriteTimeUtc)
                    {
                        return cachedDocument;
                    }
                }

                CachedDocument refreshedDocument;

                if (!File.Exists(path))
                {
                    refreshedDocument = new CachedDocument(null, DateTime.MinValue, $"file not found: {path}");
                }
                else
                {
                    var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);

                    try
                    {
                        refreshedDocument = new CachedDocument(XDocument.Load(path), lastWriteTimeUtc, null);
                    }
                    catch (Exception ex)
                    {
                        refreshedDocument = new CachedDocument(null, lastWriteTimeUtc, $"failed to load XML: {ex.Message}");
                    }
                }

                Cache[path] = refreshedDocument;
                return refreshedDocument;
            }
        }
    }
}
