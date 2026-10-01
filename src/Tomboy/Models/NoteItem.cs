using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Tomboy.Models
{
    public class NoteItem
    {
        public const string DATE_TIME_FORMAT = "yyyy-MM-ddTHH:mm:ss.fffffffzzz";

        public string Guid { get; set; } = System.Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Note";

        private string _xmlContent = string.Empty;
        private string? _textContent = null;

        public string XmlContent
        {
            get => _xmlContent;
            set
            {
                _xmlContent = value;
                _textContent = null;
            }
        }

        public string TextContent
        {
            get
            {
                if (_textContent == null)
                {
                    _textContent = ExtractPlainText(_xmlContent);
                }
                return _textContent;
            }
            set
            {
                _textContent = value;
            }
        }

        public static string ExtractPlainText(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            try
            {
                using var reader = new StringReader(source);
                using var xml = new XmlTextReader(reader) { Namespaces = false };
                var sb = new System.Text.StringBuilder();

                while (xml.Read())
                {
                    switch (xml.NodeType)
                    {
                        case XmlNodeType.Text:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            sb.Append(xml.Value);
                            break;
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return System.Text.RegularExpressions.Regex.Replace(source, "<[^>]+>", " ");
            }
        }
        public string Notebook { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();

        public DateTime CreateDate { get; set; } = DateTime.Now;
        public DateTime LastChangeDate { get; set; } = DateTime.Now;
        public DateTime LastMetadataChangeDate { get; set; } = DateTime.Now;

        public int CursorPosition { get; set; } = 0;
        public int SelectionBoundPosition { get; set; } = 0;
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 480;
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public bool IsOpenOnStartup { get; set; } = false;

        public string FilePath { get; set; } = string.Empty;

        public void SetNotebook(string? notebookName)
        {
            string newNb = string.IsNullOrWhiteSpace(notebookName) || notebookName == "(None)" 
                ? string.Empty 
                : notebookName.Trim();

            if (Notebook == newNb) return;

            Notebook = newNb;
            Tags.RemoveAll(t => t.StartsWith("system:notebook:"));

            if (!string.IsNullOrEmpty(Notebook))
            {
                Tags.Add($"system:notebook:{Notebook}");
            }

            LastMetadataChangeDate = DateTime.Now;
        }

        public static NoteItem LoadFromFile(string path)
        {
            var note = new NoteItem
            {
                FilePath = path,
                Guid = Path.GetFileNameWithoutExtension(path),
                CreateDate = DateTime.MinValue,
                LastChangeDate = DateTime.MinValue,
                LastMetadataChangeDate = DateTime.MinValue
            };

            try
            {
                var doc = new XmlDocument();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    doc.Load(reader);
                }

                var root = doc.DocumentElement;
                if (root != null)
                {
                    foreach (XmlNode child in root.ChildNodes)
                    {
                        switch (child.LocalName)
                        {
                            case "title":
                                note.Title = child.InnerText;
                                break;
                            case "text":
                                note.XmlContent = child.InnerXml;
                                break;
                            case "last-change-date":
                                if (DateTime.TryParse(child.InnerText, out var lcd))
                                    note.LastChangeDate = lcd.ToLocalTime();
                                break;
                            case "last-metadata-change-date":
                                if (DateTime.TryParse(child.InnerText, out var lmcd))
                                    note.LastMetadataChangeDate = lmcd.ToLocalTime();
                                break;
                            case "create-date":
                                if (DateTime.TryParse(child.InnerText, out var cd))
                                    note.CreateDate = cd.ToLocalTime();
                                break;
                            case "cursor-position":
                                if (int.TryParse(child.InnerText, out int cp))
                                    note.CursorPosition = cp;
                                break;
                            case "selection-bound-position":
                                if (int.TryParse(child.InnerText, out int sbp))
                                    note.SelectionBoundPosition = sbp;
                                break;
                            case "width":
                                if (int.TryParse(child.InnerText, out int w))
                                    note.Width = w;
                                break;
                            case "height":
                                if (int.TryParse(child.InnerText, out int h))
                                    note.Height = h;
                                break;
                            case "x":
                                if (int.TryParse(child.InnerText, out int x))
                                    note.X = x;
                                break;
                            case "y":
                                if (int.TryParse(child.InnerText, out int y))
                                    note.Y = y;
                                break;
                            case "open-on-startup":
                                if (bool.TryParse(child.InnerText, out bool oos))
                                    note.IsOpenOnStartup = oos;
                                break;
                            case "tags":
                                foreach (XmlNode tagNode in child.ChildNodes)
                                {
                                    if (tagNode.LocalName == "tag" && !string.IsNullOrEmpty(tagNode.InnerText))
                                    {
                                        if (!note.Tags.Contains(tagNode.InnerText))
                                            note.Tags.Add(tagNode.InnerText);
                                    }
                                }
                                break;
                            case "tag":
                                if (!string.IsNullOrEmpty(child.InnerText) && !note.Tags.Contains(child.InnerText))
                                {
                                    note.Tags.Add(child.InnerText);
                                }
                                break;
                        }
                    }
                }

                // Fallbacks matching Tomboy Note.CreateExistingNote
                if (note.LastChangeDate == DateTime.MinValue)
                {
                    note.LastChangeDate = File.GetLastWriteTime(path);
                }
                if (note.CreateDate == DateTime.MinValue)
                {
                    note.CreateDate = File.GetCreationTime(path);
                }
                if (note.LastMetadataChangeDate == DateTime.MinValue)
                {
                    note.LastMetadataChangeDate = note.LastChangeDate;
                }

                // Deduplicate and resolve notebook from tags
                var nbTag = note.Tags.LastOrDefault(t => t.StartsWith("system:notebook:"));
                if (nbTag != null)
                {
                    note.Notebook = nbTag.Substring("system:notebook:".Length);
                    note.Tags.RemoveAll(t => t.StartsWith("system:notebook:"));
                    note.Tags.Add(nbTag);
                }
                else
                {
                    note.Notebook = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading note {path}: {ex.Message}");
            }

            // Fallback: If XmlContent is still empty, guarantee valid structure
            if (string.IsNullOrWhiteSpace(note.XmlContent) && !string.IsNullOrWhiteSpace(note.Title))
            {
                note.XmlContent = $"<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>{note.Title}</note-title>\n\n</note-content>";
            }

            return note;
        }

        public void Save(string directory)
        {
            FilePath = Path.Combine(directory, $"{Guid}.note");
            Directory.CreateDirectory(directory);

            // Synchronize Notebook tag
            Tags.RemoveAll(t => t.StartsWith("system:notebook:"));
            if (!string.IsNullOrEmpty(Notebook))
            {
                Tags.Add($"system:notebook:{Notebook}");
            }

            // Ensure XmlContent wraps valid note-content
            string contentToWrite = XmlContent;
            if (string.IsNullOrWhiteSpace(contentToWrite))
            {
                contentToWrite = $"<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>{Title}</note-title>\n\n</note-content>";
            }

            string tmpFile = FilePath + ".tmp";
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = System.Text.Encoding.UTF8
            };

            using (var stream = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var xml = XmlWriter.Create(stream, settings))
            {
                xml.WriteStartDocument();
                xml.WriteStartElement(null, "note", "http://beatniksoftware.com/tomboy");
                xml.WriteAttributeString("version", "0.3");
                xml.WriteAttributeString("xmlns", "link", null, "http://beatniksoftware.com/tomboy/link");
                xml.WriteAttributeString("xmlns", "size", null, "http://beatniksoftware.com/tomboy/size");

                xml.WriteStartElement(null, "title", null);
                xml.WriteString(Title);
                xml.WriteEndElement();

                xml.WriteStartElement(null, "text", null);
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteRaw(contentToWrite);
                xml.WriteEndElement();

                xml.WriteStartElement(null, "last-change-date", null);
                xml.WriteString(LastChangeDate.ToString(DATE_TIME_FORMAT));
                xml.WriteEndElement();

                xml.WriteStartElement(null, "last-metadata-change-date", null);
                xml.WriteString(LastMetadataChangeDate.ToString(DATE_TIME_FORMAT));
                xml.WriteEndElement();

                if (CreateDate != DateTime.MinValue)
                {
                    xml.WriteStartElement(null, "create-date", null);
                    xml.WriteString(CreateDate.ToString(DATE_TIME_FORMAT));
                    xml.WriteEndElement();
                }

                xml.WriteStartElement(null, "cursor-position", null);
                xml.WriteString(CursorPosition.ToString());
                xml.WriteEndElement();

                xml.WriteStartElement(null, "selection-bound-position", null);
                xml.WriteString(SelectionBoundPosition.ToString());
                xml.WriteEndElement();

                xml.WriteStartElement(null, "width", null);
                xml.WriteString(Width.ToString());
                xml.WriteEndElement();

                xml.WriteStartElement(null, "height", null);
                xml.WriteString(Height.ToString());
                xml.WriteEndElement();

                xml.WriteStartElement(null, "x", null);
                xml.WriteString(X.ToString());
                xml.WriteEndElement();

                xml.WriteStartElement(null, "y", null);
                xml.WriteString(Y.ToString());
                xml.WriteEndElement();

                if (Tags.Count > 0)
                {
                    xml.WriteStartElement(null, "tags", null);
                    foreach (var t in Tags.Distinct())
                    {
                        xml.WriteStartElement(null, "tag", null);
                        xml.WriteString(t);
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement(); // tags
                }

                xml.WriteStartElement(null, "open-on-startup", null);
                xml.WriteString(IsOpenOnStartup.ToString());
                xml.WriteEndElement();

                xml.WriteEndElement(); // note
                xml.WriteEndDocument();
            }

            // Atomic file replace
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
            File.Move(tmpFile, FilePath);
        }
    }
}
