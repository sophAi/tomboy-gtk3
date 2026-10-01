using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Tomboy.Models;

namespace Tomboy.Services
{
    public class FileSystemSyncServer
    {
        private readonly string serverPath;
        private readonly string manifestPath;
        private readonly string lockPath;

        public int LatestRevision { get; private set; } = 0;
        public string ServerId { get; private set; } = string.Empty;
        public Dictionary<string, int> ServerNotes { get; } = new();

        public FileSystemSyncServer(string path)
        {
            serverPath = path;
            if (!Directory.Exists(serverPath))
            {
                Directory.CreateDirectory(serverPath);
            }

            manifestPath = Path.Combine(serverPath, "manifest.xml");
            lockPath = Path.Combine(serverPath, "lock");

            LoadManifest();
        }

        private void LoadManifest()
        {
            ServerNotes.Clear();
            LatestRevision = 0;
            ServerId = Guid.NewGuid().ToString();

            if (File.Exists(manifestPath))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(manifestPath);

                    var syncNode = doc.SelectSingleNode("//sync") ?? doc.SelectSingleNode("//*[local-name()='sync']");
                    if (syncNode != null)
                    {
                        var revAttr = syncNode.Attributes?["revision"]?.Value;
                        if (!string.IsNullOrEmpty(revAttr) && int.TryParse(revAttr, out var rev))
                        {
                            LatestRevision = rev;
                        }

                        var idAttr = syncNode.Attributes?["server-id"]?.Value;
                        if (!string.IsNullOrEmpty(idAttr))
                        {
                            ServerId = idAttr;
                        }
                    }

                    var noteNodes = doc.SelectNodes("//note") ?? doc.SelectNodes("//*[local-name()='note']");
                    if (noteNodes != null)
                    {
                        foreach (XmlNode n in noteNodes)
                        {
                            string? id = n.Attributes?["id"]?.Value ?? n.Attributes?["guid"]?.Value;
                            string? revStr = n.Attributes?["rev"]?.Value ?? n.Attributes?["latest-revision"]?.Value;
                            int noteRev = 0;
                            if (!string.IsNullOrEmpty(revStr))
                            {
                                int.TryParse(revStr, out noteRev);
                            }

                            if (!string.IsNullOrEmpty(id))
                            {
                                ServerNotes[id] = noteRev;
                            }
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileSystemSyncServer] Error loading {manifestPath}: {ex.Message}");
                }
            }

            // Manifest does not exist or failed to load. Discover existing notes on the server!
            DiscoverExistingNotes();
        }

        private void DiscoverExistingNotes()
        {
            try
            {
                // Check flat notes in serverPath
                foreach (var file in Directory.GetFiles(serverPath, "*.note"))
                {
                    string guid = Path.GetFileNameWithoutExtension(file);
                    if (!ServerNotes.ContainsKey(guid))
                    {
                        ServerNotes[guid] = 0;
                    }
                }

                // Check revision subdirectories: serverPath/*/*/*.note
                foreach (var dir in Directory.GetDirectories(serverPath))
                {
                    string dirName = Path.GetFileName(dir);
                    if (int.TryParse(dirName, out _))
                    {
                        foreach (var subDir in Directory.GetDirectories(dir))
                        {
                            string subName = Path.GetFileName(subDir);
                            if (int.TryParse(subName, out var rev))
                            {
                                if (rev > LatestRevision) LatestRevision = rev;
                                foreach (var file in Directory.GetFiles(subDir, "*.note"))
                                {
                                    string guid = Path.GetFileNameWithoutExtension(file);
                                    if (!ServerNotes.ContainsKey(guid) || ServerNotes[guid] < rev)
                                    {
                                        ServerNotes[guid] = rev;
                                    }
                                }
                            }
                        }
                    }
                }

                if (ServerNotes.Count > 0)
                {
                    Commit(LatestRevision, ServerId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileSystemSyncServer] Error discovering notes: {ex.Message}");
            }
        }

        public string GetRevisionDirPath(int rev)
        {
            return Path.Combine(
                serverPath,
                (rev / 100).ToString(),
                rev.ToString()
            );
        }

        public string? FindNoteFilePath(string guid, int rev)
        {
            // 1. Revision path
            string revDir = GetRevisionDirPath(rev);
            string revNotePath = Path.Combine(revDir, guid + ".note");
            if (File.Exists(revNotePath)) return revNotePath;

            // 2. Flat root path
            string rootNotePath = Path.Combine(serverPath, guid + ".note");
            if (File.Exists(rootNotePath)) return rootNotePath;

            // 3. Fallback search across revision folders
            try
            {
                var matches = Directory.GetFiles(serverPath, guid + ".note", SearchOption.AllDirectories);
                if (matches.Length > 0)
                {
                    return matches[0];
                }
            }
            catch { }

            return null;
        }

        public string? GetNoteXml(string guid, int rev)
        {
            string? filePath = FindNoteFilePath(guid, rev);
            if (filePath != null && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            return null;
        }

        public void UploadNote(NoteItem note, int newRev)
        {
            string revDir = GetRevisionDirPath(newRev);
            if (!Directory.Exists(revDir))
            {
                Directory.CreateDirectory(revDir);
            }

            string revNotePath = Path.Combine(revDir, note.Guid + ".note");
            string rootNotePath = Path.Combine(serverPath, note.Guid + ".note");

            note.Save(revDir);
            try
            {
                File.Copy(revNotePath, rootNotePath, true);
            }
            catch { }

            ServerNotes[note.Guid] = newRev;
        }

        public void DeleteNote(string guid)
        {
            ServerNotes.Remove(guid);

            // Delete from root
            string rootNotePath = Path.Combine(serverPath, guid + ".note");
            if (File.Exists(rootNotePath))
            {
                try { File.Delete(rootNotePath); } catch { }
            }

            // Delete from any revision directories
            try
            {
                var matches = Directory.GetFiles(serverPath, guid + ".note", SearchOption.AllDirectories);
                foreach (var match in matches)
                {
                    try { File.Delete(match); } catch { }
                }
            }
            catch { }
        }

        public void Commit(int newRev, string serverId)
        {
            LatestRevision = newRev;
            ServerId = serverId;

            string tmpPath = manifestPath + ".tmp";
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = System.Text.Encoding.UTF8
            };

            using (var writer = XmlWriter.Create(tmpPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("sync");
                writer.WriteAttributeString("revision", newRev.ToString());
                writer.WriteAttributeString("server-id", serverId);

                foreach (var kvp in ServerNotes.OrderBy(k => k.Key))
                {
                    writer.WriteStartElement("note");
                    writer.WriteAttributeString("id", kvp.Key);
                    writer.WriteAttributeString("rev", kvp.Value.ToString());
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // sync
                writer.WriteEndDocument();
            }

            if (File.Exists(manifestPath))
            {
                string oldPath = manifestPath + ".old";
                if (File.Exists(oldPath))
                {
                    try { File.Delete(oldPath); } catch { }
                }
                try { File.Move(manifestPath, oldPath); } catch { }
            }

            File.Move(tmpPath, manifestPath);
        }
    }
}
