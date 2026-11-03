using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

using ModPlus.LanguageTools;

using ModPlusVSTools.Localization.Models;

namespace ModPlusVSTools.Localization.Services
{
    /// <summary>
    /// Сведения об узле локализации из каталога (без записей).
    /// </summary>
    internal sealed class ModuleInfo
    {
        /// <summary>Имя узла.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Ключ источника.</summary>
        public string SourceKey { get; set; } = string.Empty;

        /// <summary>Имя группы.</summary>
        public string DomainName { get; set; } = string.Empty;

        /// <summary>Файлы по языкам.</summary>
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Файл без суффикса языка.</summary>
        public bool UsesSharedFileName { get; set; }

        /// <summary>Наибольшее число строк по языкам.</summary>
        public int EntryCount { get; set; }
    }

    /// <summary>
    /// Каталог узлов локализации.
    /// </summary>
    internal sealed class CatalogData
    {
        /// <summary>Языки.</summary>
        public IReadOnlyList<string> Languages { get; set; } = Array.Empty<string>();

        /// <summary>Узлы.</summary>
        public List<ModuleInfo> Modules { get; } = new List<ModuleInfo>();

        /// <summary>Файлы, которые не удалось прочитать.</summary>
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Снимок записи для сохранения в фоновом потоке.
    /// </summary>
    internal sealed class EntrySnapshot
    {
        /// <summary>Запись.</summary>
        public LocEntry Entry { get; set; } = null!;

        /// <summary>Имя.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Имя на диске.</summary>
        public string? OriginalName { get; set; }

        /// <summary>Комментарий.</summary>
        public string? Comment { get; set; }

        /// <summary>Значения по языкам.</summary>
        public string?[] Values { get; set; } = Array.Empty<string?>();
    }

    /// <summary>
    /// Снимок модуля для сохранения в фоновом потоке.
    /// </summary>
    internal sealed class ModuleSnapshot
    {
        /// <summary>Модуль.</summary>
        public LocModule Module { get; set; } = null!;

        /// <summary>Версия изменений на момент снимка.</summary>
        public int Version { get; set; }

        /// <summary>Модуль новый (файлов ещё нет).</summary>
        public bool IsNew { get; set; }

        /// <summary>Файлы по языкам на момент снимка.</summary>
        public Dictionary<string, string> Files { get; set; } = null!;

        /// <summary>Строки.</summary>
        public List<EntrySnapshot> Items { get; } = new List<EntrySnapshot>();

        /// <summary>Атрибуты.</summary>
        public List<EntrySnapshot> Attributes { get; } = new List<EntrySnapshot>();

        /// <summary>Удалённые строки.</summary>
        public List<string> RemovedItems { get; } = new List<string>();

        /// <summary>Записанные файлы по языкам (заполняется при сохранении).</summary>
        public Dictionary<string, string> SavedFiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Чтение и запись исходных файлов локализации.
    /// </summary>
    /// <remarks>
    /// Формат записи — <see cref="SourceXml.SaveSettings"/> из ModPlus.LanguageTools (его используют LangFilesEditor и сборка):
    /// отступы, каждый атрибут с новой строки, CRLF, UTF-8 с BOM, объявление xml. Файл перезаписывается
    /// только если в нём что-то изменилось, поэтому сохранение не порождает лишних изменений в git.
    /// </remarks>
    internal static class LocalizationRepository
    {
        private static readonly XmlReaderSettings CatalogReaderSettings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
        };

