using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Tomboy.Models;
using Tomboy.Views;

namespace Tomboy.Services
{
    public class SyncResult
    {
        public bool Success { get; set; } = false;
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int DeletedLocally { get; set; }
        public int DeletedOnServer { get; set; }
        public int ConflictsResolved { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SyncService
    {
        private static uint autosyncTimeoutId = 0;
        public static event Action? SyncCompleted;

        public static void InitializeAutosync()
        {
            UpdateAutosyncTimer();
            Preferences.SettingChanged += UpdateAutosyncTimer;
        }

        public static void UpdateAutosyncTimer()
        {
            if (autosyncTimeoutId != 0)
            {
                GLib.Source.Remove(autosyncTimeoutId);
                autosyncTimeoutId = 0;
            }

            var cfg = Preferences.Current;
            if (cfg.SyncAutosyncTimeout >= 5 && !string.IsNullOrWhiteSpace(cfg.SyncSelectedServiceAddin))
            {
                uint intervalMs = (uint)(cfg.SyncAutosyncTimeout * 60 * 1000);
                autosyncTimeoutId = GLib.Timeout.Add(intervalMs, () =>
                {
                    try
                    {
                        SynchronizeConfigured();
                    }
                    catch { }
                    return true; // repeat
                });
            }
        }

        public static SyncResult SynchronizeConfigured()
        {
            var cfg = Preferences.Current;

            if (cfg.SyncSelectedServiceAddin == "sshfs")
            {
                if (string.IsNullOrWhiteSpace(cfg.SyncSshfsServer) || string.IsNullOrWhiteSpace(cfg.SyncSshfsUsername))
                {
                    return new SyncResult { Success = false, Message = "SSH 伺服器或使用者名稱尚未設定。" };
                }

                if (!SshSyncService.Mount(cfg.SyncSshfsServer, cfg.SyncSshfsPort, cfg.SyncSshfsUsername, cfg.SyncSshfsFolder, out string errorMsg))
                {
                    return new SyncResult { Success = false, Message = $"SSH 連線/掛載失敗：{errorMsg}" };
                }

                try
                {
                    var result = Synchronize(SshSyncService.MountPath, cfg.SyncConfiguredConflictBehavior);
                    return result;
                }
                finally
                {
                    SshSyncService.Unmount();
                }
            }
            else if (cfg.SyncSelectedServiceAddin == "local")
            {
                if (string.IsNullOrWhiteSpace(cfg.SyncLocalPath))
                {
                    return new SyncResult { Success = false, Message = "未指定本地端同步資料夾路徑。" };
                }
                return Synchronize(cfg.SyncLocalPath, cfg.SyncConfiguredConflictBehavior);
            }
            else
            {
                return new SyncResult { Success = false, Message = "尚未設定或儲存同步化服務。" };
            }
        }

        public static SyncResult Synchronize(string targetPath, int conflictBehavior = 0)
        {
            var result = new SyncResult();
            string localDir = NoteStorage.NoteDirectory;

            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            {
                result.Success = false;
                result.Message = "目標同步目錄不存在。";
                return result;
            }

            try
            {
                if (!Directory.Exists(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                var server = new FileSystemSyncServer(targetPath);
                var client = TomboySyncClient.Instance;

                // 1. Verify / associate Server ID
                if (string.IsNullOrEmpty(client.AssociatedServerId))
                {
                    client.AssociatedServerId = server.ServerId;
                }
                else if (client.AssociatedServerId != server.ServerId)
                {
                    Console.WriteLine($"[SyncService] Server ID changed from {client.AssociatedServerId} to {server.ServerId}. Re-associating.");
                    client.AssociatedServerId = server.ServerId;
                    client.LastSynchronizedRevision = -1;
                }

                int initialLastSyncRev = client.LastSynchronizedRevision;
                var downloadedGuids = new HashSet<string>();

                var localNotes = NoteStorage.LoadAllNotes().ToDictionary(n => n.Guid, n => n);
                var serverNotes = server.ServerNotes;
                var deletedNotes = client.DeletedNotes;

                int uploaded = 0;
                int downloaded = 0;
                int deletedLocally = 0;
                int deletedOnServer = 0;
                int conflictsResolved = 0;
                bool serverModified = false;
                var notesToUpdateClientRev = new List<string>();

                // 2. Step A: Download New & Updated Notes from Server
                foreach (var kvp in serverNotes.ToList())
                {
                    string guid = kvp.Key;
                    int rev = kvp.Value;

                    if (!localNotes.ContainsKey(guid))
                    {
                        // Note not present locally. Was it intentionally deleted locally?
                        if (deletedNotes.ContainsKey(guid))
                        {
                            // Note was deleted locally; do not re-download. It will be deleted from server in Step D.
                            continue;
                        }

                        // New note from server!
                        string? xml = server.GetNoteXml(guid, rev);
                        if (!string.IsNullOrEmpty(xml))
                        {
                            string localFilePath = Path.Combine(localDir, guid + ".note");
                            File.WriteAllText(localFilePath, xml);
                            var note = NoteItem.LoadFromFile(localFilePath);
                            localNotes[guid] = note;
                            client.SetRevision(guid, rev);
                            downloadedGuids.Add(guid);
                            downloaded++;
                        }
                    }
                    else
                    {
                        // Note exists locally and on server
                        var localNote = localNotes[guid];
                        int clientRev = client.GetRevision(guid);

                        if (rev > clientRev)
                        {
                            // Server has a newer revision!
                            string? xml = server.GetNoteXml(guid, rev);
                            if (!string.IsNullOrEmpty(xml))
                            {
                                bool localModified = localNote.LastMetadataChangeDate > client.LastSyncDate || localNote.LastChangeDate > client.LastSyncDate;
                                bool basicallyEqual = BasicallyEqualTo(localNote, xml);

                                if (!localModified || basicallyEqual)
                                {
                                    // Local was unmodified, or content is basically equal: simply update from server
                                    File.WriteAllText(localNote.FilePath, xml);
                                    var updatedNote = NoteItem.LoadFromFile(localNote.FilePath);
                                    localNotes[guid] = updatedNote;
                                    client.SetRevision(guid, rev);
                                    downloadedGuids.Add(guid);
                                    downloaded++;
                                    GLib.Idle.Add(() =>
                                    {
                                        MainWindow.ReloadOpenNoteWindow(updatedNote);
                                        return false;
                                    });
                                }
                                else
                                {
                                    // Both local and server modified with real content difference -> Conflict!
                                    if (conflictBehavior == 1 || conflictBehavior == 0)
                                    {
                                        // Backup local version with conflict title
                                        string backupTitle = $"{localNote.Title} (衝突備份 {DateTime.Now:yyyy-MM-dd HHmmss})";
                                        var backupNote = new NoteItem
                                        {
                                            Guid = Guid.NewGuid().ToString(),
                                            Title = backupTitle,
                                            XmlContent = localNote.XmlContent.Replace($"<note-title>{localNote.Title}</note-title>", $"<note-title>{backupTitle}</note-title>"),
                                            Notebook = localNote.Notebook,
                                            Tags = new List<string>(localNote.Tags)
                                        };
                                        backupNote.Save(localDir);
                                        localNotes[backupNote.Guid] = backupNote;
                                        conflictsResolved++;
                                    }

                                    // Apply server version
                                    File.WriteAllText(localNote.FilePath, xml);
                                    var updatedNote = NoteItem.LoadFromFile(localNote.FilePath);
                                    localNotes[guid] = updatedNote;
                                    client.SetRevision(guid, rev);
                                    downloadedGuids.Add(guid);
                                    downloaded++;
                                    GLib.Idle.Add(() =>
                                    {
                                        MainWindow.ReloadOpenNoteWindow(updatedNote);
                                        return false;
                                    });
                                }
                            }
                        }
                    }
                }

                // 3. Step B: Propagate Server Deletions to Local
                // If a note was previously synced (clientRev != -1), but is no longer in serverNotes, it was deleted on the server!
                foreach (var localNote in localNotes.Values.ToList())
                {
                    int clientRev = client.GetRevision(localNote.Guid);
                    if (clientRev != -1 && !serverNotes.ContainsKey(localNote.Guid))
                    {
                        if (File.Exists(localNote.FilePath))
                        {
                            try { File.Delete(localNote.FilePath); } catch { }
                        }
                        client.RemoveRevision(localNote.Guid);
                        localNotes.Remove(localNote.Guid);
                        deletedLocally++;
                        GLib.Idle.Add(() =>
                        {
                            MainWindow.CloseOpenNoteWindow(localNote.Guid);
                            return false;
                        });
                    }
                }

                // 4. Step C: Upload New & Modified Notes to Server
                int newRev = server.LatestRevision + 1;
                foreach (var localNote in localNotes.Values)
                {
                    // Skip notes that were downloaded or updated from server in this session
                    if (downloadedGuids.Contains(localNote.Guid))
                        continue;

                    int clientRev = client.GetRevision(localNote.Guid);
                    bool isNew = clientRev == -1;
                    bool isModified = (initialLastSyncRev < 0 || clientRev <= initialLastSyncRev) &&
                                      (localNote.LastMetadataChangeDate > client.LastSyncDate || localNote.LastChangeDate > client.LastSyncDate);

                    if (isNew || isModified)
                    {
                        server.UploadNote(localNote, newRev);
                        notesToUpdateClientRev.Add(localNote.Guid);
                        uploaded++;
                        serverModified = true;
                    }
                }

                // 5. Step D: Propagate Local Deletions to Server
                foreach (var deletedGuid in deletedNotes.Keys.ToList())
                {
                    if (serverNotes.ContainsKey(deletedGuid))
                    {
                        server.DeleteNote(deletedGuid);
                        deletedOnServer++;
                        serverModified = true;
                    }
                }

                // 6. Step E: Commit Revisions & Update Manifests
                if (serverModified)
                {
                    server.Commit(newRev, server.ServerId);
                }
                else
                {
                    newRev = server.LatestRevision;
                }

                foreach (var guid in notesToUpdateClientRev)
                {
                    client.SetRevision(guid, newRev);
                }

                client.CommitSync(newRev, DateTime.Now);

                // 7. Step F: Notify UI & Formulate Message
                GLib.Idle.Add(() =>
                {
                    try { SyncCompleted?.Invoke(); } catch { }
                    return false;
                });

                result.Success = true;
                result.Uploaded = uploaded;
                result.Downloaded = downloaded;
                result.DeletedLocally = deletedLocally;
                result.DeletedOnServer = deletedOnServer;
                result.ConflictsResolved = conflictsResolved;

                var details = new List<string>();
                if (uploaded > 0) details.Add($"上傳: {uploaded}");
                if (downloaded > 0) details.Add($"下載: {downloaded}");
                if (deletedOnServer > 0) details.Add($"遠端刪除: {deletedOnServer}");
                if (deletedLocally > 0) details.Add($"本地刪除: {deletedLocally}");
                if (conflictsResolved > 0) details.Add($"解決衝突: {conflictsResolved}");

                string detailMsg = details.Count > 0 ? string.Join("，", details) : "筆記已是最新狀態，無需變更";
                result.Message = $"同步完成 ({detailMsg})。";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"同步時發生錯誤: {ex.Message}";
                Console.WriteLine($"[SyncService] Exception: {ex}");
            }

            return result;
        }

        public static bool BasicallyEqualTo(NoteItem localNote, string serverXml)
        {
            if (string.IsNullOrWhiteSpace(serverXml))
                return false;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(serverXml);

                string serverTitle = doc.SelectSingleNode("//*[local-name()='title']")?.InnerText?.Trim() ?? string.Empty;
                if (localNote.Title.Trim() != serverTitle)
                    return false;

                var serverTags = new HashSet<string>();
                var tagNodes = doc.SelectNodes("//*[local-name()='tags']/*[local-name()='tag']");
                if (tagNodes != null)
                {
                    foreach (XmlNode tagNode in tagNodes)
                    {
                        if (!string.IsNullOrWhiteSpace(tagNode.InnerText))
                            serverTags.Add(tagNode.InnerText.Trim());
                    }
                }

                var localTags = new HashSet<string>(localNote.Tags.Select(t => t.Trim()));
                if (!serverTags.SetEquals(localTags))
                    return false;

                string localInner = GetInnerContent(localNote.XmlContent);
                string serverInner = GetInnerContent(serverXml);

                return localInner == serverInner;
            }
            catch
            {
                return false;
            }
        }

        private static string GetInnerContent(string xmlContent)
        {
            if (string.IsNullOrEmpty(xmlContent))
                return string.Empty;

            const string pattern = @"<note-content[^>]*>(?<inner>.*)</note-content>";
            var match = Regex.Match(
                xmlContent,
                pattern,
                RegexOptions.Singleline
            );

            if (!match.Success)
                return string.Empty;

            return match.Groups["inner"].Value.Replace("\r\n", "\n").Trim();
        }
    }
}
