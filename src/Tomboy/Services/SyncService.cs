using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomboy.Models;

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
                            bool localModified = localNote.LastChangeDate > client.LastSyncDate;

                            if (!localModified)
                            {
                                // Local was unmodified, simply update from server
                                string? xml = server.GetNoteXml(guid, rev);
                                if (!string.IsNullOrEmpty(xml))
                                {
                                    File.WriteAllText(localNote.FilePath, xml);
                                    localNotes[guid] = NoteItem.LoadFromFile(localNote.FilePath);
                                    client.SetRevision(guid, rev);
                                    downloaded++;
                                }
                            }
                            else
                            {
                                // Both local and server modified -> Conflict!
                                if (conflictBehavior == 1 || conflictBehavior == 0)
                                {
                                    // Backup local version with conflict title
                                    var backupNote = new NoteItem
                                    {
                                        Guid = Guid.NewGuid().ToString(),
                                        Title = $"{localNote.Title} (衝突備份 {DateTime.Now:yyyy-MM-dd HHmmss})",
                                        XmlContent = localNote.XmlContent.Replace($"<note-title>{localNote.Title}</note-title>", $"<note-title>{localNote.Title} (衝突備份 {DateTime.Now:yyyy-MM-dd HHmmss})</note-title>")
                                    };
                                    backupNote.Save(localDir);
                                    conflictsResolved++;
                                }

                                // Apply server version
                                string? xml = server.GetNoteXml(guid, rev);
                                if (!string.IsNullOrEmpty(xml))
                                {
                                    File.WriteAllText(localNote.FilePath, xml);
                                    localNotes[guid] = NoteItem.LoadFromFile(localNote.FilePath);
                                    client.SetRevision(guid, rev);
                                    downloaded++;
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
                    }
                }

                // 4. Step C: Upload New & Modified Notes to Server
                int newRev = server.LatestRevision + 1;
                foreach (var localNote in localNotes.Values)
                {
                    int clientRev = client.GetRevision(localNote.Guid);
                    bool isNew = clientRev == -1;
                    bool isModified = localNote.LastChangeDate > client.LastSyncDate;

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
    }
}
