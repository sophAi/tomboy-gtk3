using System;
using System.Runtime.InteropServices;
using GLib;

namespace Tomboy.Services
{
    public static class GlobalHotkeyService
    {
        private const string LibKeybinder = "libkeybinder-3.0.so.0";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void KeybinderHandler(IntPtr keystring, IntPtr userData);

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern void keybinder_init();

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool keybinder_bind(string keystring, KeybinderHandler handler, IntPtr userData);

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern void keybinder_unbind(string keystring);

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern void keybinder_unbind_all();

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool keybinder_supported();

        private static bool isInitialized = false;
        private static bool isSupported = false;

        // Keep strong references to delegates to prevent GC collection
        private static KeybinderHandler? showMenuHandler;
        private static KeybinderHandler? startHereHandler;
        private static KeybinderHandler? newNoteHandler;
        private static KeybinderHandler? searchHandler;

        private static System.Action? onShowMenu;
        private static System.Action? onStartHere;
        private static System.Action? onNewNote;
        private static System.Action? onSearch;

        public static void Initialize(System.Action showMenu, System.Action startHere, System.Action newNote, System.Action search)
        {
            onShowMenu = showMenu;
            onStartHere = startHere;
            onNewNote = newNote;
            onSearch = search;

            showMenuHandler = (k, u) => GLib.Idle.Add(() => { onShowMenu?.Invoke(); return false; });
            startHereHandler = (k, u) => GLib.Idle.Add(() => { onStartHere?.Invoke(); return false; });
            newNoteHandler = (k, u) => GLib.Idle.Add(() => { onNewNote?.Invoke(); return false; });
            searchHandler = (k, u) => GLib.Idle.Add(() => { onSearch?.Invoke(); return false; });

            try
            {
                keybinder_init();
                isSupported = keybinder_supported();
                isInitialized = true;
                Console.WriteLine($"[GlobalHotkeyService] libkeybinder-3.0 initialized. Supported: {isSupported}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalHotkeyService] libkeybinder-3.0 unavailable or failed: {ex.Message}");
                isInitialized = false;
                isSupported = false;
                return;
            }

            RebindAll();

            Preferences.SettingChanged += OnSettingChanged;
        }

        private static void OnSettingChanged()
        {
            RebindAll();
        }

        public static void RebindAll()
        {
            if (!isInitialized || !isSupported) return;

            try
            {
                keybinder_unbind_all();

                var cfg = Preferences.Current;
                if (!cfg.EnableKeybindings)
                {
                    Console.WriteLine("[GlobalHotkeyService] Hotkeys are disabled in Preferences.");
                    return;
                }

                BindKey(cfg.KeybindingShowNoteMenu, showMenuHandler!, "ShowNoteMenu");
                BindKey(cfg.KeybindingOpenStartHere, startHereHandler!, "OpenStartHere");
                BindKey(cfg.KeybindingCreateNewNote, newNoteHandler!, "CreateNewNote");
                BindKey(cfg.KeybindingOpenRecentChanges, searchHandler!, "OpenRecentChanges");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalHotkeyService] Rebind error: {ex.Message}");
            }
        }

        private static void BindKey(string? keystring, KeybinderHandler handler, string name)
        {
            if (string.IsNullOrWhiteSpace(keystring) || keystring.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                bool success = keybinder_bind(keystring.Trim(), handler, IntPtr.Zero);
                if (success)
                {
                    Console.WriteLine($"[GlobalHotkeyService] Bound hotkey '{keystring}' for {name}");
                }
                else
                {
                    Console.WriteLine($"[GlobalHotkeyService] Failed to bind hotkey '{keystring}' for {name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalHotkeyService] Error binding '{keystring}': {ex.Message}");
            }
        }
    }
}
