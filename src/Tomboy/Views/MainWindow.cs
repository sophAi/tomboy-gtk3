using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Gdk;
using Gtk;
using Tomboy.Models;
using Tomboy.Services;

namespace Tomboy.Views
{
    public class MainWindow : Gtk.Window
    {
        private List<NoteItem> allNotes = new();
        private ListStore notesListStore;
        private TreeView notesTreeView;
        private ListStore notebookStore;
        private TreeView notebookTreeView;
        private Entry searchEntry;
        private Statusbar statusBar;

        private string currentNotebookFilter = "All Notes";
        private bool isRefreshingNotebooks = false;
        private readonly HashSet<string> knownNotebooks = new(StringComparer.OrdinalIgnoreCase);

        private Pixbuf? allNotesIcon;
        private Pixbuf? unfiledNotesIcon;
        private Pixbuf? notebookIcon;
        private Pixbuf? noteIcon;

        public MainWindow() : base(Gtk.WindowType.Toplevel)
        {
            Instance = this;
            Title = "Search All Notes";
            SetDefaultSize(820, 540);
            SetPosition(WindowPosition.Center);

            // Load Tomboy Icons
            allNotesIcon = IconService.GetIcon("filter-note-all", 22);
            unfiledNotesIcon = IconService.GetIcon("filter-note-unfiled", 22);
            notebookIcon = IconService.GetIcon("notebook", 22);
            noteIcon = IconService.GetIcon("note", 22) ?? IconService.GetIcon("tomboy", 22);

            var tomboyAppIcon = IconService.GetIcon("tomboy", 48) ?? IconService.GetIcon("tomboy", 22);
            if (tomboyAppIcon != null)
            {
                Icon = tomboyAppIcon;
            }

            var mainVBox = new VBox(false, 0);

            // 1. Classic Tomboy MenuBar: File, Edit, Tools, Help
            var menuBar = CreateMenuBar();
            mainVBox.PackStart(menuBar, false, false, 0);

            // 2. Toolbar
            var toolbar = new Toolbar();
            
            var newNoteBtn = new ToolButton(Stock.New) { Label = "New Note", TooltipText = "Create New Note (Ctrl+N)" };
            newNoteBtn.Clicked += OnNewNoteClicked;
            toolbar.Insert(newNoteBtn, -1);

            var syncBtn = new ToolButton(Stock.Refresh) { Label = "Sync", TooltipText = "Synchronize Notes (Ctrl+S)" };
            syncBtn.Clicked += (s, e) => OnSyncAction();
            toolbar.Insert(syncBtn, -1);

            var deleteBtn = new ToolButton(Stock.Delete) { Label = "Delete", TooltipText = "Delete Selected Note" };
            deleteBtn.Clicked += OnDeleteClicked;
            toolbar.Insert(deleteBtn, -1);

            mainVBox.PackStart(toolbar, false, false, 0);

            // 3. Main HPaned: Sidebar & Content Area
            var hpaned = new HPaned();
            hpaned.Position = 230;

            // Sidebar: Notebooks with Icons
            var sidebarVBox = new VBox(false, 6) { BorderWidth = 6 };
            sidebarVBox.PackStart(new Label("<b>NOTEBOOKS</b>") { UseMarkup = true, Xalign = 0 }, false, false, 0);

            notebookStore = new ListStore(typeof(Pixbuf), typeof(string), typeof(bool)); // Pixbuf, Name, IsSpecial
            notebookTreeView = new TreeView(notebookStore);
            notebookTreeView.HeadersVisible = false;

            var nbColumn = new TreeViewColumn();
            var nbPixbufCell = new CellRendererPixbuf();
            nbColumn.PackStart(nbPixbufCell, false);
            nbColumn.AddAttribute(nbPixbufCell, "pixbuf", 0);

            var nbTextCell = new CellRendererText();
            nbColumn.PackStart(nbTextCell, true);
            nbColumn.SetCellDataFunc(nbTextCell, new TreeCellDataFunc((col, cell, model, iter) => {
                var crt = (CellRendererText)cell;
                string name = (string)model.GetValue(iter, 1);
                bool isSpecial = (bool)model.GetValue(iter, 2);
                if (isSpecial)
                {
                    crt.Markup = $"<span weight='bold'>{GLib.Markup.EscapeText(name)}</span>";
                }
                else
                {
                    crt.Text = name;
                }
            }));
            notebookTreeView.AppendColumn(nbColumn);

            notebookTreeView.Selection.Changed += OnNotebookSelectionChanged;
            notebookTreeView.ButtonPressEvent += OnNotebookTreeButtonPress;

            var scrollNb = new ScrolledWindow();
            scrollNb.Add(notebookTreeView);
            sidebarVBox.PackStart(scrollNb, true, true, 0);

            var newNbBtn = new Button("➕ New Notebook");
            newNbBtn.Clicked += OnNewNotebookClicked;
            sidebarVBox.PackEnd(newNbBtn, false, false, 0);

            hpaned.Pack1(sidebarVBox, false, false);

            // Right: Search & Notes List with Icons
            var rightVBox = new VBox(false, 6) { BorderWidth = 6 };
            searchEntry = new Entry() { PlaceholderText = "🔍 Search all notes..." };
            searchEntry.Changed += (s, e) => ApplyFilterSafely();
            rightVBox.PackStart(searchEntry, false, false, 0);

            notesListStore = new ListStore(typeof(Pixbuf), typeof(string), typeof(string), typeof(string), typeof(NoteItem)); // Pixbuf, Title, Notebook, Date, NoteItem
            notesTreeView = new TreeView(notesListStore);
            notesTreeView.HeadersVisible = true;

            // Note Title Column with Note Icon
            var titleCol = new TreeViewColumn { Title = "Note", Expand = true, Resizable = true, MinWidth = 150 };
            var notePixCell = new CellRendererPixbuf();
            titleCol.PackStart(notePixCell, false);
            titleCol.AddAttribute(notePixCell, "pixbuf", 0);

            var noteTitleCell = new CellRendererText { Ellipsize = Pango.EllipsizeMode.End };
            titleCol.PackStart(noteTitleCell, true);
            titleCol.AddAttribute(noteTitleCell, "text", 1);
            notesTreeView.AppendColumn(titleCol);

            // Notebook Column
            var nbCol = new TreeViewColumn { Title = "Notebook", Resizable = true };
            var nbCell = new CellRendererText { Ellipsize = Pango.EllipsizeMode.End };
            nbCol.PackStart(nbCell, true);
            nbCol.AddAttribute(nbCell, "text", 2);
            notesTreeView.AppendColumn(nbCol);

            // Last Changed Date Column (Left Aligned)
            var dateCol = new TreeViewColumn { Title = "Last Changed", Resizable = false, Alignment = 0.0f };
            var dateCell = new CellRendererText { Xalign = 0.0f };
            dateCol.PackStart(dateCell, true);
            dateCol.AddAttribute(dateCell, "text", 3);
            notesTreeView.AppendColumn(dateCol);

            titleCol.SortColumnId = 1;
            nbCol.SortColumnId = 2;
            dateCol.SortColumnId = 3;

            notesListStore.SetSortFunc(1, (model, a, b) => {
                var noteA = (NoteItem)model.GetValue(a, 4);
                var noteB = (NoteItem)model.GetValue(b, 4);
                if (noteA == null || noteB == null) return 0;
                return string.Compare(noteA.Title, noteB.Title, StringComparison.CurrentCultureIgnoreCase);
            });

            notesListStore.SetSortFunc(2, (model, a, b) => {
                var noteA = (NoteItem)model.GetValue(a, 4);
                var noteB = (NoteItem)model.GetValue(b, 4);
                if (noteA == null || noteB == null) return 0;
                return string.Compare(noteA.Notebook, noteB.Notebook, StringComparison.CurrentCultureIgnoreCase);
            });

            notesListStore.SetSortFunc(3, (model, a, b) => {
                var noteA = (NoteItem)model.GetValue(a, 4);
                var noteB = (NoteItem)model.GetValue(b, 4);
                if (noteA == null || noteB == null) return 0;
                return DateTime.Compare(noteA.LastChangeDate, noteB.LastChangeDate);
            });

            notesTreeView.RowActivated += OnNoteRowActivated;
            notesTreeView.ButtonPressEvent += OnNotesTreeButtonPress;
            notesTreeView.KeyPressEvent += (o, args) => {
                if (args.Event.Key == Gdk.Key.Delete)
                {
                    OnDeleteClicked(o, EventArgs.Empty);
                }
            };

            // Setup Drag-and-Drop: Dragging notes into notebooks
            var dragTargets = new TargetEntry[] {
                new TargetEntry("text/uri-list", TargetFlags.App, 1),
                new TargetEntry("text/plain", TargetFlags.App, 2)
            };

            notesTreeView.EnableModelDragSource(
                Gdk.ModifierType.Button1Mask,
                dragTargets,
                Gdk.DragAction.Move
            );
            notesTreeView.DragDataGet += OnNotesTreeDragDataGet;

            Gtk.Drag.DestSet(
                notebookTreeView,
                DestDefaults.All,
                dragTargets,
                Gdk.DragAction.Move
            );
            notebookTreeView.DragMotion += OnNotebookTreeDragMotion;
            notebookTreeView.DragLeave += OnNotebookTreeDragLeave;
            notebookTreeView.DragDataReceived += OnNotebookTreeDragDataReceived;

            var scrollNotes = new ScrolledWindow();
            scrollNotes.Add(notesTreeView);
            rightVBox.PackStart(scrollNotes, true, true, 0);

            hpaned.Pack2(rightVBox, true, false);
            mainVBox.PackStart(hpaned, true, true, 0);

            // 4. Status Bar (Tomboy Note Count Info)
            statusBar = new Statusbar();
            mainVBox.PackStart(statusBar, false, false, 0);

            Add(mainVBox);
            ShowAll();

            RefreshNotes();
            SelectAllNotesNotebook();
            searchEntry.GrabFocus();

            SyncService.SyncCompleted += () => RefreshNotes();
        }

