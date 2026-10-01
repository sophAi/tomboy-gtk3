using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Xsl;
using Gtk;
using Tomboy.Models;
using Tomboy.Views;

namespace Tomboy.Services
{
    public class HtmlExportService
    {
        private const string STYLESHEET_NAME = "ExportToHtml.xsl";
        private static XslCompiledTransform? xsl;
        private static readonly object xslLock = new object();

        private static XslCompiledTransform NoteXsl
        {
            get
            {
                lock (xslLock)
                {
                    if (xsl == null)
                    {
                        xsl = new XslCompiledTransform();
                        var settings = new XsltSettings(true, true);
                        var resolver = new XmlUrlResolver();

                        // 1. Check user-customized XSL in ~/.config/tomboy/
                        string userConfigXsl = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".config", "tomboy", STYLESHEET_NAME);

                        // 2. Check application directory
                        string appDirXsl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, STYLESHEET_NAME);

                        if (File.Exists(userConfigXsl))
                        {
                            using var reader = XmlReader.Create(userConfigXsl);
                            xsl.Load(reader, settings, resolver);
                        }
                        else if (File.Exists(appDirXsl))
                        {
                            using var reader = XmlReader.Create(appDirXsl);
                            xsl.Load(reader, settings, resolver);
                        }
                        else
                        {
                            // 3. Embedded resource
                            Assembly asm = Assembly.GetExecutingAssembly();
                            using Stream? resStream = asm.GetManifestResourceStream("Tomboy.Resources.ExportToHtml.xsl");
                            if (resStream != null)
                            {
                                using var reader = XmlReader.Create(resStream);
                                xsl.Load(reader, settings, resolver);
                            }
                            else
                            {
                                throw new FileNotFoundException("Could not find embedded HTML export template ExportToHtml.xsl");
                            }
                        }
                    }
                    return xsl;
                }
            }
        }

        public static string SanitizeNoteTitle(string noteTitle)
        {
            if (string.IsNullOrWhiteSpace(noteTitle))
                return "Untitled";

            string sanitized = noteTitle.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(c, '_');
            }
            sanitized = sanitized.Replace('/', '_')
                                 .Replace('\\', '_')
                                 .Replace(':', '_')
                                 .Replace('?', '_');

            return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
        }

        public static void PromptExportNote(NoteItem note, Window parent)
        {
            if (note == null) return;

            // Ensure note file on disk is up to date
            note.Save(NoteStorage.NoteDirectory);

            string defaultName = $"{SanitizeNoteTitle(note.Title)}.html";
            var dlg = new ExportToHtmlDialog(parent, defaultName);

            int response = dlg.Run();
            string targetPath = dlg.SelectedExportPath;
            bool exportLinked = dlg.ExportLinked;
            bool exportLinkedAll = dlg.ExportLinkedAll;

            if (response == (int)ResponseType.Ok && !string.IsNullOrWhiteSpace(targetPath))
            {
                dlg.SavePreferences();
                dlg.Hide();
                dlg.Dispose();

                try
                {
                    ExportNoteToHtml(note, targetPath, exportLinked, exportLinkedAll, parent, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error exporting note to HTML: {ex}");
                    string safeMsg = GLib.Markup.EscapeText(ex.Message);
                    var err = new MessageDialog(parent, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok,
                        $"無法儲存檔案：\n{safeMsg}");
                    err.Run();
                    err.Hide();
                    err.Dispose();
                }
            }
            else
            {
                dlg.Hide();
                dlg.Dispose();
            }
        }

        public static void ExportNoteToHtml(
            NoteItem note,
            string outputPath,
            bool exportLinked,
            bool exportLinkedAll,
            Window? parent = null,
            bool openBrowser = true)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));

            // Ensure the note exists on disk
            if (!File.Exists(note.FilePath))
            {
                note.Save(NoteStorage.NoteDirectory);
            }

            var allNotes = NoteStorage.LoadAllNotes();
            var ext = new TransformExtension(note.Title, title => FindNotePath(allNotes, title));

            var args = new XsltArgumentList();
            args.AddParam("export-linked", "", exportLinked);
            args.AddParam("export-linked-all", "", exportLinkedAll);
            args.AddParam("root-note", "", note.Title);
            args.AddParam("exporting-multiple", "", false);

            if (Preferences.Current.EnableCustomFont && !string.IsNullOrWhiteSpace(Preferences.Current.CustomFontFace))
            {
                args.AddParam("font", "", $"font-family:'{Preferences.Current.CustomFontFace}';");
            }
            else
            {
                args.AddParam("font", "", "font-family:sans-serif;");
            }

            args.AddExtensionObject("http://beatniksoftware.com/tomboy", ext);

            // Ensure output directory exists
            string? outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            using (var xmlReader = XmlReader.Create(note.FilePath))
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
            using (var xmlWriter = XmlWriter.Create(sw, NoteXsl.OutputSettings))
            {
                NoteXsl.Transform(xmlReader, args, xmlWriter, new XmlUrlResolver());
            }

            if (openBrowser)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not open exported note in browser: {ex.Message}");
                    if (parent != null && parent.Handle != IntPtr.Zero)
                    {
                        string safePath = GLib.Markup.EscapeText(outputPath);
                        var msgDialog = new MessageDialog(parent, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                            $"筆記已成功匯出至：\n{safePath}");
                        msgDialog.Run();
                        msgDialog.Hide();
                        msgDialog.Dispose();
                    }
                }
            }
        }

        public static void PromptExportAllNotes(List<NoteItem> notes, Window parent, string? titlePrefix = null)
        {
            if (notes == null || notes.Count == 0)
            {
                var emptyDlg = new MessageDialog(parent, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, "目前沒有可以匯出的筆記。");
                emptyDlg.Run();
                emptyDlg.Hide();
                emptyDlg.Dispose();
                return;
            }

            string dialogTitle = string.IsNullOrEmpty(titlePrefix)
                ? "選擇匯出所有筆記為 HTML 的資料夾"
                : $"選擇匯出「{titlePrefix}」筆記為 HTML 的資料夾";

            var folderDlg = new FileChooserDialog(dialogTitle, parent, FileChooserAction.SelectFolder);
            folderDlg.AddButton("取消", ResponseType.Cancel);
            folderDlg.AddButton("匯出", ResponseType.Ok);
            folderDlg.DefaultResponse = ResponseType.Ok;
            folderDlg.LocalOnly = true;

            string lastDir = Preferences.Current.ExportHtmlLastDirectory;
            if (string.IsNullOrEmpty(lastDir) || !Directory.Exists(lastDir))
            {
                lastDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            folderDlg.SetCurrentFolder(lastDir);

            int resp = folderDlg.Run();
            string selectedFolder = folderDlg.Filename;
            folderDlg.Hide();
            folderDlg.Dispose();

            if (resp == (int)ResponseType.Ok && !string.IsNullOrWhiteSpace(selectedFolder))
            {
                try
                {
                    Preferences.Current.ExportHtmlLastDirectory = selectedFolder;
                    Preferences.Save();

                    Directory.CreateDirectory(selectedFolder);

                    var allNotes = NoteStorage.LoadAllNotes();
                    int count = 0;

                    foreach (var note in notes)
                    {
                        string noteFile = Path.Combine(selectedFolder, $"{SanitizeNoteTitle(note.Title)}.html");
                        ExportSingleNoteForBatch(note, noteFile, allNotes);
                        count++;
                    }

                    string safeFolder = GLib.Markup.EscapeText(selectedFolder);
                    var successDlg = new MessageDialog(parent, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                        $"已成功將 {count} 篇筆記匯出至：\n{safeFolder}");
                    successDlg.Run();
                    successDlg.Hide();
                    successDlg.Dispose();

                    try
                    {
                        Process.Start(new ProcessStartInfo(selectedFolder) { UseShellExecute = true });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error exporting notes to folder: {ex}");
                    string safeErr = GLib.Markup.EscapeText(ex.Message);
                    var errDlg = new MessageDialog(parent, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok,
                        $"匯出筆記時發生錯誤：\n{safeErr}");
                    errDlg.Run();
                    errDlg.Hide();
                    errDlg.Dispose();
                }
            }
        }

        private static void ExportSingleNoteForBatch(NoteItem note, string outputPath, List<NoteItem> allNotes)
        {
            if (!File.Exists(note.FilePath))
            {
                note.Save(NoteStorage.NoteDirectory);
            }

            var ext = new TransformExtension(note.Title, title => FindNotePath(allNotes, title));

            var args = new XsltArgumentList();
            args.AddParam("export-linked", "", false);
            args.AddParam("export-linked-all", "", false);
            args.AddParam("root-note", "", note.Title);
            args.AddParam("exporting-multiple", "", true);

            if (Preferences.Current.EnableCustomFont && !string.IsNullOrWhiteSpace(Preferences.Current.CustomFontFace))
            {
                args.AddParam("font", "", $"font-family:'{Preferences.Current.CustomFontFace}';");
            }
            else
            {
                args.AddParam("font", "", "font-family:sans-serif;");
            }

            args.AddExtensionObject("http://beatniksoftware.com/tomboy", ext);

            using var xmlReader = XmlReader.Create(note.FilePath);
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
            using var xmlWriter = XmlWriter.Create(sw, NoteXsl.OutputSettings);

            NoteXsl.Transform(xmlReader, args, xmlWriter, new XmlUrlResolver());
        }

        private static string? FindNotePath(List<NoteItem> notes, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            var match = notes.FirstOrDefault(n => string.Equals(n.Title.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null && File.Exists(match.FilePath))
            {
                return match.FilePath;
            }
            return null;
        }

        public class TransformExtension
        {
            private readonly Func<string, string?> pathResolver;
            private readonly List<string> resolvedNotes = new();

            public TransformExtension(string rootTitle, Func<string, string?> pathResolver)
            {
                this.pathResolver = pathResolver;
                if (!string.IsNullOrEmpty(rootTitle))
                {
                    resolvedNotes.Add(rootTitle.Trim().ToLowerInvariant());
                }
            }

            public string ToLower(string s) => s?.ToLowerInvariant() ?? string.Empty;

            public string GetPath(string title)
            {
                if (string.IsNullOrWhiteSpace(title)) return string.Empty;
                string lower = title.Trim().ToLowerInvariant();
                if (resolvedNotes.Contains(lower)) return string.Empty;

                resolvedNotes.Add(lower);
                string? path = pathResolver(title);
                return path ?? string.Empty;
            }

            public string GetRelativePath(string title)
            {
                if (string.IsNullOrWhiteSpace(title)) return string.Empty;
                return SanitizeNoteTitle(title) + ".html";
            }
        }
    }
}
