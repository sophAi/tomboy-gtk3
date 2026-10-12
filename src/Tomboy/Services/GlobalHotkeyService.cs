using System;
using System.Collections.Generic;
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
        private static extern void keybinder_unbind(string keystring, KeybinderHandler handler);

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern void keybinder_unbind_all(string keystring);

        [DllImport(LibKeybinder, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool keybinder_supported();

        private static bool isInitialized = false;
        private static bool isSupported = false;

        // Track currently bound keystrings to ensure clean unbinding
        private static readonly HashSet<string> currentlyBoundKeys = new();

        // Prevent repeated triggers (key repeat / rapid keypress debounce)
        private static readonly Dictionary<string, System.DateTime> lastTriggerTimes = new();
        private static readonly object triggerLock = new();

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

            showMenuHandler = (k, u) =>
            {
                if (!CanTrigger("ShowMenu", 500)) return;
                GLib.Idle.Add(() => { onShowMenu?.Invoke(); return false; });
            };

            startHereHandler = (k, u) =>
            {
                if (!CanTrigger("StartHere", 500)) return;
                GLib.Idle.Add(() => { onStartHere?.Invoke(); return false; });
            };

            newNoteHandler = (k, u) =>
            {
                // Enforce 1000ms cooldown to prevent X11 key-repeat from spawning multiple new notes
                if (!CanTrigger("NewNote", 1000)) return;
                GLib.Idle.Add(() => { onNewNote?.Invoke(); return false; });
            };

            searchHandler = (k, u) =>
            {
                if (!CanTrigger("Search", 500)) return;
                GLib.Idle.Add(() => { onSearch?.Invoke(); return false; });
            };

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

        private static bool CanTrigger(string actionName, int cooldownMs)
        {
            lock (triggerLock)
            {
                var now = System.DateTime.UtcNow;
                if (lastTriggerTimes.TryGetValue(actionName, out var lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < cooldownMs)
                    {
                        return false;
                    }
                }
                lastTriggerTimes[actionName] = now;
                return true;
            }
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
                // Unbind all previously registered keys to prevent duplicate handlers
                foreach (var key in currentlyBoundKeys)
                {
                    try
                    {
                        keybinder_unbind_all(key);
                    }
                    catch { }
                }
                currentlyBoundKeys.Clear();

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

            string key = keystring.Trim();
            try
            {
                // Always unbind before binding to guarantee no duplicate registrations
                keybinder_unbind_all(key);

                bool success = keybinder_bind(key, handler, IntPtr.Zero);
                if (success)
                {
                    currentlyBoundKeys.Add(key);
                    Console.WriteLine($"[GlobalHotkeyService] Bound hotkey '{key}' for {name}");
                }
                else
                {
                    Console.WriteLine($"[GlobalHotkeyService] Failed to bind hotkey '{key}' for {name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GlobalHotkeyService] Error binding '{key}': {ex.Message}");
            }
        }
    }
}