        public new void Present()
        {
            base.Present();
            SelectAllNotesNotebook();
            searchEntry.GrabFocus();
            searchEntry.SelectRegion(0, -1);
            GLib.Timeout.Add(50, () => {
                if (searchEntry != null && searchEntry.Handle != IntPtr.Zero)
                {
                    searchEntry.GrabFocus();
                    searchEntry.SelectRegion(0, -1);
                }
                return false;
            });
        }

        protected override void OnShown()
        {
            base.OnShown();
            SelectAllNotesNotebook();
            searchEntry.GrabFocus();
            searchEntry.SelectRegion(0, -1);
            GLib.Timeout.Add(50, () => {
                if (searchEntry != null && searchEntry.Handle != IntPtr.Zero)
                {
                    searchEntry.GrabFocus();
                    searchEntry.SelectRegion(0, -1);
                }
                return false;
            });
        }

        public void SelectAllNotesNotebook()
        {
            currentNotebookFilter = "All Notes";
            if (notebookStore != null && notebookStore.GetIterFirst(out TreeIter firstIter))
            {
                notebookTreeView.Selection.SelectIter(firstIter);
                var path = notebookStore.GetPath(firstIter);
                if (path != null)
                {
                    notebookTreeView.SetCursor(path, null, false);
                }
            }
            ApplyFilterSafely();
        }

