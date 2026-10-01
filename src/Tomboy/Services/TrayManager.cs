using System;
using Gdk;
using Gtk;

namespace Tomboy.Services
{
    public class TrayManager
    {
        private StatusIcon statusIcon;
        private Menu popupMenu;

        public System.Action? OnCreateNewNote { get; set; }
        public System.Action? OnOpenStartHere { get; set; }
        public System.Action? OnSearchNotes { get; set; }
        public System.Action? OnSyncNotes { get; set; }
        public System.Action? OnShowPreferences { get; set; }
        public System.Action? OnShowAbout { get; set; }
        public System.Action? OnQuitApp { get; set; }
        public System.Action? OnToggleMainWindow { get; set; }

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

            statusIcon.Activate += (s, e) => OnToggleMainWindow?.Invoke();
            statusIcon.PopupMenu += (s, e) => ShowContextMenu();

            popupMenu = CreateContextMenu();
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

        private Menu CreateContextMenu()
        {
            var menu = new Menu();

            var newNoteItem = new MenuItem("📝 _Create New Note");
            newNoteItem.Activated += (s, e) => OnCreateNewNote?.Invoke();
            menu.Append(newNoteItem);

            var startHereItem = new MenuItem("📌 Open '_Start Here'");
            startHereItem.Activated += (s, e) => OnOpenStartHere?.Invoke();
            menu.Append(startHereItem);

            var searchItem = new MenuItem("🔍 _Search All Notes...");
            searchItem.Activated += (s, e) => OnSearchNotes?.Invoke();
            menu.Append(searchItem);

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

        private void ShowContextMenu()
        {
            popupMenu.Popup();
        }
    }
}
