using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Tomboy.Services
{
    public class PreferencesConfig
    {
        // 正在編輯 (Editing)
        public bool EnableSpellchecking { get; set; } = true;
        public bool EnableWikiwords { get; set; } = false;
        public bool EnableAutoBulletedLists { get; set; } = true;
        public bool EnableCustomFont { get; set; } = false;
        public string CustomFontFace { get; set; } = "Sans 11";
        public string NoteTitleColor { get; set; } = "#005A9E";
        public bool EnableCustomSearchMatchColor { get; set; } = false;
        public string CustomSearchMatchColor { get; set; } = "#ffff00";
        public int NoteRenameBehavior { get; set; } = 0; // 0: 詢問我要怎麼做, 1: 永不重新命名連結, 2: 永遠重新命名連結
        public string CustomTimestampFormat { get; set; } = "dddd, MMMM d, yyyy h:mm tt";

        // 快速鍵 (Hotkeys)
        public bool EnableKeybindings { get; set; } = true;
        public string KeybindingShowNoteMenu { get; set; } = "<Alt>F12";
        public string KeybindingOpenStartHere { get; set; } = "<Alt>Home";
        public string KeybindingCreateNewNote { get; set; } = "<Alt>N";
        public string KeybindingOpenRecentChanges { get; set; } = "<Alt>F11";

        // 同步化 (Synchronization)
        public string SyncSelectedServiceAddin { get; set; } = string.Empty; // "sshfs" or "local"
        public string SyncSshfsServer { get; set; } = string.Empty;
        public int SyncSshfsPort { get; set; } = -1;
        public string SyncSshfsFolder { get; set; } = string.Empty;
        public string SyncSshfsUsername { get; set; } = string.Empty;
        public string SyncLocalPath { get; set; } = string.Empty;
        public int SyncAutosyncTimeout { get; set; } = -1; // in minutes, <5 = disabled
        public int SyncConfiguredConflictBehavior { get; set; } = 0; // 0: 詢問, 1: 重新命名本地端, 2: 取代本地端

        // 附加元件 (Add-ins)
        public Dictionary<string, bool> AddinsEnabled { get; set; } = new();

        // 匯出成 HTML (Export to HTML)
        public string ExportHtmlLastDirectory { get; set; } = string.Empty;
        public bool ExportHtmlExportLinked { get; set; } = false;
        public bool ExportHtmlExportLinkedAll { get; set; } = false;

        // LaTeX 數學公式 (LaTeX Math Formulas)
        public bool EnableLatexMath { get; set; } = true;
        public string LatexHeader { get; set; } = string.Empty;
        public string LatexFooter { get; set; } = string.Empty;
        public bool LatexDollarEnabled { get; set; } = false;

        // 進階 (Advanced)
        public int MenuMinNoteCount { get; set; } = 10;
        public int MenuMaxNoteCount { get; set; } = 18;
        public bool EnableStartupNotes { get; set; } = true;
    }

    public static class Preferences
    {
        private static readonly object syncLock = new object();
        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "tomboy",
            "preferences.json"
        );

        private static PreferencesConfig current = new PreferencesConfig();

        public static event Action? SettingChanged;

        public static PreferencesConfig Current
        {
            get
            {
                lock (syncLock)
                {
                    return current;
                }
            }
            set
            {
                lock (syncLock)
                {
                    current = value;
                }
                Save();
                SettingChanged?.Invoke();
            }
        }

        static Preferences()
        {
            Load();
        }

        public static void Load()
        {
            lock (syncLock)
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        string json = File.ReadAllText(ConfigPath);
                        var cfg = JsonSerializer.Deserialize<PreferencesConfig>(json);
                        if (cfg != null) current = cfg;
                    }
                }
                catch { }
            }
        }

        public static void Save()
        {
            lock (syncLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(ConfigPath)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string json = JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigPath, json);
                }
                catch { }
            }
            SettingChanged?.Invoke();
        }
    }
}
