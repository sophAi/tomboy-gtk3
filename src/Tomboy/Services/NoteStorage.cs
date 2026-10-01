using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomboy.Models;

namespace Tomboy.Services
{
    public class NoteStorage
    {
        public static string NoteDirectory
        {
            get
            {
                string localShare = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localShare, "tomboy");
            }
        }

        public static List<NoteItem> LoadAllNotes()
        {
            var notes = new List<NoteItem>();
            string dir = NoteDirectory;

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                var startHere = CreateDefaultStartHereNote();
                startHere.Save(dir);
                notes.Add(startHere);
                return notes;
            }

            var files = Directory.GetFiles(dir, "*.note");
            foreach (var file in files)
            {
                var note = NoteItem.LoadFromFile(file);
                notes.Add(note);
            }

            return notes.OrderByDescending(n => n.LastChangeDate).ToList();
        }

        public static NoteItem CreateDefaultStartHereNote()
        {
            return new NoteItem
            {
                Title = "Start Here",
                XmlContent = "<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>Start Here</note-title>\n\nWelcome to Tomboy Notes (.NET 8 + GTK3)!\n\nUse this note to get started.\n<list><list-item dir=\"ltr\">Click Create New Note to write ideas.</list-item><list-item dir=\"ltr\">Organize notes with Notebooks.<list><list-item dir=\"ltr\">Supports nested indentation and formatting.</list-item></list></list-item></list></note-content>"
            };
        }

        private static readonly Dictionary<string, (DateTime lastWrite, string title)> titleCache = new();
        private static readonly object cacheLock = new();

        public static List<string> GetAllNoteTitles()
        {
            lock (cacheLock)
            {
                string dir = NoteDirectory;
                if (!Directory.Exists(dir)) return new List<string>();

                var files = Directory.GetFiles(dir, "*.note");
                var currentFiles = new HashSet<string>(files);
                var titles = new List<string>();

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (titleCache.TryGetValue(file, out var cached) && cached.lastWrite == fileInfo.LastWriteTimeUtc)
                        {
                            if (!string.IsNullOrWhiteSpace(cached.title))
                                titles.Add(cached.title);
                            continue;
                        }

                        string noteTitle = FastReadNoteTitle(file);
                        titleCache[file] = (fileInfo.LastWriteTimeUtc, noteTitle);
                        if (!string.IsNullOrWhiteSpace(noteTitle))
                            titles.Add(noteTitle);
                    }
                    catch { }
                }

                var keysToRemove = titleCache.Keys.Where(k => !currentFiles.Contains(k)).ToList();
                foreach (var k in keysToRemove)
                {
                    titleCache.Remove(k);
                }

                return titles;
            }
        }

        private static string FastReadNoteTitle(string filePath)
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                int startTag = line.IndexOf("<title>");
                if (startTag != -1)
                {
                    int endTag = line.IndexOf("</title>", startTag);
                    if (endTag != -1)
                    {
                        string title = line.Substring(startTag + 7, endTag - (startTag + 7));
                        return System.Net.WebUtility.HtmlDecode(title).Trim();
                    }
                }
                startTag = line.IndexOf("<note-title>");
                if (startTag != -1)
                {
                    int endTag = line.IndexOf("</note-title>", startTag);
                    if (endTag != -1)
                    {
                        string title = line.Substring(startTag + 12, endTag - (startTag + 12));
                        return System.Net.WebUtility.HtmlDecode(title).Trim();
                    }
                }
            }
            return string.Empty;
        }

        public static void DeleteNote(NoteItem note)
        {
            if (File.Exists(note.FilePath))
            {
                File.Delete(note.FilePath);
            }
            lock (cacheLock)
            {
                titleCache.Remove(note.FilePath);
            }
            TomboySyncClient.Instance.RecordNoteDeleted(note.Guid, note.Title);
        }
    }
}