        protected override bool OnDeleteEvent(Gdk.Event ev)
        {
            // Intercept window close button (X) to hide instead of destroying native GTK widget
            Hide();
            return true;
        }

        private MenuBar CreateMenuBar()
        {
            var menuBar = new MenuBar();

            // --- 1. File Menu ---
            var fileMenuItem = new MenuItem("_File");
            var fileMenu = new Menu();

            var newNoteItem = new MenuItem("📝 _New Note");
            newNoteItem.Activated += OnNewNoteClicked;
            fileMenu.Append(newNoteItem);

            var syncItem = new MenuItem("🔄 _Sync Notes");
            syncItem.Activated += (s, e) => OnSyncAction();
            fileMenu.Append(syncItem);

            fileMenu.Append(new SeparatorMenuItem());

            var closeItem = new MenuItem("❌ _Close");
            closeItem.Activated += (s, e) => Hide();
            fileMenu.Append(closeItem);

            var quitItem = new MenuItem("🚪 _Quit Tomboy");
            quitItem.Activated += (s, e) => Application.Quit();
            fileMenu.Append(quitItem);

            fileMenuItem.Submenu = fileMenu;
            menuBar.Append(fileMenuItem);

            // --- 2. Edit Menu ---
            var editMenuItem = new MenuItem("_Edit");
            var editMenu = new Menu();

            var deleteItem = new MenuItem("🗑️ _Delete Note");
            deleteItem.Activated += OnDeleteClicked;
            editMenu.Append(deleteItem);

            var exportItem = new MenuItem("🌐 輸出選取筆記為 HTML(_E)...");
            exportItem.Activated += OnExportSelectedNoteToHtml;
            editMenu.Append(exportItem);

            editMenu.Append(new SeparatorMenuItem());

            var prefItem = new MenuItem("⚙️ 偏好設定(_P)");
            prefItem.Activated += (s, e) => new PreferencesWindow(this).ShowAll();
            editMenu.Append(prefItem);

            editMenuItem.Submenu = editMenu;
            menuBar.Append(editMenuItem);

            // --- 3. Tools Menu ---
            var toolsMenuItem = new MenuItem("_Tools");
            var toolsMenu = new Menu();

            var exportAllItem = new MenuItem("🌐 輸出所有筆記為 HTML...");
            exportAllItem.Activated += (s, e) => HtmlExportService.PromptExportAllNotes(allNotes, this);
            toolsMenu.Append(exportAllItem);

            var exportNotebookItem = new MenuItem("🌐 輸出目前分類筆記為 HTML...");
            exportNotebookItem.Activated += (s, e) => {
                var notesInNb = GetCurrentFilteredNotes();
                HtmlExportService.PromptExportAllNotes(notesInNb, this, currentNotebookFilter);
            };
            toolsMenu.Append(exportNotebookItem);

            toolsMenu.Append(new SeparatorMenuItem());

            var openDirItem = new MenuItem("📂 _Open Notes Folder");
            openDirItem.Activated += (s, e) => {
                try { Process.Start(new ProcessStartInfo(NoteStorage.NoteDirectory) { UseShellExecute = true }); } catch { }
            };
            toolsMenu.Append(openDirItem);

            toolsMenuItem.Submenu = toolsMenu;
            menuBar.Append(toolsMenuItem);

            // --- 4. Help Menu ---
            var helpMenuItem = new MenuItem("_Help");
            var helpMenu = new Menu();

            var aboutItem = new MenuItem("ℹ _About Tomboy");
            aboutItem.Activated += (s, e) => {
                var about = new AboutDialog
                {
                    ProgramName = "Tomboy Notes",
                    Version = "2.0.0",
                    Comments = "Classic desktop note-taking application ported to .NET 8 and GTK3.",
                    Website = "https://wiki.gnome.org/Apps/Tomboy"
                };
                about.Run();
                about.Hide();
                about.Dispose();
            };
            helpMenu.Append(aboutItem);

            helpMenuItem.Submenu = helpMenu;
            menuBar.Append(helpMenuItem);

            return menuBar;
        }

