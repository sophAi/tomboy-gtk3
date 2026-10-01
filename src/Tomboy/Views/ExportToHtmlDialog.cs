using System;
using System.IO;
using Gtk;
using Tomboy.Services;

namespace Tomboy.Views
{
    public class ExportToHtmlDialog : FileChooserDialog
    {
        private readonly CheckButton exportLinked;
        private readonly CheckButton exportLinkedAll;

        public ExportToHtmlDialog(Window parent, string defaultFileName)
            : base("匯出為 HTML (Export to HTML)", parent, FileChooserAction.Save)
        {
            AddButton("取消", ResponseType.Cancel);
            AddButton("儲存", ResponseType.Ok);

            DefaultResponse = ResponseType.Ok;
            DoOverwriteConfirmation = true;
            LocalOnly = true;

            var box = new VBox(false, 6) { BorderWidth = 6 };

            exportLinked = new CheckButton("匯出被連結的筆記 (Export linked notes)");
            exportLinked.Toggled += (s, e) => SetExportLinkedAllSensitivity();
            box.PackStart(exportLinked, false, false, 0);

            exportLinkedAll = new CheckButton("包含所有其他被連結的筆記 (Include all other linked notes)")
            {
                MarginStart = 24
            };
            box.PackStart(exportLinkedAll, false, false, 0);

            box.ShowAll();
            ExtraWidget = box;

            var filter = new FileFilter { Name = "HTML 網頁 (*.html, *.htm)" };
            filter.AddPattern("*.html");
            filter.AddPattern("*.htm");
            AddFilter(filter);

            var allFilter = new FileFilter { Name = "所有檔案 (*.*)" };
            allFilter.AddPattern("*");
            AddFilter(allFilter);

            LoadPreferences(defaultFileName);
            SetExportLinkedAllSensitivity();
        }

        public bool ExportLinked
        {
            get => exportLinked.Active;
            set => exportLinked.Active = value;
        }

        public bool ExportLinkedAll
        {
            get => exportLinkedAll.Active;
            set => exportLinkedAll.Active = value;
        }

        public string SelectedExportPath
        {
            get
            {
                string path = Filename ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(path) &&
                    !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                {
                    path += ".html";
                }
                return path;
            }
        }

        public void SavePreferences()
        {
            string path = SelectedExportPath;
            if (!string.IsNullOrEmpty(path))
            {
                string? dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Preferences.Current.ExportHtmlLastDirectory = dir;
                }
            }

            Preferences.Current.ExportHtmlExportLinked = ExportLinked;
            Preferences.Current.ExportHtmlExportLinkedAll = ExportLinkedAll;
            Preferences.Save();
        }

        private void LoadPreferences(string defaultFileName)
        {
            string lastDir = Preferences.Current.ExportHtmlLastDirectory;
            if (string.IsNullOrEmpty(lastDir) || !Directory.Exists(lastDir))
            {
                lastDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            SetCurrentFolder(lastDir);
            CurrentName = defaultFileName;

            ExportLinked = Preferences.Current.ExportHtmlExportLinked;
            ExportLinkedAll = Preferences.Current.ExportHtmlExportLinkedAll;
        }

        private void SetExportLinkedAllSensitivity()
        {
            exportLinkedAll.Sensitive = exportLinked.Active;
        }
    }
}