        /// <summary>
        /// Читает каталог узлов: имена узлов и число строк. Файлы читаются потоковым XmlReader
        /// без построения дерева — каталог из ~1700 файлов строится за доли секунды.
        /// </summary>
        /// <param name="root">Папка исходных файлов локализации.</param>
        /// <param name="cancellationToken">Отмена.</param>
        public static CatalogData LoadCatalog(string root, CancellationToken cancellationToken)
        {
            var catalog = new CatalogData { Languages = LocalizationFolder.GetLanguages(root) };
            var modules = new Dictionary<string, ModuleInfo>(StringComparer.Ordinal);

            foreach (var language in catalog.Languages)
            {
                foreach (var file in Directory.EnumerateFiles(Path.Combine(root, language), "*.xml", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var sourceKey = LocalizationFolder.GetSourceKey(fileName, language);
                    var sharedFileName = string.Equals(fileName, sourceKey, StringComparison.OrdinalIgnoreCase);

                    List<KeyValuePair<string, int>> nodes;
                    try
                    {
                        nodes = ReadNodes(file);
                    }
                    catch (Exception exception) when (exception is XmlException || exception is IOException)
                    {
                        catalog.Errors.Add($"{file}: {exception.Message}");
                        continue;
                    }

                    foreach (var node in nodes)
                    {
                        var key = sourceKey + "|" + node.Key;
                        if (!modules.TryGetValue(key, out var info))
                        {
                            info = new ModuleInfo
                            {
                                Name = node.Key,
                                SourceKey = sourceKey,
                                DomainName = LocalizationFolder.GetDomainName(sourceKey),
                            };
                            modules[key] = info;
                            catalog.Modules.Add(info);
                        }

                        info.Files[language] = file;
                        info.UsesSharedFileName |= sharedFileName;
                        if (node.Value > info.EntryCount)
                            info.EntryCount = node.Value;
                    }
                }
            }

            return catalog;
        }

        /// <summary>
        /// Читает записи модуля из файлов всех языков.
        /// </summary>
        /// <param name="moduleName">Имя узла.</param>
        /// <param name="files">Файлы по языкам.</param>
        /// <param name="languages">Языки.</param>
        /// <param name="attributes">Атрибуты.</param>
        /// <param name="items">Строки.</param>
        public static void LoadModule(
            string moduleName,
            IReadOnlyDictionary<string, string> files,
            IReadOnlyList<string> languages,
            out List<LocEntry> attributes,
            out List<LocEntry> items)
        {
            attributes = new List<LocEntry>();
            items = new List<LocEntry>();
            var attributeIndex = new Dictionary<string, LocEntry>(StringComparer.Ordinal);
            var itemIndex = new Dictionary<string, LocEntry>(StringComparer.Ordinal);

            for (var language = 0; language < languages.Count; language++)
            {
                if (!files.TryGetValue(languages[language], out var file) || !File.Exists(file))
                    continue;

                var node = XElement.Load(file).Element(moduleName);
                if (node == null)
                    continue;

                foreach (var attribute in node.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                        continue;

                    var entry = GetOrCreate(attributes, attributeIndex, attribute.Name.LocalName, languages.Count, isAttribute: true);
                    entry.SetRawValue(language, attribute.Value);
                }

                foreach (var element in node.Elements())
                {
                    var entry = GetOrCreate(items, itemIndex, element.Name.LocalName, languages.Count, isAttribute: false);
                    if (string.IsNullOrEmpty(entry.Comment) && element.PreviousNode is XComment comment)
                        entry.SetRawComment(comment.Value);

                    entry.SetRawValue(language, element.Value);
                }
            }
        }

        /// <summary>
        /// Снимок модуля для сохранения. Вызывается в UI-потоке.
        /// </summary>
        /// <param name="module">Модуль.</param>
        public static ModuleSnapshot CreateSnapshot(LocModule module)
        {
            var snapshot = new ModuleSnapshot
            {
                Module = module,
                Version = module.ChangeVersion,
                IsNew = module.IsNew,
                Files = new Dictionary<string, string>(module.FilesByLanguage, StringComparer.OrdinalIgnoreCase),
            };

            foreach (var entry in module.Items)
                snapshot.Items.Add(CreateEntrySnapshot(entry));
            foreach (var entry in module.Attributes)
                snapshot.Attributes.Add(CreateEntrySnapshot(entry));

            snapshot.RemovedItems.AddRange(module.RemovedItemNames);
            return snapshot;
        }

        /// <summary>
        /// Записывает модуль во все языковые файлы. Может выполняться в фоновом потоке.
        /// </summary>
        /// <param name="root">Папка исходных файлов локализации.</param>
        /// <param name="snapshot">Снимок модуля.</param>
        /// <param name="languages">Языки.</param>
        /// <param name="usesSharedFileName">Файл без суффикса языка.</param>
        /// <param name="sourceKey">Ключ источника.</param>
        /// <param name="moduleName">Имя узла.</param>
        /// <returns>Число перезаписанных файлов.</returns>
        public static int Save(
            string root,
            ModuleSnapshot snapshot,
            IReadOnlyList<string> languages,
            bool usesSharedFileName,
            string sourceKey,
            string moduleName)
        {
            var written = 0;

            for (var language = 0; language < languages.Count; language++)
            {
                var languageName = languages[language];
                if (!snapshot.Files.TryGetValue(languageName, out var file) || !File.Exists(file))
                    file = LocalizationFolder.BuildSourceFilePath(root, languageName, sourceKey, usesSharedFileName);

                var exists = File.Exists(file);

                // У существующего узла нет файла этого языка и нет значений для него — файл не создаётся
                if (!exists && !snapshot.IsNew && !HasValues(snapshot, language))
                    continue;

                var document = exists ? XElement.Load(file) : new XElement("ModPlus");
                var modified = !exists;

                var node = document.Element(moduleName);
                if (node == null)
                {
                    node = new XElement(moduleName);
                    document.Add(node);
                    modified = true;
                }

                modified |= RemoveItems(node, snapshot.RemovedItems);
                modified |= WriteAttributes(node, snapshot.Attributes, language);
                modified |= WriteItems(node, snapshot.Items, language);

                if (modified)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    WriteDocument(file, document);
                    written++;
                }

                snapshot.SavedFiles[languageName] = file;
            }

            return written;
        }