        public string? SearchText
        {
            get
            {
                if (searchEntry == null || searchEntry.Handle == IntPtr.Zero)
                    return null;
                string text = searchEntry.Text?.Trim() ?? string.Empty;
                return string.IsNullOrEmpty(text) ? null : text;
            }
            set
            {
                if (searchEntry != null && searchEntry.Handle != IntPtr.Zero)
                {
                    searchEntry.Text = value ?? string.Empty;
                }
            }
        }

        public void SetSearchText(string text)
        {
            searchEntry.Text = text;
            ApplyFilterSafely();
        }

        public void RefreshNotes()
        {
            if (isRefreshingNotebooks) return;

            isRefreshingNotebooks = true;
            try
            {
                string targetFilter = currentNotebookFilter;
                allNotes = NoteStorage.LoadAllNotes();

                foreach (var n in allNotes)
                {
                    if (!string.IsNullOrWhiteSpace(n.Notebook))
                        knownNotebooks.Add(n.Notebook);
                }

                notebookStore.Clear();
                notebookStore.AppendValues(allNotesIcon, "All Notes", true);
                notebookStore.AppendValues(unfiledNotesIcon, "Unfiled Notes", true);

                var sortedNbs = knownNotebooks.OrderBy(b => b, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var nb in sortedNbs)
                {
                    notebookStore.AppendValues(notebookIcon, nb, false);
                }

                // Maintain current selection or default to All Notes
                bool foundSelection = false;
                if (notebookStore.GetIterFirst(out TreeIter iter))
                {
                    do
                    {
                        string name = (string)notebookStore.GetValue(iter, 1);
                        if (string.Equals(name, targetFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            notebookTreeView.Selection.SelectIter(iter);
                            var path = notebookStore.GetPath(iter);
                            if (path != null)
                            {
                                notebookTreeView.SetCursor(path, null, false);
                            }
                            currentNotebookFilter = name;
                            foundSelection = true;
                            break;
                        }
                    } while (notebookStore.IterNext(ref iter));

                    if (!foundSelection && notebookStore.GetIterFirst(out TreeIter firstIter))
                    {
                        currentNotebookFilter = "All Notes";
                        notebookTreeView.Selection.SelectIter(firstIter);
                        var path = notebookStore.GetPath(firstIter);
                        if (path != null)
                        {
                            notebookTreeView.SetCursor(path, null, false);
                        }
                    }
                }
            }
            finally
            {
                isRefreshingNotebooks = false;
            }

            ApplyFilterSafely();
        }

        private void ApplyFilterSafely()
        {
            if (notesTreeView == null || notesListStore == null) return;

            notesTreeView.Model = null;
            notesListStore.Clear();

            string query = searchEntry.Text?.Trim().ToLower() ?? string.Empty;
            IEnumerable<NoteItem> querySet = allNotes;

            if (currentNotebookFilter == "Unfiled Notes")
            {
                querySet = querySet.Where(n => string.IsNullOrWhiteSpace(n.Notebook));
            }
            else if (currentNotebookFilter != "All Notes" && !string.IsNullOrEmpty(currentNotebookFilter))
            {
                querySet = querySet.Where(n => string.Equals(n.Notebook, currentNotebookFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query))
            {
                var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length > 0)
                {
                    querySet = querySet.Where(n => terms.All(term =>
                        n.Title.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.TextContent.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
                }
            }

            int count = 0;
            foreach (var note in querySet)
            {
                string displayDate = GetPrettyPrintDate(note.LastChangeDate, true);
                notesListStore.AppendValues(noteIcon, note.Title, string.IsNullOrEmpty(note.Notebook) ? "-" : note.Notebook, displayDate, note);
                count++;
            }

            notesTreeView.Model = notesListStore;

            // Update Tomboy Status Bar Count
            if (statusBar != null)
            {
                statusBar.Pop(0);
                if (!string.IsNullOrEmpty(query))
                {
                    string matchText = count == 1 ? "Matches: 1 note" : $"Matches: {count} notes";
                    statusBar.Push(0, matchText);
                }
                else
                {
                    string totalText = count == 1 ? "Total: 1 note" : $"Total: {count} notes";
                    statusBar.Push(0, totalText);
                }
            }
        }

        public static string GetPrettyPrintDate(DateTime date, bool showTime)
        {
            if (date == DateTime.MinValue)
                return "無日期";

            DateTime now = DateTime.Now;
            string shortTime = date.ToShortTimeString();
            bool isZh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

            if (date.Date == now.Date)
            {
                return showTime ? (isZh ? $"今天, {shortTime}" : $"Today, {shortTime}") : (isZh ? "今天" : "Today");
            }
            if (date.Date == now.Date.AddDays(-1))
            {
                return showTime ? (isZh ? $"昨天, {shortTime}" : $"Yesterday, {shortTime}") : (isZh ? "昨天" : "Yesterday");
            }
            int daysAgo = (int)(now.Date - date.Date).TotalDays;
            if (daysAgo >= 2 && daysAgo <= 6)
            {
                return showTime
                    ? (isZh ? $"{daysAgo} 天前，{shortTime}" : $"{daysAgo} days ago, {shortTime}")
                    : (isZh ? $"{daysAgo} 天前" : $"{daysAgo} days ago");
            }
            if (date.Date == now.Date.AddDays(1))
            {
                return showTime ? (isZh ? $"明天, {shortTime}" : $"Tomorrow, {shortTime}") : (isZh ? "明天" : "Tomorrow");
            }
            int daysAhead = (int)(date.Date - now.Date).TotalDays;
            if (daysAhead >= 2 && daysAhead <= 6)
            {
                return showTime
                    ? (isZh ? $"{daysAhead} 天內，{shortTime}" : $"In {daysAhead} days, {shortTime}")
                    : (isZh ? $"{daysAhead} 天內" : $"In {daysAhead} days");
            }
            if (date.Year == now.Year)
            {
                return showTime
                    ? (isZh ? date.ToString("M月d日，HH:mm") : date.ToString("MMM d, HH:mm"))
                    : (isZh ? date.ToString("M月d日") : date.ToString("MMM d"));
            }
            return showTime
                ? (isZh ? date.ToString("yyyy年M月d日，HH:mm") : date.ToString("MMM d yyyy, HH:mm"))
                : (isZh ? date.ToString("yyyy年M月d日") : date.ToString("MMM d yyyy"));
        }

        private void OnNotebookSelectionChanged(object? sender, EventArgs e)
        {
            if (isRefreshingNotebooks) return;

            if (notebookTreeView.Selection.GetSelected(out TreeIter iter))
            {
                currentNotebookFilter = (string)notebookStore.GetValue(iter, 1);
                ApplyFilterSafely();
            }
        }

        private void OnNewNotebookClicked(object? sender, EventArgs e)
        {
            var dlg = new Dialog("New Notebook", this, DialogFlags.Modal);
            dlg.SetDefaultSize(300, 120);

            var entry = new Entry();
            dlg.ContentArea.PackStart(new Label("Notebook Name:"), false, false, 4);
            dlg.ContentArea.PackStart(entry, false, false, 4);
            dlg.AddButton("Create", ResponseType.Accept);
            dlg.AddButton("Cancel", ResponseType.Cancel);
            dlg.ShowAll();

            if (dlg.Run() == (int)ResponseType.Accept && !string.IsNullOrWhiteSpace(entry.Text))
            {
                string nbName = entry.Text.Trim();
                knownNotebooks.Add(nbName);
                currentNotebookFilter = nbName;
                RefreshNotes();
            }

            dlg.Hide();
            dlg.Dispose();
        }

        [GLib.ConnectBefore]
        private void OnNotebookTreeButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 3) // Right-click context menu
            {
                if (notebookTreeView.GetDestRowAtPos((int)args.Event.X, (int)args.Event.Y, out TreePath path, out _))
                {
                    notebookTreeView.Selection.SelectPath(path);
                    notebookTreeView.SetCursor(path, null, false);

                    if (notebookStore.GetIter(out TreeIter iter, path))
                    {
                        string nbName = (string)notebookStore.GetValue(iter, 1);
                        bool isSpecial = (bool)notebookStore.GetValue(iter, 2);

                        var menu = new Menu();

                        var newNoteInNb = new MenuItem($"➕ 在「{GLib.Markup.EscapeText(nbName)}」建立新筆記");
                        newNoteInNb.Activated += (s, e) => {
                            CreateNewNoteInNotebook(isSpecial ? null : nbName);
                        };
                        menu.Append(newNoteInNb);

                        if (!isSpecial)
                        {
                            menu.Append(new SeparatorMenuItem());
                            var deleteNbItem = new MenuItem($"🗑️ 刪除筆記本「{GLib.Markup.EscapeText(nbName)}」");
                            deleteNbItem.Activated += (s, e) => PromptDeleteNotebook(nbName);
                            menu.Append(deleteNbItem);
                        }

                        menu.ShowAll();
                        menu.Popup();
                    }
                }
            }
        }

        private void PromptDeleteNotebook(string nbName)
        {
            var msg = new MessageDialog(
                this,
                DialogFlags.Modal,
                MessageType.Question,
                ButtonsType.None,
                $"確定要刪除筆記本「{GLib.Markup.EscapeText(nbName)}」嗎？\n此筆記本內的筆記不會被刪除，但會移至未分類。"
            );
            msg.AddButton("取消", ResponseType.Cancel);
            msg.AddButton("刪除筆記本", ResponseType.Accept);
            int resp = msg.Run();
            msg.Hide();
            msg.Dispose();

            if (resp == (int)ResponseType.Accept)
            {
                knownNotebooks.Remove(nbName);
                foreach (var note in allNotes.Where(n => string.Equals(n.Notebook, nbName, StringComparison.OrdinalIgnoreCase)))
                {
                    note.SetNotebook(null);
                    note.Save(NoteStorage.NoteDirectory);
                }
                if (string.Equals(currentNotebookFilter, nbName, StringComparison.OrdinalIgnoreCase))
                {
                    currentNotebookFilter = "All Notes";
                }
                RefreshNotes();
            }
        }

        public NoteItem CreateNewNote(string? title = null, string? nbName = null)
        {
            string noteTitle = string.IsNullOrWhiteSpace(title)
                ? $"New Note {DateTime.Now:yyyy-MM-dd HH:mm}"
                : title.Trim();

            var newNote = new NoteItem
            {
                Title = noteTitle,
                XmlContent = $"<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>{noteTitle}</note-title>\n\nDescribe your new note here.</note-content>"
            };

            string? targetNb = nbName;
            if (string.IsNullOrWhiteSpace(targetNb) && currentNotebookFilter != "All Notes" && currentNotebookFilter != "Unfiled Notes")
            {
                targetNb = currentNotebookFilter;
            }

            if (!string.IsNullOrWhiteSpace(targetNb) && targetNb != "All Notes" && targetNb != "Unfiled Notes")
            {
                newNote.Notebook = targetNb;
            }

            newNote.Save(NoteStorage.NoteDirectory);
            RefreshNotes();
            OpenNoteWindow(newNote, selectTitleOnOpen: true);
            return newNote;
        }

        private void CreateNewNoteInNotebook(string? nbName)
        {
            CreateNewNote(null, nbName);
        }

        private void OnNewNoteClicked(object? sender, EventArgs e)
        {
            CreateNewNoteInNotebook(currentNotebookFilter != "All Notes" && currentNotebookFilter != "Unfiled Notes" ? currentNotebookFilter : null);
        }

        public static MainWindow? Instance { get; private set; }
        public static readonly Dictionary<string, NoteWindow> openNoteWindows = new();

        public static NoteWindow? GetOpenNoteWindow(string guid)
        {
            if (openNoteWindows.TryGetValue(guid, out var win) && win != null && win.Handle != IntPtr.Zero)
                return win;
            return null;
        }

        public static void RegisterOpenNoteWindow(string guid, NoteWindow win)
        {
            openNoteWindows[guid] = win;
        }

        private void OnDeleteClicked(object? sender, EventArgs e)
        {
            if (notesTreeView.Selection.GetSelected(out TreeIter iter))
            {
                var note = (NoteItem)notesListStore.GetValue(iter, 4);
                if (note == null) return;

                string safeTitle = GLib.Markup.EscapeText(note.Title);
                var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Question, ButtonsType.None, $"Really delete note '{safeTitle}'?\nIf you delete a note it is permanently lost.");
                msg.AddButton("Cancel", ResponseType.Cancel);
                msg.AddButton("Delete", ResponseType.Accept);
                int response = msg.Run();
                msg.Hide();
                msg.Dispose();

                if (response == (int)ResponseType.Accept)
                {
                    CloseOpenNoteWindow(note.Guid);
                    NoteStorage.DeleteNote(note);
                    RefreshNotes();
                }
            }
        }

