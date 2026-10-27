using System;
using System.Collections.Generic;
using System.Linq;
using Gdk;
using Gtk;
using Tomboy.Models;
using Tomboy.Views;

namespace Tomboy.Services
{
    public class TrayManager
    {
        private StatusIcon statusIcon;

        public System.Action? OnCreateNewNote { get; set; }
        public System.Action? OnOpenStartHere { get; set; }
        public System.Action? OnSearchNotes { get; set; }
        public System.Action? OnSyncNotes { get; set; }
        public System.Action? OnShowPreferences { get; set; }
        public System.Action? OnShowAbout { get; set; }
        public System.Action? OnQuitApp { get; set; }
        public System.Action? OnToggleMainWindow { get; set; }
        public System.Action<NoteItem>? OnOpenNote { get; set; }

        public TrayManager()
        {
            statusIcon = new StatusIcon();
            
            // Load Tomboy Icon
            Pixbuf? tomboyPb = IconService.GetIcon("tomboy", 24) ?? IconService.GetIcon("tomboy-48", 24);
            if (tomboyPb != null)
            {
                statusIcon.Pixbuf = tomboyPb;
            }
            else if (!SetStatusIconFromTheme(statusIcon, "tomboy", "accessories-text-editor", "text-editor", "document-open"))
            {
                Pixbuf pb = CreateFallbackTomboyPixbuf();
                statusIcon.Pixbuf = pb;
            }

            statusIcon.TooltipText = "Tomboy Notes (.NET 8 + GTK3)";
            statusIcon.Visible = true;

            // Left-click: Open the Note Menu with recent notes (matches Tomboy Tray.cs OnActivate)
            statusIcon.Activate += (s, e) => ShowNoteMenu();

            // Right-click: Open the Context Menu (matches Tomboy Tray.cs OnPopupMenu)
            statusIcon.PopupMenu += (s, e) => ShowContextMenu();
        }

        private bool SetStatusIconFromTheme(StatusIcon icon, params string[] iconNames)
        {
            IconTheme theme = IconTheme.Default;
            foreach (var name in iconNames)
            {
                if (theme.HasIcon(name))
                {
                    icon.IconName = name;
                    return true;
                }
            }
            return false;
        }

        private Pixbuf CreateFallbackTomboyPixbuf()
        {
            // Draw a vibrant 24x24 yellow Tomboy note icon dynamically
            Pixbuf pixbuf = new Pixbuf(Colorspace.Rgb, true, 8, 24, 24);
            pixbuf.Fill(0xFFFF88FF); // Tomboy Note Yellow
            return pixbuf;
        }

        public void ShowNoteMenu()
        {
            var menu = CreateNoteMenu();
            menu.Popup();
        }

        public void ShowContextMenu()
        {
            var menu = CreateContextMenu();
            menu.Popup();
        }