        /// <summary>
        /// Есть ли на диске файл источника с указанным ключом хотя бы в одном языке.
        /// </summary>
        /// <param name="root">Папка исходных файлов локализации.</param>
        /// <param name="languages">Языки.</param>
        /// <param name="sourceKey">Ключ источника.</param>
        public static bool SourceExists(string root, IReadOnlyList<string> languages, string sourceKey)
        {
            foreach (var language in languages)
            {
                if (File.Exists(LocalizationFolder.BuildSourceFilePath(root, language, sourceKey, false)) ||
                    File.Exists(LocalizationFolder.BuildSourceFilePath(root, language, sourceKey, true)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValues(ModuleSnapshot snapshot, int language)
        {
            foreach (var entry in snapshot.Items)
            {
                if (entry.Values[language] != null)
                    return true;
            }

            foreach (var entry in snapshot.Attributes)
            {
                if (entry.Values[language] != null)
                    return true;
            }

            return false;
        }

        private static EntrySnapshot CreateEntrySnapshot(LocEntry entry)
        {
            var values = new string?[entry.LanguageCount];
            for (var i = 0; i < values.Length; i++)
                values[i] = entry.GetRawValue(i);

            return new EntrySnapshot
            {
                Entry = entry,
                Name = entry.Name,
                OriginalName = entry.OriginalName,
                Comment = entry.Comment,
                Values = values,
            };
        }

        /// <summary>
        /// Имена узлов файла и число их дочерних элементов.
        /// </summary>
        private static List<KeyValuePair<string, int>> ReadNodes(string file)
        {
            var result = new List<KeyValuePair<string, int>>();
            using var reader = XmlReader.Create(file, CatalogReaderSettings);

            string? currentName = null;
            var currentCount = 0;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (reader.Depth == 1)
                {
                    if (currentName != null)
                        result.Add(new KeyValuePair<string, int>(currentName, currentCount));

                    currentName = reader.LocalName;
                    currentCount = 0;
                }
                else if (reader.Depth == 2)
                {
                    currentCount++;
                }
            }

            if (currentName != null)
                result.Add(new KeyValuePair<string, int>(currentName, currentCount));

            return result;
        }

        private static LocEntry GetOrCreate(
            List<LocEntry> target,
            Dictionary<string, LocEntry> index,
            string name,
            int languageCount,
            bool isAttribute)
        {
            if (index.TryGetValue(name, out var entry))
                return entry;

            entry = new LocEntry(name, languageCount, isAttribute) { OriginalName = name };
            target.Add(entry);
            index[name] = entry;
            return entry;
        }

        private static bool RemoveItems(XElement node, List<string> names)
        {
            var modified = false;
            foreach (var name in names)
            {
                var element = node.Element(name);
                if (element == null)
                    continue;

                if (element.PreviousNode is XComment comment && DeletionMarker.IsMarked(comment.Value))
                    comment.Remove();

                element.Remove();
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Атрибуты записываются в порядке записей редактора: если состав, порядок или значения
        /// отличаются от файла, атрибуты узла заменяются целиком.
        /// </summary>
        private static bool WriteAttributes(XElement node, List<EntrySnapshot> attributes, int language)
        {
            var desired = new List<KeyValuePair<string, string>>(attributes.Count);
            foreach (var attribute in attributes)
            {
                var value = attribute.Values[language];
                if (value != null)
                    desired.Add(new KeyValuePair<string, string>(attribute.Name, QuoteNormalizer.Normalize(value)));
            }

            var current = node.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
            var same = current.Count == desired.Count;
            for (var i = 0; same && i < current.Count; i++)
            {
                same = current[i].Name.LocalName == desired[i].Key &&
                       string.Equals(current[i].Value, desired[i].Value, StringComparison.Ordinal);
            }

            if (same)
                return false;

            node.ReplaceAttributes(desired.Select(pair => new XAttribute(pair.Key, pair.Value)));
            return true;
        }

        /// <summary>
        /// Записывает строки: значения, имена (переименование), комментарии и порядок.
        /// Элемент перемещается вместе со своим комментарием; чужие комментарии
        /// (границы диапазонов «todo remove after X start/end») остаются на месте.
        /// </summary>
        private static bool WriteItems(XElement node, List<EntrySnapshot> items, int language)
        {
            var modified = false;
            XElement? previous = null;

            // Элементы ищутся по именам на диске до любых изменений: иначе при обмене именами
            // двух записей (A→B, B→A) вторая нашла бы уже переименованный первый элемент
            var elementsByName = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var existing in node.Elements())
            {
                if (!elementsByName.ContainsKey(existing.Name.LocalName))
                    elementsByName[existing.Name.LocalName] = existing;
            }

            foreach (var item in items)
            {
                var value = item.Values[language];
                XElement? element = null;
                if (item.OriginalName != null)
                    elementsByName.TryGetValue(item.OriginalName, out element);

                if (element == null)
                {
                    // Значения для языка нет — ключ в этом языке не создаётся
                    if (value == null)
                        continue;

                    element = new XElement(item.Name);
                    Insert(node, previous, element, null);
                    modified = true;
                }
                else
                {
                    if (element.Name.LocalName != item.Name)
                    {
                        element.Name = item.Name;
                        modified = true;
                    }

                    var expected = previous == null ? node.Elements().FirstOrDefault() : previous.ElementsAfterSelf().FirstOrDefault();
                    if (!ReferenceEquals(expected, element))
                    {
                        var comment = GetOwnComment(element, item.Comment);
                        comment?.Remove();
                        element.Remove();
                        Insert(node, previous, element, comment);
                        modified = true;
                    }
                }

                if (value != null)
                {
                    value = QuoteNormalizer.Normalize(value);
                    if (element.HasElements || !string.Equals(element.Value, value, StringComparison.Ordinal))
                    {
                        element.SetValue(value);
                        modified = true;
                    }
                }

                modified |= WriteComment(element, item.Comment);
                previous = element;
            }

            return modified;
        }

        private static void Insert(XElement node, XElement? previous, XElement element, XComment? comment)
        {
            if (previous == null)
            {
                if (comment != null)
                    node.AddFirst(comment, element);
                else
                    node.AddFirst(element);
            }
            else
            {
                if (comment != null)
                    previous.AddAfterSelf(comment, element);
                else
                    previous.AddAfterSelf(element);
            }
        }

        /// <summary>
        /// Комментарий, принадлежащий элементу: предшествующий комментарий, совпадающий
        /// с комментарием записи или являющийся пометкой к удалению.
        /// </summary>
        private static XComment? GetOwnComment(XElement element, string? entryComment)
        {
            if (element.PreviousNode is not XComment comment)
                return null;

            return string.Equals(comment.Value, entryComment, StringComparison.Ordinal) || DeletionMarker.IsMarked(comment.Value)
                ? comment
                : null;
        }

        /// <summary>
        /// Записывает или обновляет комментарий перед элементом (как ModuleXmlSerializer.WriteItemComment).
        /// </summary>
        private static bool WriteComment(XElement element, string? comment)
        {
            if (string.IsNullOrEmpty(comment))
            {
                // Снимается только пометка к удалению: чужие комментарии не теряются
                if (element.PreviousNode is not XComment obsolete || !DeletionMarker.IsMarked(obsolete.Value))
                    return false;

                obsolete.Remove();
                return true;
            }

            if (element.PreviousNode is XComment existing)
            {
                if (existing.Value == comment)
                    return false;

                existing.Value = comment!;
                return true;
            }

            element.AddBeforeSelf(new XComment(comment!));
            return true;
        }

        /// <summary>
        /// Документ сначала записывается в память и только потом на диск: если запись прервётся
        /// исключением, файл на диске не останется обрезанным.
        /// </summary>
        private static void WriteDocument(string file, XElement document)
        {
            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, SourceXml.SaveSettings))
                document.WriteTo(writer);

            File.WriteAllBytes(file, stream.ToArray());
        }
    }
}