        private void OnNoteRowActivated(object o, RowActivatedArgs args)
        {
            if (notesListStore.GetIter(out TreeIter iter, args.Path))
            {
                var note = (NoteItem)notesListStore.GetValue(iter, 4);
                OpenNoteWindow(note, initialSearchText: SearchText);
            }
        }

        public void OpenNoteWindow(NoteItem note, bool selectTitleOnOpen = false, string? initialSearchText = null)
        {
            if (openNoteWindows.TryGetValue(note.Guid, out var existingWin) && existingWin != null && existingWin.Handle != IntPtr.Zero)
            {
                existingWin.Present();
                if (!string.IsNullOrWhiteSpace(initialSearchText))
                {
                    existingWin.OpenInNoteSearchBar(initialSearchText);
                }
                return;
            }

            var availableNbs = new List<string>();
            notebookStore.Foreach((model, path, iter) => {
                availableNbs.Add((string)model.GetValue(iter, 1));
                return false;
            });

            var win = new NoteWindow(note, availableNbs, () => RefreshNotes(), selectTitleOnOpen: selectTitleOnOpen, initialSearchText: initialSearchText);
            openNoteWindows[note.Guid] = win;
            win.DeleteEvent += (s, e) => {
                openNoteWindows.Remove(note.Guid);
            };
            win.Destroyed += (s, e) => {
                openNoteWindows.Remove(note.Guid);
            };
            win.ShowAll();
            win.Present();
        }

