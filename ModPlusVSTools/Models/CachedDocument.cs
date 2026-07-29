using System;
using System.Xml.Linq;

namespace ModPlusVSTools.Models
{
    internal sealed class CachedDocument
    {
        public CachedDocument(XDocument document, DateTime lastWriteTimeUtc, string errorMessage)
        {
            Document = document;
            LastWriteTimeUtc = lastWriteTimeUtc;
            ErrorMessage = errorMessage;
        }

        public XDocument Document { get; }

        public DateTime LastWriteTimeUtc { get; }

        public string ErrorMessage { get; }
    }
}