        private List<NoteItem> GetRecentNotes()
        {
            int minSize = Math.Max(1, Preferences.Current.MenuMinNoteCount);
            int maxSize = Math.Max(minSize, Preferences.Current.MenuMaxNoteCount);
            int listSize = 0;
            DateTime daysAgo = DateTime.Today.AddDays(-3);

            var allNotes = NoteStorage.LoadAllNotes();
            var result = new List<NoteItem>();

            foreach (var note in allNotes)
            {
                // Skip template notes
                if (note.Tags.Contains("system:template") ||
                    note.Title.EndsWith("Template", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip Start Here (has its own dedicated menu entry)
                if (string.Equals(note.Title, "Start Here", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isPinned = note.Tags.Contains("system:pinned");
                bool isOpened = MainWindow.openNoteWindows.ContainsKey(note.Guid);

                bool show = false;
                if (isPinned)
                {
                    show = true;
                }
                else if (isOpened || note.LastChangeDate > daysAgo || listSize < minSize)
                {
                    if (listSize < maxSize)
                    {
                        show = true;
                    }
                }

                if (show)
                {
                    result.Add(note);
                    listSize++;
                }
            }

            return result;
        }

        private MenuItem CreateNoteMenuItem(NoteItem note)
        {
            string displayName = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title;
            displayName = Ellipsify(displayName, 40);
            string labelText = FormatForLabel(displayName);

            var item = new MenuItem();
            var box = new Box(Orientation.Horizontal, 6);
            var iconPb = IconService.GetIcon("note", 16);
            if (iconPb != null)
            {
                box.PackStart(new Image(iconPb), false, false, 0);
            }
            else
            {
                box.PackStart(new Label("📄"), false, false, 0);
            }

            var label = new Label(labelText) { Xalign = 0 };
            box.PackStart(label, true, true, 0);

            item.Add(box);
            item.Activated += (s, e) => OnOpenNote?.Invoke(note);
            return item;
        }

        private static string FormatForLabel(string name)
        {
            // Replace underscores with double-underscores so GTK does not treat them as mnemonics
            return name.Replace("_", "__");
        }

        private static string Ellipsify(string str, int max)
        {
            if (str.Length > max)
                return str.Substring(0, max - 1) + "…";
            return str;
        }

        private Menu CreateNoteMenu()
        {
            var menu = new Menu();

            var newNoteItem = new MenuItem("📝 _Create New Note");
            newNoteItem.Activated += (s, e) => OnCreateNewNote?.Invoke();
            menu.Append(newNoteItem);

            var searchItem = new MenuItem("🔍 _Search All Notes...");
            searchItem.Activated += (s, e) => OnSearchNotes?.Invoke();
            menu.Append(searchItem);

            menu.Append(new SeparatorMenuItem());

            var recentNotes = GetRecentNotes();
            if (recentNotes.Count > 0)
            {
                foreach (var note in recentNotes)
                {
                    menu.Append(CreateNoteMenuItem(note));
                }
                menu.Append(new SeparatorMenuItem());
            }

            var startHereItem = new MenuItem("📌 Open '_Start Here'");
            startHereItem.Activated += (s, e) => OnOpenStartHere?.Invoke();
            menu.Append(startHereItem);

            menu.ShowAll();
            return menu;
        }

        private Menu CreateContextMenu()
        {
            var menu = new Menu();

            var newNoteItem = new MenuItem("📝 _Create New Note");
            newNoteItem.Activated += (s, e) => OnCreateNewNote?.Invoke();
            menu.Append(newNoteItem);

            var searchItem = new MenuItem("🔍 _Search All Notes...");
            searchItem.Activated += (s, e) => OnSearchNotes?.Invoke();
            menu.Append(searchItem);

            // Recent Notes Submenu: allows opening recent notes from right-click as well
            var recentNotes = GetRecentNotes();
            var recentMenu = new Menu();
            var recentSubmenuItem = new MenuItem("🕒 最近筆記 (_R)") { Submenu = recentMenu };
            if (recentNotes.Count > 0)
            {
                foreach (var note in recentNotes)
                {
                    recentMenu.Append(CreateNoteMenuItem(note));
                }
            }
            else
            {
                var emptyItem = new MenuItem("(無最近筆記)") { Sensitive = false };
                recentMenu.Append(emptyItem);
            }
            menu.Append(recentSubmenuItem);

            menu.Append(new SeparatorMenuItem());

            var syncItem = new MenuItem("🔄 S_ynchronize Notes");
            syncItem.Activated += (s, e) => OnSyncNotes?.Invoke();
            menu.Append(syncItem);

            var prefItem = new MenuItem("⚙ _Preferences");
            prefItem.Activated += (s, e) => OnShowPreferences?.Invoke();
            menu.Append(prefItem);

            var aboutItem = new MenuItem("ℹ _About Tomboy");
            aboutItem.Activated += (s, e) => OnShowAbout?.Invoke();
            menu.Append(aboutItem);

            menu.Append(new SeparatorMenuItem());

            var quitItem = new MenuItem("❌ _Quit");
            quitItem.Activated += (s, e) => OnQuitApp?.Invoke();
            menu.Append(quitItem);

            menu.ShowAll();
            return menu;
        }
    }
}