        public static void OpenNote(NoteItem note, bool selectTitleOnOpen = false, string? initialSearchText = null)
        {
            if (openNoteWindows.TryGetValue(note.Guid, out var existingWin) && existingWin != null && existingWin.Handle != IntPtr.Zero)
            {
                existingWin.Present();
                if (!string.IsNullOrWhiteSpace(initialSearchText))
                {
                    existingWin.OpenInNoteSearchBar(initialSearchText);
                }
                return;
            }

            if (Instance != null && Instance.Handle != IntPtr.Zero)
            {
                Instance.OpenNoteWindow(note, selectTitleOnOpen, initialSearchText);
                return;
            }

            var allNotes = NoteStorage.LoadAllNotes();
            var availableNbs = allNotes.Select(n => n.Notebook).Where(b => !string.IsNullOrEmpty(b)).Distinct();
            var win = new NoteWindow(note, availableNbs, onSave: () => Instance?.RefreshNotes(), selectTitleOnOpen: selectTitleOnOpen, initialSearchText: initialSearchText);
            openNoteWindows[note.Guid] = win;
            win.DeleteEvent += (s, e) => openNoteWindows.Remove(note.Guid);
            win.Destroyed += (s, e) => openNoteWindows.Remove(note.Guid);
            win.ShowAll();
            win.Present();
        }

