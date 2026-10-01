using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Tomboy.Services
{
    public class TomboySyncClient
    {
        private static TomboySyncClient? instance;
        public static TomboySyncClient Instance => instance ??= new TomboySyncClient();

        public static string ManifestFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "tomboy",
            "manifest.xml"
        );

        private DateTime lastSyncDate = DateTime.MinValue;
        private int lastSyncRev = -1;
        private string serverId = string.Empty;
        private readonly Dictionary<string, int> fileRevisions = new();
        private readonly Dictionary<string, string> deletedNotes = new();
        private readonly object lockObj = new();

        public DateTime LastSyncDate
        {
            get { lock (lockObj) return lastSyncDate; }
            set { lock (lockObj) { lastSyncDate = value; Save(); } }
        }

        public int LastSynchronizedRevision
        {
            get { lock (lockObj) return lastSyncRev; }
            set { lock (lockObj) { lastSyncRev = value; Save(); } }
        }

        public string AssociatedServerId
        {
            get { lock (lockObj) return serverId; }
            set { lock (lockObj) { serverId = value; Save(); } }
        }

        public Dictionary<string, int> FileRevisions
        {
            get { lock (lockObj) return new Dictionary<string, int>(fileRevisions); }
        }

        public Dictionary<string, string> DeletedNotes
        {
            get { lock (lockObj) return new Dictionary<string, string>(deletedNotes); }
        }

        public TomboySyncClient()
        {
            Load();
        }

        public void Load()
        {
            lock (lockObj)
            {
                fileRevisions.Clear();
                deletedNotes.Clear();
                lastSyncDate = DateTime.MinValue;
                lastSyncRev = -1;
                serverId = string.Empty;

                string path = ManifestFilePath;
                if (!File.Exists(path))
                {
                    return;
                }

                try
                {
                    var doc = new XmlDocument();
                    doc.Load(path);

                    var syncDateNode = doc.SelectSingleNode("//*[local-name()='last-sync-date']");
                    if (syncDateNode != null && DateTime.TryParse(syncDateNode.InnerText, out var d))
                    {
                        lastSyncDate = d;
                    }

                    var syncRevNode = doc.SelectSingleNode("//*[local-name()='last-sync-rev']");
                    if (syncRevNode != null && int.TryParse(syncRevNode.InnerText, out var r))
                    {
                        lastSyncRev = r;
                    }

                    var serverIdNode = doc.SelectSingleNode("//*[local-name()='server-id']");
                    if (serverIdNode != null)
                    {
                        serverId = serverIdNode.InnerText.Trim();
                    }

                    var revNodes = doc.SelectNodes("//*[local-name()='note-revisions']/*[local-name()='note']");
                    if (revNodes != null)
                    {
                        foreach (XmlNode n in revNodes)
                        {
                            string? guid = n.Attributes?["guid"]?.Value;
                            string? revStr = n.Attributes?["latest-revision"]?.Value;
                            if (!string.IsNullOrEmpty(guid) && int.TryParse(revStr, out var rev))
                            {
                                fileRevisions[guid] = rev;
                            }
                        }
                    }

                    var delNodes = doc.SelectNodes("//*[local-name()='note-deletions']/*[local-name()='note']");
                    if (delNodes != null)
                    {
                        foreach (XmlNode n in delNodes)
                        {
                            string? guid = n.Attributes?["guid"]?.Value;
                            string? title = n.Attributes?["title"]?.Value ?? string.Empty;
                            if (!string.IsNullOrEmpty(guid))
                            {
                                deletedNotes[guid] = title;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TomboySyncClient] Error reading manifest.xml: {ex.Message}");
                }
            }
        }

        public void Save()
        {
            lock (lockObj)
            {
                string path = ManifestFilePath;
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                try
                {
                    var settings = new XmlWriterSettings
                    {
                        Indent = true,
                        OmitXmlDeclaration = false,
                        Encoding = System.Text.Encoding.UTF8
                    };

                    using var writer = XmlWriter.Create(path, settings);
                    writer.WriteStartDocument();
                    writer.WriteStartElement("manifest", "http://beatniksoftware.com/tomboy");

                    writer.WriteStartElement("last-sync-date");
                    writer.WriteString(lastSyncDate.ToString("o"));
                    writer.WriteEndElement();

                    writer.WriteStartElement("last-sync-rev");
                    writer.WriteString(lastSyncRev.ToString());
                    writer.WriteEndElement();

                    writer.WriteStartElement("server-id");
                    writer.WriteString(serverId);
                    writer.WriteEndElement();

                    writer.WriteStartElement("note-revisions");
                    foreach (var kvp in fileRevisions)
                    {
                        writer.WriteStartElement("note");
                        writer.WriteAttributeString("guid", kvp.Key);
                        writer.WriteAttributeString("latest-revision", kvp.Value.ToString());
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); // note-revisions

                    writer.WriteStartElement("note-deletions");
                    foreach (var kvp in deletedNotes)
                    {
                        writer.WriteStartElement("note");
                        writer.WriteAttributeString("guid", kvp.Key);
                        writer.WriteAttributeString("title", kvp.Value);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); // note-deletions

                    writer.WriteEndElement(); // manifest
                    writer.WriteEndDocument();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TomboySyncClient] Error writing manifest.xml: {ex.Message}");
                }
            }
        }

        public void RecordNoteDeleted(string guid, string title)
        {
            lock (lockObj)
            {
                deletedNotes[guid] = title;
                fileRevisions.Remove(guid);
                Save();
            }
        }

        public int GetRevision(string guid)
        {
            lock (lockObj)
            {
                return fileRevisions.TryGetValue(guid, out var rev) ? rev : -1;
            }
        }

        public void SetRevision(string guid, int revision)
        {
            lock (lockObj)
            {
                fileRevisions[guid] = revision;
            }
        }

        public void RemoveRevision(string guid)
        {
            lock (lockObj)
            {
                fileRevisions.Remove(guid);
            }
        }

        public void CommitSync(int newRevision, DateTime syncDate)
        {
            lock (lockObj)
            {
                lastSyncRev = newRevision;
                lastSyncDate = syncDate;
                deletedNotes.Clear();
                Save();
            }
        }
    }
}
