using System;
using System.IO;
using Gtk;
using Tomboy.Services;

namespace Tomboy.Views
{
    public class SyncWindow : Window
    {
        private Entry pathEntry;
        private Label statusLabel;
        private System.Action? onSyncDone;

        public SyncWindow(System.Action? onSyncDone = null) : base(WindowType.Toplevel)
        {
            this.onSyncDone = onSyncDone;
            Title = "Synchronize Notes";
            SetDefaultSize(480, 220);
            SetPosition(WindowPosition.Center);

            var vbox = new VBox(false, 10) { BorderWidth = 16 };

            var titleLabel = new Label("<b>🔄 Synchronize Notes</b>") { UseMarkup = true, Xalign = 0 };
            vbox.PackStart(titleLabel, false, false, 0);

            var descLabel = new Label("Select a local directory, network share, or cloud folder to synchronize notes.") { Wrap = true, Xalign = 0 };
            vbox.PackStart(descLabel, false, false, 0);

            var pathHBox = new HBox(false, 6);
            string defaultPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TomboySync");
            pathEntry = new Entry(defaultPath);
            var browseBtn = new Button("Browse...");
            browseBtn.Clicked += OnBrowseClicked;

            pathHBox.PackStart(pathEntry, true, true, 0);
            pathHBox.PackEnd(browseBtn, false, false, 0);
            vbox.PackStart(pathHBox, false, false, 0);

            statusLabel = new Label(string.Empty) { Xalign = 0 };
            vbox.PackStart(statusLabel, false, false, 0);

            var btnHBox = new HBox(false, 6);
            var syncBtn = new Button("🔄 Sync Now");
            syncBtn.Clicked += OnSyncClicked;
            var closeBtn = new Button("Close");
            closeBtn.Clicked += (s, e) => Destroy();

            btnHBox.PackEnd(closeBtn, false, false, 0);
            btnHBox.PackEnd(syncBtn, false, false, 0);
            vbox.PackEnd(btnHBox, false, false, 0);

            Add(vbox);
            ShowAll();
        }

        private void OnBrowseClicked(object? sender, EventArgs e)
        {
            var dlg = new FileChooserDialog("Select Sync Folder", this, FileChooserAction.SelectFolder, "Cancel", ResponseType.Cancel, "Select", ResponseType.Accept);
            if (dlg.Run() == (int)ResponseType.Accept)
            {
                pathEntry.Text = dlg.Filename;
            }
            dlg.Hide();
            dlg.Dispose();
        }

        private void OnSyncClicked(object? sender, EventArgs e)
        {
            string target = pathEntry.Text;
            if (string.IsNullOrWhiteSpace(target))
            {
                statusLabel.Text = "Please specify a directory.";
                return;
            }

            if (!Directory.Exists(target))
            {
                try { Directory.CreateDirectory(target); }
                catch (Exception ex)
                {
                    statusLabel.Text = $"Cannot create directory: {ex.Message}";
                    return;
                }
            }

            statusLabel.Text = "Synchronizing...";
            var result = SyncService.Synchronize(target);
            statusLabel.Text = result.Message;
            onSyncDone?.Invoke();
        }
    }
}