        public bool OpenNoteByUriOrTitle(string uriOrTitleOrPath)
        {
            if (string.IsNullOrWhiteSpace(uriOrTitleOrPath)) return false;

            string target = uriOrTitleOrPath.Trim();
            if (target.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fileUri = new Uri(target);
                    target = fileUri.LocalPath;
                }
                catch
                {
                    target = target.Substring("file://".Length);
                }
            }

            if (target.StartsWith("note://tomboy/", StringComparison.OrdinalIgnoreCase))
            {
                target = target.Substring("note://tomboy/".Length).Trim();
            }

            // 1. Check if it is an existing file path (e.g. ~/.local/share/tomboy/<guid>.note)
            if (File.Exists(target))
            {
                string guidCandidate = System.IO.Path.GetFileNameWithoutExtension(target);
                var existing = allNotes.FirstOrDefault(n => 
                    string.Equals(n.FilePath, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Guid, guidCandidate, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    OpenNoteWindow(existing);
                    return true;
                }

                try
                {
                    var loadedNote = NoteItem.LoadFromFile(target);
                    if (loadedNote != null)
                    {
                        allNotes.Add(loadedNote);
                        RefreshNotes();
                        OpenNoteWindow(loadedNote);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading note file {target}: {ex.Message}");
                }
            }

            // 2. Check by GUID
            var note = allNotes.FirstOrDefault(n => string.Equals(n.Guid, target, StringComparison.OrdinalIgnoreCase));

            // 3. Check by Title
            if (note == null)
            {
                note = allNotes.FirstOrDefault(n => string.Equals(n.Title, target, StringComparison.OrdinalIgnoreCase));
            }

            // 4. Check by File name without extension (if target was a guid with .note or similar)
            if (note == null)
            {
                string guidPart = System.IO.Path.GetFileNameWithoutExtension(target);
                note = allNotes.FirstOrDefault(n => string.Equals(n.Guid, guidPart, StringComparison.OrdinalIgnoreCase));
            }

            // 5. If still not found, try reloading notes in case it was created externally
            if (note == null)
            {
                allNotes = NoteStorage.LoadAllNotes();
                note = allNotes.FirstOrDefault(n => 
                    string.Equals(n.Guid, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Title, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(System.IO.Path.GetFileNameWithoutExtension(n.FilePath), target, StringComparison.OrdinalIgnoreCase));
            }

            if (note != null)
            {
                OpenNoteWindow(note);
                return true;
            }

            return false;
        }

        public static void UnregisterOpenNoteWindow(string guid)
        {
            openNoteWindows.Remove(guid);
        }

        public static void CloseOpenNoteWindow(string guid)
        {
            if (openNoteWindows.TryGetValue(guid, out var win) && win != null && win.Handle != IntPtr.Zero)
            {
                openNoteWindows.Remove(guid);
                win.CloseWithoutSaving();
            }
        }

        public void OpenStartHereNote()
        {
            var startHere = allNotes.FirstOrDefault(n => n.Title == "Start Here");
            if (startHere == null)
            {
                startHere = NoteStorage.CreateDefaultStartHereNote();
                startHere.Save(NoteStorage.NoteDirectory);
                RefreshNotes();
            }
            OpenNoteWindow(startHere);
        }

        public void OnSyncAction()
        {
            var cfg = Preferences.Current;
            if (!string.IsNullOrWhiteSpace(cfg.SyncSelectedServiceAddin))
            {
                statusBar.Push(0, "正在同步筆記...");
                var res = SyncService.SynchronizeConfigured();
                statusBar.Push(0, res.Message);
                RefreshNotes();
                var dlg = new MessageDialog(this, DialogFlags.Modal, res.Success ? MessageType.Info : MessageType.Warning, ButtonsType.Ok, GLib.Markup.EscapeText(res.Message));
                dlg.Run();
                dlg.Hide();
                dlg.Dispose();
            }
            else
            {
                new SyncWindow(() => RefreshNotes()).ShowAll();
            }
        }

        [GLib.ConnectBefore]
        private void OnNotesTreeButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 3) // Right-click context menu
            {
                if (notesTreeView.GetPathAtPos((int)args.Event.X, (int)args.Event.Y, out TreePath path, out _, out _, out _))
                {
                    notesTreeView.Selection.SelectPath(path);

                    if (notesListStore.GetIter(out TreeIter iter, path))
                    {
                        var note = (NoteItem)notesListStore.GetValue(iter, 4);
                        if (note != null)
                        {
                            var menu = new Menu();

                            var openItem = new MenuItem("📖 開啟筆記(_O)");
                            openItem.Activated += (s, e) => OpenNoteWindow(note, initialSearchText: SearchText);
                            menu.Append(openItem);

                            var exportItem = new MenuItem("🌐 輸出成 HTML(_E)...");
                            exportItem.Activated += (s, e) => HtmlExportService.PromptExportNote(note, this);
                            menu.Append(exportItem);

                            menu.Append(new SeparatorMenuItem());

                            var deleteItem = new MenuItem("🗑️ 刪除筆記(_D)");
                            deleteItem.Activated += OnDeleteClicked;
                            menu.Append(deleteItem);

                            menu.ShowAll();
                            menu.Popup();
                        }
                    }
                }
            }
        }

