using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Tomboy.Services
{
    public static class SshSyncService
    {
        public static string MountPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "tomboy",
            "sync-sshfs"
        );

        public static bool IsSshfsInstalled
        {
            get
            {
                string[] knownPaths = { "/usr/bin/sshfs", "/usr/local/bin/sshfs", "/bin/sshfs" };
                foreach (var p in knownPaths)
                {
                    if (File.Exists(p)) return true;
                }
                return FindExecutableInPath("sshfs") != null;
            }
        }

        public static bool IsFusermountInstalled
        {
            get
            {
                string[] known = { "/usr/bin/fusermount3", "/usr/bin/fusermount", "/bin/fusermount", "/bin/fusermount3" };
                foreach (var p in known)
                {
                    if (File.Exists(p)) return true;
                }
                return FindExecutableInPath("fusermount3") != null || FindExecutableInPath("fusermount") != null;
            }
        }

        public static bool IsMounted
        {
            get
            {
                try
                {
                    if (File.Exists("/proc/mounts"))
                    {
                        string target = MountPath.TrimEnd('/');
                        foreach (var line in File.ReadAllLines("/proc/mounts"))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && parts[1].TrimEnd('/') == target)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch { }
                return false;
            }
        }

        public static bool Mount(string server, int port, string username, string folder, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "伺服器或使用者名稱欄位是空的。";
                return false;
            }

            if (!IsSshfsInstalled)
            {
                errorMessage = "此電腦不支援此同步化附加元件。請確定您已正確安裝及設定 FUSE 與 sshfs 套件。\n(可透過 apt install sshfs 安裝)";
                return false;
            }

            try
            {
                if (!Directory.Exists(MountPath))
                {
                    Directory.CreateDirectory(MountPath);
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"無法建立掛載目錄 \"{MountPath}\": {ex.Message}";
                return false;
            }

            // If already mounted, unmount first
            if (IsMounted)
            {
                Unmount();
            }

            string portStr = (port > 0 && port != 22) ? $"-p {port} " : string.Empty;
            string remoteTarget = string.IsNullOrWhiteSpace(folder)
                ? $"{username}@{server}:"
                : $"{username}@{server}:{folder}";

            string args = $"{portStr}{remoteTarget} \"{MountPath}\" -o reconnect,ServerAliveInterval=15,ServerAliveCountMax=3";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sshfs",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = new Process { StartInfo = psi };
                p.Start();

                bool exited = p.WaitForExit(10000); // 10 seconds timeout
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    Unmount();
                    errorMessage = "連線至伺服器逾時。請確定您的 SSH 金鑰已加入執行中的 SSH 伺服程式 (ssh-agent) 裡。";
                    return false;
                }

                if (p.ExitCode != 0)
                {
                    string stderr = p.StandardError.ReadToEnd().Trim();
                    Unmount();
                    errorMessage = "連線至指定伺服器時發生錯誤：\n\n" + (string.IsNullOrWhiteSpace(stderr) ? $"Exit code {p.ExitCode}" : stderr);
                    return false;
                }

                if (!IsMounted)
                {
                    Unmount();
                    errorMessage = "連線至伺服器失敗，未能成功掛載目錄。請檢查伺服器位址或認證。";
                    return false;
                }

                // Verify write/read/delete capabilities
                if (!TestMountDirectory(MountPath, out var testErr))
                {
                    Unmount();
                    errorMessage = testErr;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Unmount();
                errorMessage = $"執行 sshfs 掛載時發生例外：{ex.Message}";
                return false;
            }
        }

        public static void Unmount()
        {
            if (!IsMounted) return;

            string unmountCmd = FindExecutableInPath("fusermount3") ?? FindExecutableInPath("fusermount") ?? "umount";
            string args = unmountCmd.Contains("fusermount") ? $"-u \"{MountPath}\"" : $"\"{MountPath}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = unmountCmd,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
        }

        public static bool TestMountDirectory(string dirPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string testFileName = "tomboy_test_" + Guid.NewGuid().ToString("N") + ".tmp";
                string testFilePath = Path.Combine(dirPath, testFileName);
                string testContent = "Testing Tomboy SSH synchronization write/read capabilities.";

                // 1. Test Write
                File.WriteAllText(testFilePath, testContent);

                // 2. Test Read
                if (!File.Exists(testFilePath))
                {
                    errorMessage = "無法讀取測試檔案。";
                    return false;
                }

                string readContent = File.ReadAllText(testFilePath);
                if (readContent != testContent)
                {
                    try { File.Delete(testFilePath); } catch { }
                    errorMessage = "寫入測試失敗，讀取的內容與寫入的不符。";
                    return false;
                }

                // 3. Test Delete
                File.Delete(testFilePath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"遠端目錄測試失敗 (寫入/讀取權限問題)：{ex.Message}";
                return false;
            }
        }

        private static string? FindExecutableInPath(string exeName)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string fullPath = Path.Combine(dir, exeName);
                if (File.Exists(fullPath)) return fullPath;
            }
            return null;
        }
    }
}