        private void OnExportSelectedNoteToHtml(object? sender, EventArgs e)
        {
            if (notesTreeView.Selection.GetSelected(out TreeIter iter))
            {
                var note = (NoteItem)notesListStore.GetValue(iter, 4);
                if (note != null)
                {
                    HtmlExportService.PromptExportNote(note, this);
                }
            }
            else
            {
                var emptyDlg = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, "請先選取要匯出的筆記。");
                emptyDlg.Run();
                emptyDlg.Hide();
                emptyDlg.Dispose();
            }
        }

        private List<NoteItem> GetCurrentFilteredNotes()
        {
            var list = new List<NoteItem>();
            notesListStore.Foreach((model, path, iter) => {
                var note = (NoteItem)model.GetValue(iter, 4);
                if (note != null) list.Add(note);
                return false;
            });
            return list;
        }

        private void OnNotesTreeDragDataGet(object o, DragDataGetArgs args)
        {
            if (notesTreeView.Selection.GetSelected(out TreeIter iter))
            {
                var note = (NoteItem)notesListStore.GetValue(iter, 4);
                if (note != null)
                {
                    string uriData = $"note://tomboy/{note.Guid}\r\n";
                    args.SelectionData.Set(
                        Gdk.Atom.Intern("text/uri-list", false),
                        8,
                        System.Text.Encoding.UTF8.GetBytes(uriData)
                    );
                    args.SelectionData.Text = note.Guid;
                }
            }
        }

        private void OnNotebookTreeDragMotion(object o, DragMotionArgs args)
        {
            if (notebookTreeView.GetDestRowAtPos(args.X, args.Y, out TreePath path, out TreeViewDropPosition pos))
            {
                if (notebookStore.GetIter(out TreeIter iter, path))
                {
                    string nbName = (string)notebookStore.GetValue(iter, 1);
                    if (nbName == "All Notes")
                    {
                        notebookTreeView.SetDragDestRow(null, TreeViewDropPosition.IntoOrAfter);
                        Gdk.Drag.Status(args.Context, 0, args.Time);
                        args.RetVal = false;
                        return;
                    }

                    notebookTreeView.SetDragDestRow(path, TreeViewDropPosition.IntoOrAfter);
                    Gdk.Drag.Status(args.Context, Gdk.DragAction.Move, args.Time);
                    args.RetVal = true;
                    return;
                }
            }

            notebookTreeView.SetDragDestRow(null, TreeViewDropPosition.IntoOrAfter);
            Gdk.Drag.Status(args.Context, 0, args.Time);
            args.RetVal = false;
        }

        private void OnNotebookTreeDragLeave(object o, DragLeaveArgs args)
        {
            notebookTreeView.SetDragDestRow(null, TreeViewDropPosition.IntoOrAfter);
        }

        private void OnNotebookTreeDragDataReceived(object o, DragDataReceivedArgs args)
        {
            notebookTreeView.SetDragDestRow(null, TreeViewDropPosition.IntoOrAfter);

            if (notebookTreeView.GetDestRowAtPos(args.X, args.Y, out TreePath path, out TreeViewDropPosition pos))
            {
                if (notebookStore.GetIter(out TreeIter iter, path))
                {
                    string destNb = (string)notebookStore.GetValue(iter, 1);
                    if (destNb == "All Notes")
                    {
                        Gtk.Drag.Finish(args.Context, false, false, args.Time);
                        return;
                    }

                    string raw = string.Empty;
                    if (args.SelectionData.Data != null && args.SelectionData.Data.Length > 0)
                    {
                        raw = System.Text.Encoding.UTF8.GetString(args.SelectionData.Data);
                    }
                    if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(args.SelectionData.Text))
                    {
                        raw = args.SelectionData.Text;
                    }

                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        bool anyMoved = false;

                        foreach (var line in lines)
                        {
                            string item = line.Trim();
                            if (item.StartsWith("note://tomboy/"))
                            {
                                item = item.Substring("note://tomboy/".Length).Trim();
                            }

                            var note = allNotes.FirstOrDefault(n => n.Guid == item || n.Title == item);
                            if (note != null)
                            {
                                string? targetNb = (destNb == "Unfiled Notes") ? null : destNb;
                                string currentNb = note.Notebook ?? string.Empty;
                                string nextNb = targetNb ?? string.Empty;

                                if (currentNb != nextNb)
                                {
                                    note.SetNotebook(targetNb);
                                    note.Save(NoteStorage.NoteDirectory);
                                    anyMoved = true;

                                    if (openNoteWindows.TryGetValue(note.Guid, out var openWin) && openWin != null && openWin.Handle != IntPtr.Zero)
                                    {
                                        openWin.UpdateNotebookDisplay(targetNb);
                                    }
                                }
                            }
                        }

                        if (anyMoved)
                        {
                            RefreshNotes();
                            Gtk.Drag.Finish(args.Context, true, false, args.Time);
                            return;
                        }
                    }
                }
            }

            Gtk.Drag.Finish(args.Context, false, false, args.Time);
        }
    }
}
