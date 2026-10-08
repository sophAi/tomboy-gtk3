using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gdk;
using Gtk;
using Pango;
using Tomboy.Models;
using Tomboy.Services;

namespace Tomboy.Views
{
    public class NoteWindow : Gtk.Window
    {
        private static readonly char[] IndentBullets = { '•', '◦', '‣' };

        private NoteItem note;
        private System.Action? onSaveCallback;
        private bool isSaving = false;
        private bool isDeleting = false;
        private bool isClosed = false;
        private bool isContentChanged = false;
        private bool isMetadataChanged = false;
        private bool isLoading = true;
        private uint saveTimeoutId = 0;
        private uint urlHighlightTimeoutId = 0;
        private int rightClickOffset = -1;
        private bool isHoveringOnLink = false;
        private static Gdk.Cursor? handCursor = null;
        private static Gdk.Cursor? textCursor = null;

        private ComboBoxText notebookCombo;
        private TextView textView;
        private UndoManager undoManager;

        // In-Note Search Bar (Ctrl+F or Search Button)
        private HBox searchHBox;
        private Entry inNoteSearchEntry;
        private Label searchStatusLabel;

        // LaTeX Math Session
        private Tomboy.Latex.LatexNoteSession? latexSession;

        private class NoteSearchMatch
        {
            public TextMark StartMark { get; set; }
            public TextMark EndMark { get; set; }

            public NoteSearchMatch(TextMark startMark, TextMark endMark)
            {
                StartMark = startMark;
                EndMark = endMark;
            }
        }

        private readonly List<NoteSearchMatch> searchMatches = new();
        private int currentSearchIndex = -1;
        private string lastSearchQuery = string.Empty;
        private DateTime lastEnterPressTime = DateTime.MinValue;

        public NoteWindow(NoteItem noteItem, IEnumerable<string> availableNotebooks, System.Action? onSave = null, bool selectTitleOnOpen = false, string? initialSearchText = null) 
            : base(Gtk.WindowType.Toplevel)
        {
            this.note = noteItem;
            this.onSaveCallback = onSave;

            Title = noteItem.Title;
            SetDefaultSize(noteItem.Width > 100 ? Math.Max(noteItem.Width, 650) : 650, noteItem.Height > 100 ? noteItem.Height : 480);
            if (noteItem.X >= 0 && noteItem.Y >= 0)
            {
                Move(noteItem.X, noteItem.Y);
            }
            else
            {
                SetPosition(WindowPosition.Center);
            }

            // Initialize editor & undo manager early so menus can bind to them
            textView = new TextView();
            textView.WrapMode = Gtk.WrapMode.Word;
            textView.LeftMargin = 8;
            textView.RightMargin = 8;
            textView.PixelsAboveLines = 0;
            textView.PixelsBelowLines = 0;
            textView.PixelsInsideWrap = 0;
            undoManager = new UndoManager(textView.Buffer);

            var mainVBox = new VBox(false, 0);

            // 1. Classic Tomboy Toolbar: Search, Link, Text, Tools, Delete, Notebooks
            var toolbar = new Toolbar();
            
            // Search Button
            var searchBtn = new ToolButton(Stock.Find) { Label = "Search", TooltipText = "Find in Note (Ctrl+F)" };
            searchBtn.Clicked += (s, e) => ToggleInNoteSearchBar();
            toolbar.Insert(searchBtn, -1);

            // Link Button
            var linkBtn = new ToolButton(Stock.JumpTo) { Label = "Link", TooltipText = "Link Selection to Note Title" };
            linkBtn.Clicked += OnLinkBtnClicked;
            toolbar.Insert(linkBtn, -1);

            // Text (Formatting Dropdown Menu)
            var textMenuBtn = new MenuToolButton(Stock.SelectFont) { Label = "Text", TooltipText = "Text Formatting & Indentation" };
            textMenuBtn.Menu = CreateTextFormattingMenu();
            toolbar.Insert(textMenuBtn, -1);

            // Tools (Tools Dropdown Menu)
            var toolsMenuBtn = new MenuToolButton(Stock.Preferences) { Label = "Tools", TooltipText = "Tools & Actions" };
            toolsMenuBtn.Menu = CreateToolsMenu();
            toolbar.Insert(toolsMenuBtn, -1);

            // Delete Button
            var deleteBtn = new ToolButton(Stock.Delete) { Label = "Delete", TooltipText = "Delete Note" };
            deleteBtn.Clicked += OnDeleteButtonClicked;
            toolbar.Insert(deleteBtn, -1);

            // Notebooks Combo
            notebookCombo = new ComboBoxText();
            notebookCombo.AppendText("(None)");
            foreach (var nb in availableNotebooks)
            {
                if (nb != "All Notes" && nb != "Unfiled Notes" && nb != "(None)")
                {
                    notebookCombo.AppendText(nb);
                }
            }

            int activeIdx = 0;
            if (!string.IsNullOrEmpty(noteItem.Notebook))
            {
                int idx = 1;
                foreach (var nb in availableNotebooks)
                {
                    if (nb == noteItem.Notebook) { activeIdx = idx; break; }
                    if (nb != "All Notes" && nb != "Unfiled Notes" && nb != "(None)") idx++;
                }
            }
            notebookCombo.Active = activeIdx;
            notebookCombo.Changed += (s, e) => {
                string selected = notebookCombo.ActiveText;
                note.SetNotebook(selected);
                isMetadataChanged = true;
                QueueSave(300, contentChanged: false);
            };

            var nbToolItem = new ToolItem();
            notebookCombo.TooltipText = "Notebook";
            nbToolItem.Add(notebookCombo);
            toolbar.Insert(nbToolItem, -1);

            mainVBox.PackStart(toolbar, false, false, 0);

            // 2. In-Note Search Bar Component (Hidden by default, toggled with Ctrl+F)
            searchHBox = new HBox(false, 6) { BorderWidth = 4, NoShowAll = true, Visible = false };
            inNoteSearchEntry = new Entry() { PlaceholderText = "Find text in note..." };
            inNoteSearchEntry.Changed += OnInNoteSearchChanged;
            inNoteSearchEntry.Activated += OnInNoteSearchActivated;
            inNoteSearchEntry.KeyPressEvent += OnInNoteSearchKeyPress;

            var nextBtn = new Button("↓ Next");
            nextBtn.Clicked += (s, e) => CycleSearchMatch(forward: true);

            var prevBtn = new Button("↑ Prev");
            prevBtn.Clicked += (s, e) => CycleSearchMatch(forward: false);

            var closeSearchBtn = new Button("✖");
            closeSearchBtn.Clicked += (s, e) => CloseInNoteSearchBar();

            searchStatusLabel = new Label(string.Empty);

            searchHBox.PackStart(new Label("🔍 Find:"), false, false, 0);
            searchHBox.PackStart(inNoteSearchEntry, true, true, 0);
            searchHBox.PackStart(nextBtn, false, false, 0);
            searchHBox.PackStart(prevBtn, false, false, 0);
            searchHBox.PackStart(searchStatusLabel, false, false, 0);
            searchHBox.PackEnd(closeSearchBtn, false, false, 0);

            // Realize all children, but ensure searchHBox is hidden and ignores container ShowAll
            searchHBox.NoShowAll = false;
            searchHBox.ShowAll();
            searchHBox.NoShowAll = true;
            searchHBox.Hide();
            searchHBox.Visible = false;

            mainVBox.PackStart(searchHBox, false, false, 0);

            ApplyCustomPreferences();
            Preferences.SettingChanged += OnPreferencesChanged;

            XmlNoteBufferBinder.EnsureCommonTags(textView.Buffer);

            // Load existing note titles for interlinking
            var allTitles = NoteStorage.GetAllNoteTitles();

            undoManager.FreezeUndo();
            XmlNoteBufferBinder.LoadXmlToBuffer(textView.Buffer, noteItem.XmlContent, allTitles, note.Title);
            undoManager.ThawUndo();
            undoManager.ClearUndoHistory();

            // Set cursor position or select title if opening a new note
            if (selectTitleOnOpen)
            {
                TextIter start = textView.Buffer.StartIter;
                TextIter end = start;
                end.ForwardToLineEnd();
                textView.Buffer.SelectRange(end, start);
            }
            else if (noteItem.CursorPosition >= 0 && noteItem.CursorPosition <= textView.Buffer.CharCount)
            {
                TextIter cursorIter = textView.Buffer.GetIterAtOffset(noteItem.CursorPosition);
                textView.Buffer.PlaceCursor(cursorIter);
            }

            // Handle Link Clicks & Hover
            textView.AddEvents((int)Gdk.EventMask.PointerMotionMask | (int)Gdk.EventMask.LeaveNotifyMask);
            textView.ButtonPressEvent += OnTextViewButtonPress;
            textView.MotionNotifyEvent += OnTextViewMotionNotify;
            textView.LeaveNotifyEvent += OnTextViewLeaveNotify;

            // Keyboard Shortcuts & Debounced Buffer Change Handler
            textView.KeyPressEvent += OnTextViewKeyPress;
            textView.PopulatePopup += OnTextViewPopulatePopup;
            textView.Buffer.Changed += (s, e) => {
                if (isClosed || isLoading || (latexSession != null && latexSession.IsRenderingLatex)) return;
                isContentChanged = true;
                UpdateTitleFormatting();
                QueueSave(1500, contentChanged: true);
                QueueUrlHighlight(250);
                if (searchHBox.Visible && !string.IsNullOrWhiteSpace(inNoteSearchEntry.Text))
                {
                    PerformInNoteSearch(scrollToMatch: false, advanceNext: false);
                }
            };

            var scroll = new ScrolledWindow();
            scroll.Add(textView);
            mainVBox.PackStart(scroll, true, true, 0);

            Add(mainVBox);
            ShowAll();
            searchHBox.Hide(); // Initially hidden
            isLoading = false;

            FocusChild = textView;
            textView.GrabFocus();

            GLib.Idle.Add(() =>
            {
                if (!isClosed)
                {
                    if (!string.IsNullOrWhiteSpace(initialSearchText))
                    {
                        OpenInNoteSearchBar(initialSearchText);
                    }
                    else if (selectTitleOnOpen && textView != null && textView.Handle != IntPtr.Zero && textView.Buffer != null && textView.Buffer.Handle != IntPtr.Zero)
                    {
                        textView.GrabFocus();
                        TextIter start = textView.Buffer.StartIter;
                        TextIter end = start;
                        end.ForwardToLineEnd();
                        textView.Buffer.SelectRange(end, start);
                    }
                    else if (textView != null && textView.Handle != IntPtr.Zero)
                    {
                        textView.GrabFocus();
                    }
                }
                return false;
            });

            InitLatexSession();
            QueueUrlHighlight(50);
        }

        private void OnDeleteButtonClicked(object? sender, EventArgs e)
        {
            string safeTitle = GLib.Markup.EscapeText(note.Title);
            var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Question, ButtonsType.None, $"Really delete note '{safeTitle}'?\nIf you delete a note it is permanently lost.");
            msg.AddButton("Cancel", ResponseType.Cancel);
            msg.AddButton("Delete", ResponseType.Accept);
            int response = msg.Run();
            msg.Hide();
            msg.Dispose();

            if (response == (int)ResponseType.Accept)
            {
                isClosed = true;
                isDeleting = true;
                if (saveTimeoutId != 0)
                {
                    GLib.Source.Remove(saveTimeoutId);
                    saveTimeoutId = 0;
                }
                if (urlHighlightTimeoutId != 0)
                {
                    GLib.Source.Remove(urlHighlightTimeoutId);
                    urlHighlightTimeoutId = 0;
                }
                latexSession?.Dispose();
                latexSession = null;
                MainWindow.UnregisterOpenNoteWindow(note.Guid);
                NoteStorage.DeleteNote(note);
                onSaveCallback?.Invoke();
                if (Handle != IntPtr.Zero)
                {
                    Hide();
                    Dispose();
                }
            }
        }

        public void CloseWithoutSaving()
        {
            isClosed = true;
            isDeleting = true;
            if (saveTimeoutId != 0)
            {
                GLib.Source.Remove(saveTimeoutId);
                saveTimeoutId = 0;
            }
            if (urlHighlightTimeoutId != 0)
            {
                GLib.Source.Remove(urlHighlightTimeoutId);
                urlHighlightTimeoutId = 0;
            }
            latexSession?.Dispose();
            latexSession = null;
            if (Handle != IntPtr.Zero)
            {
                Hide();
                Dispose();
            }
        }

        private void UpdateTitleFormatting()
        {
            if (textView == null || textView.Handle == IntPtr.Zero || textView.Buffer == null || textView.Buffer.Handle == IntPtr.Zero) return;

            TextBuffer buf = textView.Buffer;
            TextIter titleStart = buf.StartIter;
            TextIter titleEnd = titleStart;
            titleEnd.ForwardToLineEnd();

            XmlNoteBufferBinder.EnsureCommonTags(buf);
            TextTag? titleTag = buf.TagTable.Lookup("note-title");

            if (titleTag != null)
            {
                if (titleStart.Offset != titleEnd.Offset)
                {
                    buf.ApplyTag(titleTag, titleStart, titleEnd);
                }

                if (!titleEnd.IsEnd)
                {
                    TextIter rest = titleEnd;
                    if (rest.ForwardChar())
                    {
                        buf.RemoveTag(titleTag, rest, buf.EndIter);
                    }
                }
            }

            string extractedTitle = buf.GetText(titleStart, titleEnd, false).Trim();
            if (!string.IsNullOrWhiteSpace(extractedTitle) && extractedTitle != Title)
            {
                note.Title = extractedTitle;
                note.LastMetadataChangeDate = DateTime.Now;
                Title = extractedTitle;
            }
        }

        private void QueueSave(uint delayMs = 1500, bool contentChanged = true)
        {
            if (isClosed || isSaving || isDeleting) return;

            if (contentChanged)
            {
                isContentChanged = true;
            }

            if (saveTimeoutId != 0)
            {
                GLib.Source.Remove(saveTimeoutId);
                saveTimeoutId = 0;
            }

            saveTimeoutId = GLib.Timeout.Add(delayMs, () => {
                saveTimeoutId = 0;
                SaveNote();
                return false;
            });
        }

        private void OnLinkBtnClicked(object? sender, EventArgs e)
        {
            TextBuffer buf = textView.Buffer;
            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                string selected = buf.GetText(start, end, false).Trim().TrimEnd('.', ',', ';', '!', '?');
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    TextTag linkTag = XmlNoteBufferBinder.GetOrCreateLinkTag(buf, selected);
                    buf.ApplyTag(linkTag, start, end);
                    SaveNote();
                    OpenOrCreateNoteByTitle(selected);
                }
            }
        }

        private void OnTextViewButtonPress(object o, ButtonPressEventArgs args)
        {
            int x, y;
            textView.WindowToBufferCoords(TextWindowType.Widget, (int)args.Event.X, (int)args.Event.Y, out x, out y);
            textView.GetIterAtLocation(out TextIter iter, x, y);

            if (args.Event.Button == 3) // Right Click
            {
                rightClickOffset = iter.Offset;
            }
            else if (args.Event.Button == 1) // Left Click
            {
                if (latexSession != null && latexSession.HandleClick(iter))
                {
                    args.RetVal = true;
                    return;
                }

                foreach (TextTag tag in iter.Tags)
                {
                    if (tag.Name != null && (tag.Name.StartsWith("note_link_") || tag.Name == "link:internal"))
                    {
                        TextIter startIter = iter;
                        if (!startIter.BeginsTag(tag)) startIter.BackwardToTagToggle(tag);
                        TextIter endIter = iter;
                        if (!endIter.EndsTag(tag)) endIter.ForwardToTagToggle(tag);

                        string clickedTitle = textView.Buffer.GetText(startIter, endIter, false);
                        OpenOrCreateNoteByTitle(clickedTitle);
                        args.RetVal = true;
                        break;
                    }
                    else if (tag.Name == "link:url" || tag.Name == "ext_url")
                    {
                        TextIter startIter = iter;
                        if (!startIter.BeginsTag(tag)) startIter.BackwardToTagToggle(tag);
                        TextIter endIter = iter;
                        if (!endIter.EndsTag(tag)) endIter.ForwardToTagToggle(tag);

                        string url = textView.Buffer.GetText(startIter, endIter, false);
                        XmlNoteBufferBinder.OpenUrl(url);
                        args.RetVal = true;
                        break;
                    }
                }
            }
        }

        private void OnTextViewPopulatePopup(object o, PopulatePopupArgs args)
        {
            if (args.Popup is Menu menu)
            {
                if (rightClickOffset >= 0 && textView.Buffer != null && rightClickOffset <= textView.Buffer.CharCount)
                {
                    TextIter clickIter = textView.Buffer.GetIterAtOffset(rightClickOffset);
                    foreach (TextTag tag in clickIter.Tags)
                    {
                        if (tag.Name == "link:url" || tag.Name == "ext_url")
                        {
                            TextIter startIter = clickIter;
                            if (!startIter.BeginsTag(tag)) startIter.BackwardToTagToggle(tag);
                            TextIter endIter = clickIter;
                            if (!endIter.EndsTag(tag)) endIter.ForwardToTagToggle(tag);

                            string rawUrl = textView.Buffer.GetText(startIter, endIter, false);

                            var sep = new SeparatorMenuItem();
                            sep.Show();
                            menu.Prepend(sep);

                            var copyItem = new MenuItem("複製連結位址 (_Copy Link Address)");
                            copyItem.Activated += (s, e) => {
                                string normUrl = XmlNoteBufferBinder.NormalizeUrl(rawUrl);
                                Clipboard.Get(Gdk.Selection.Clipboard).Text = string.IsNullOrEmpty(normUrl) ? rawUrl : normUrl;
                            };
                            copyItem.Show();
                            menu.Prepend(copyItem);

                            var openItem = new MenuItem("開啟連結 (_Open Link)");
                            openItem.Activated += (s, e) => XmlNoteBufferBinder.OpenUrl(rawUrl);
                            openItem.Show();
                            menu.Prepend(openItem);

                            break;
                        }
                        else if (tag.Name != null && (tag.Name.StartsWith("note_link_") || tag.Name == "link:internal"))
                        {
                            TextIter startIter = clickIter;
                            if (!startIter.BeginsTag(tag)) startIter.BackwardToTagToggle(tag);
                            TextIter endIter = clickIter;
                            if (!endIter.EndsTag(tag)) endIter.ForwardToTagToggle(tag);

                            string title = textView.Buffer.GetText(startIter, endIter, false);

                            var sep = new SeparatorMenuItem();
                            sep.Show();
                            menu.Prepend(sep);

                            var openNoteItem = new MenuItem($"開啟筆記 \"{title}\" (_Open Note)");
                            openNoteItem.Activated += (s, e) => OpenOrCreateNoteByTitle(title);
                            openNoteItem.Show();
                            menu.Prepend(openNoteItem);

                            break;
                        }
                    }
                    rightClickOffset = -1;
                }

                if (latexSession != null)
                {
                    var children = menu.Children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children[i];
                        if (child is MenuItem mi && mi.Child is Label lbl)
                        {
                            string text = lbl.Text ?? string.Empty;
                            bool isCopy = text.Contains("Copy") || text.Contains("複製");
                            bool isCut = text.Contains("Cut") || text.Contains("剪下");
                            bool isPaste = text.Contains("Paste") || text.Contains("貼上");

                            if (isCopy || isCut || isPaste)
                            {
                                var newMi = new MenuItem(text)
                                {
                                    UseUnderline = true,
                                    Sensitive = mi.Sensitive
                                };
                                if (isCopy)
                                    newMi.Activated += (ms, me) => latexSession.HandleCopy();
                                else if (isCut)
                                    newMi.Activated += (ms, me) => latexSession.HandleCut();
                                else if (isPaste)
                                    newMi.Activated += (ms, me) => latexSession.HandlePaste();

                                newMi.Show();
                                menu.Remove(mi);
                                menu.Insert(newMi, i);
                            }
                        }
                    }
                }
            }
        }

        private void QueueUrlHighlight(uint delayMs = 250)
        {
            if (isClosed || isLoading || (latexSession != null && latexSession.IsRenderingLatex)) return;

            if (urlHighlightTimeoutId != 0)
            {
                GLib.Source.Remove(urlHighlightTimeoutId);
                urlHighlightTimeoutId = 0;
            }

            urlHighlightTimeoutId = GLib.Timeout.Add(delayMs, () =>
            {
                urlHighlightTimeoutId = 0;
                if (!isClosed && !isLoading && textView != null && textView.Handle != IntPtr.Zero && textView.Buffer != null && textView.Buffer.Handle != IntPtr.Zero)
                {
                    if (latexSession == null || !latexSession.IsRenderingLatex)
                    {
                        XmlNoteBufferBinder.HighlightExternalUrls(textView.Buffer);
                        var allTitles = NoteStorage.GetAllNoteTitles();
                        XmlNoteBufferBinder.HighlightNoteTitleLinks(textView.Buffer, allTitles, note.Title);
                    }
                }
                return false;
            });
        }

        [GLib.ConnectBefore]
        private void OnTextViewMotionNotify(object o, MotionNotifyEventArgs args)
        {
            if (isClosed || textView == null || textView.Handle == IntPtr.Zero || textView.Buffer == null || textView.Buffer.Handle == IntPtr.Zero)
                return;

            int x, y;
            textView.WindowToBufferCoords(TextWindowType.Widget, (int)args.Event.X, (int)args.Event.Y, out x, out y);
            textView.GetIterAtLocation(out TextIter iter, x, y);

            bool isOverLink = false;
            foreach (TextTag tag in iter.Tags)
            {
                if (tag.Name != null && (tag.Name == "link:url" || tag.Name == "ext_url" || tag.Name.StartsWith("note_link_") || tag.Name == "link:internal"))
                {
                    isOverLink = true;
                    break;
                }
            }

            bool avoidHand = (args.Event.State & (ModifierType.ShiftMask | ModifierType.ControlMask)) != 0;

            if (isOverLink != isHoveringOnLink)
            {
                isHoveringOnLink = isOverLink;
                var textWin = textView.GetWindow(TextWindowType.Text);
                if (textWin != null && textWin.Handle != IntPtr.Zero)
                {
                    if (isOverLink && !avoidHand)
                    {
                        handCursor ??= new Gdk.Cursor(Gdk.CursorType.Hand2);
                        textWin.Cursor = handCursor;
                    }
                    else
                    {
                        textCursor ??= new Gdk.Cursor(Gdk.CursorType.Xterm);
                        textWin.Cursor = textCursor;
                    }
                }
            }
        }

        private void OnTextViewLeaveNotify(object o, LeaveNotifyEventArgs args)
        {
            if (isHoveringOnLink)
            {
                isHoveringOnLink = false;
                var textWin = textView?.GetWindow(TextWindowType.Text);
                if (textWin != null && textWin.Handle != IntPtr.Zero)
                {
                    textCursor ??= new Gdk.Cursor(Gdk.CursorType.Xterm);
                    textWin.Cursor = textCursor;
                }
            }
        }

        private void OpenOrCreateNoteByTitle(string targetTitle)
        {
            if (string.IsNullOrWhiteSpace(targetTitle)) return;
            targetTitle = targetTitle.Trim().TrimEnd('.', ',', ';', '!', '?');
            if (string.IsNullOrWhiteSpace(targetTitle)) return;

            var allNotes = NoteStorage.LoadAllNotes();
            var targetNote = allNotes.FirstOrDefault(n => string.Equals(n.Title, targetTitle, StringComparison.OrdinalIgnoreCase));

            if (targetNote == null)
            {
                targetNote = new NoteItem
                {
                    Title = targetTitle,
                    XmlContent = $"<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>{targetTitle}</note-title>\n\nDescribe your new note here.</note-content>"
                };
                targetNote.Save(NoteStorage.NoteDirectory);
                onSaveCallback?.Invoke();
            }

            MainWindow.OpenNote(targetNote, selectTitleOnOpen: false);
        }

        public string? SearchText
        {
            get
            {
                if (inNoteSearchEntry == null || inNoteSearchEntry.Handle == IntPtr.Zero)
                    return null;
                string text = inNoteSearchEntry.Text?.Trim() ?? string.Empty;
                return string.IsNullOrEmpty(text) ? null : text;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    OpenInNoteSearchBar(value);
                }
            }
        }

        public void ToggleInNoteSearchBar()
        {
            if (searchHBox.Visible)
            {
                CloseInNoteSearchBar();
            }
            else
            {
                OpenInNoteSearchBar();
            }
        }

        public void OpenInNoteSearchBar(string? searchText = null)
        {
            searchHBox.NoShowAll = false;
            searchHBox.ShowAll();
            searchHBox.NoShowAll = true;
            searchHBox.Visible = true;

            if (!string.IsNullOrEmpty(searchText))
            {
                textView.Buffer.PlaceCursor(textView.Buffer.StartIter);
                inNoteSearchEntry.Text = searchText;
            }
            else if (textView.Buffer.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                string sel = textView.Buffer.GetText(start, end, false);
                if (!string.IsNullOrEmpty(sel) && !sel.Contains('\n'))
                {
                    inNoteSearchEntry.Text = sel;
                }
            }

            inNoteSearchEntry.GrabFocus();
            inNoteSearchEntry.SelectRegion(0, inNoteSearchEntry.Text.Length);
            if (!string.IsNullOrWhiteSpace(inNoteSearchEntry.Text))
            {
                PerformInNoteSearch(scrollToMatch: true, advanceNext: false);
            }
        }

        private void CloseInNoteSearchBar()
        {
            ClearSearchHighlights();
            searchStatusLabel.Text = string.Empty;
            searchHBox.Hide();
            searchHBox.Visible = false;
            textView.GrabFocus();
        }

        protected override bool OnKeyPressEvent(Gdk.EventKey ev)
        {
            bool ctrl = (ev.State & ModifierType.ControlMask) != 0;
            bool shift = (ev.State & ModifierType.ShiftMask) != 0;
            if (ctrl && (ev.Key == Gdk.Key.z || ev.Key == Gdk.Key.Z))
            {
                if (shift)
                    undoManager.Redo();
                else
                    undoManager.Undo();
                return true;
            }
            if (ctrl && (ev.Key == Gdk.Key.y || ev.Key == Gdk.Key.Y))
            {
                undoManager.Redo();
                return true;
            }
            if (ctrl && (ev.Key == Gdk.Key.f || ev.Key == Gdk.Key.F))
            {
                ToggleInNoteSearchBar();
                return true;
            }
            if (ctrl && (ev.Key == Gdk.Key.g || ev.Key == Gdk.Key.G))
            {
                if (!searchHBox.Visible)
                {
                    ToggleInNoteSearchBar();
                }
                CycleSearchMatch(forward: !shift);
                return true;
            }
            if (ev.Key == Gdk.Key.Escape && searchHBox.Visible)
            {
                CloseInNoteSearchBar();
                return true;
            }
            return base.OnKeyPressEvent(ev);
        }

        [GLib.ConnectBefore]
        private void OnInNoteSearchKeyPress(object o, KeyPressEventArgs args)
        {
            var ev = args.Event;
            bool ctrl = (ev.State & ModifierType.ControlMask) != 0;
            bool shift = (ev.State & ModifierType.ShiftMask) != 0;

            if (ctrl && (ev.Key == Gdk.Key.f || ev.Key == Gdk.Key.F))
            {
                ToggleInNoteSearchBar();
                args.RetVal = true;
                return;
            }

            if (ev.Key == Gdk.Key.Return || ev.Key == Gdk.Key.KP_Enter)
            {
                lastEnterPressTime = DateTime.UtcNow;
                CycleSearchMatch(forward: !shift);
                args.RetVal = true;
                return;
            }

            if (ev.Key == Gdk.Key.Escape)
            {
                CloseInNoteSearchBar();
                args.RetVal = true;
                return;
            }
        }

        private void OnInNoteSearchActivated(object? sender, EventArgs e)
        {
            if ((DateTime.UtcNow - lastEnterPressTime).TotalMilliseconds < 100)
                return;

            lastEnterPressTime = DateTime.UtcNow;
            CycleSearchMatch(forward: true);
        }

        private void OnInNoteSearchChanged(object? sender, EventArgs e)
        {
            PerformInNoteSearch(scrollToMatch: true, advanceNext: false);
        }

        private void PerformInNoteSearch(bool scrollToMatch, bool advanceNext = false)
        {
            string query = inNoteSearchEntry.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(query))
            {
                ClearSearchHighlights();
                searchStatusLabel.Text = string.Empty;
                lastSearchQuery = string.Empty;
                return;
            }

            TextBuffer buf = textView.Buffer;
            if (buf == null || buf.Handle == IntPtr.Zero) return;

            if (query == lastSearchQuery && searchMatches.Count > 0 && advanceNext)
            {
                CycleSearchMatch(forward: true);
                return;
            }

            ClearSearchHighlights();
            lastSearchQuery = query;

            TextIter searchIter = buf.StartIter;
            while (searchIter.ForwardSearch(query, TextSearchFlags.CaseInsensitive, out TextIter matchStart, out TextIter matchEnd, buf.EndIter))
            {
                TextMark startMark = buf.CreateMark(null, matchStart, false);
                TextMark endMark = buf.CreateMark(null, matchEnd, true);
                searchMatches.Add(new NoteSearchMatch(startMark, endMark));

                buf.ApplyTag("find-match", matchStart, matchEnd);

                searchIter = matchEnd;
                if (searchIter.IsEnd) break;
            }

            if (searchMatches.Count == 0)
            {
                searchStatusLabel.Text = "Not found";
                currentSearchIndex = -1;
            }
            else
            {
                int nearestIdx = 0;
                TextIter cursorIter = buf.GetIterAtMark(buf.InsertMark);
                for (int i = 0; i < searchMatches.Count; i++)
                {
                    TextIter mStart = buf.GetIterAtMark(searchMatches[i].StartMark);
                    if (mStart.Offset >= cursorIter.Offset)
                    {
                        nearestIdx = i;
                        break;
                    }
                }

                currentSearchIndex = nearestIdx;
                HighlightCurrentMatch(scrollToMatch);
            }
        }

        private void CycleSearchMatch(bool forward)
        {
            string query = inNoteSearchEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query)) return;

            if (searchMatches.Count == 0 || query != lastSearchQuery)
            {
                PerformInNoteSearch(scrollToMatch: true, advanceNext: false);
                return;
            }

            if (currentSearchIndex < 0)
            {
                TextBuffer buf = textView.Buffer;
                TextIter cursorIter = buf.GetIterAtMark(buf.InsertMark);
                int nearest = 0;
                for (int i = 0; i < searchMatches.Count; i++)
                {
                    TextIter mStart = buf.GetIterAtMark(searchMatches[i].StartMark);
                    if (mStart.Offset >= cursorIter.Offset)
                    {
                        nearest = i;
                        break;
                    }
                }
                currentSearchIndex = nearest;
            }
            else
            {
                if (forward)
                {
                    currentSearchIndex = (currentSearchIndex + 1) % searchMatches.Count;
                }
                else
                {
                    currentSearchIndex = (currentSearchIndex - 1 + searchMatches.Count) % searchMatches.Count;
                }
            }

            HighlightCurrentMatch(scrollToMatch: true);
        }

        private void HighlightCurrentMatch(bool scrollToMatch)
        {
            if (currentSearchIndex < 0 || currentSearchIndex >= searchMatches.Count) return;

            TextBuffer buf = textView.Buffer;
            if (buf == null || buf.Handle == IntPtr.Zero) return;

            buf.RemoveTag("find-match-current", buf.StartIter, buf.EndIter);

            foreach (var m in searchMatches)
            {
                TextIter s = buf.GetIterAtMark(m.StartMark);
                TextIter e = buf.GetIterAtMark(m.EndMark);
                buf.ApplyTag("find-match", s, e);
            }

            var current = searchMatches[currentSearchIndex];
            TextIter start = buf.GetIterAtMark(current.StartMark);
            TextIter end = buf.GetIterAtMark(current.EndMark);

            buf.ApplyTag("find-match-current", start, end);
            buf.SelectRange(start, end);

            if (scrollToMatch)
            {
                textView.ScrollToIter(start, 0.1, false, 0, 0);
            }

            searchStatusLabel.Text = $"{currentSearchIndex + 1} of {searchMatches.Count}";
        }

        private void ClearSearchHighlights()
        {
            TextBuffer buf = textView?.Buffer;
            if (buf != null && buf.Handle != IntPtr.Zero)
            {
                buf.RemoveTag("find-match", buf.StartIter, buf.EndIter);
                buf.RemoveTag("find-match-current", buf.StartIter, buf.EndIter);

                foreach (var m in searchMatches)
                {
                    if (m.StartMark != null && m.StartMark.Handle != IntPtr.Zero)
                        buf.DeleteMark(m.StartMark);
                    if (m.EndMark != null && m.EndMark.Handle != IntPtr.Zero)
                        buf.DeleteMark(m.EndMark);
                }
            }
            searchMatches.Clear();
            currentSearchIndex = -1;
            lastSearchQuery = string.Empty;
        }

        private void OnTextViewKeyPress(object o, KeyPressEventArgs args)
        {
            var ev = args.Event;
            bool ctrl = (ev.State & ModifierType.ControlMask) != 0;
            bool shift = (ev.State & ModifierType.ShiftMask) != 0;
            bool alt = (ev.State & ModifierType.Mod1Mask) != 0;

            switch (ev.Key)
            {
                case Gdk.Key.space:
                case Gdk.Key.KP_Space:
                case Gdk.Key.period:
                case Gdk.Key.comma:
                case Gdk.Key.colon:
                case Gdk.Key.semicolon:
                case Gdk.Key.parenright:
                case Gdk.Key.bracketright:
                    QueueUrlHighlight(50);
                    break;
                case Gdk.Key.KP_Enter:
                case Gdk.Key.Return:
                    QueueUrlHighlight(50);
                    if (!ctrl)
                    {
                        args.RetVal = AddNewline(shift);
                        return;
                    }
                    break;
                case Gdk.Key.Tab:
                    QueueUrlHighlight(50);
                    args.RetVal = AddTab();
                    return;
                case Gdk.Key.ISO_Left_Tab:
                    QueueUrlHighlight(50);
                    args.RetVal = RemoveTab();
                    return;
                case Gdk.Key.BackSpace:
                    QueueUrlHighlight(50);
                    args.RetVal = BackspaceKeyHandler();
                    if ((bool)args.RetVal) return;
                    break;
                case Gdk.Key.Delete:
                    QueueUrlHighlight(50);
                    args.RetVal = DeleteKeyHandler();
                    if ((bool)args.RetVal) return;
                    break;
                case Gdk.Key.Escape:
                    if (searchHBox.Visible)
                    {
                        CloseInNoteSearchBar();
                        args.RetVal = true;
                        return;
                    }
                    break;
            }

            if (ctrl)
            {
                switch (ev.Key)
                {
                    case Gdk.Key.c:
                    case Gdk.Key.C:
                        if (latexSession != null && latexSession.HandleCopy())
                        {
                            args.RetVal = true;
                            return;
                        }
                        break;
                    case Gdk.Key.x:
                    case Gdk.Key.X:
                        if (latexSession != null && latexSession.HandleCut())
                        {
                            args.RetVal = true;
                            return;
                        }
                        break;
                    case Gdk.Key.v:
                    case Gdk.Key.V:
                        if (latexSession != null && latexSession.HandlePaste())
                        {
                            args.RetVal = true;
                            return;
                        }
                        break;
                    case Gdk.Key.z:
                    case Gdk.Key.Z:
                        if (shift)
                            undoManager.Redo();
                        else
                            undoManager.Undo();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.y:
                    case Gdk.Key.Y:
                        undoManager.Redo();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.b:
                    case Gdk.Key.B:
                        ToggleTagOnSelection("bold");
                        args.RetVal = true;
                        break;
                    case Gdk.Key.i:
                    case Gdk.Key.I:
                        ToggleTagOnSelection("italic");
                        args.RetVal = true;
                        break;
                    case Gdk.Key.u:
                    case Gdk.Key.U:
                        ToggleTagOnSelection("underline");
                        args.RetVal = true;
                        break;
                    case Gdk.Key.s:
                    case Gdk.Key.S:
                        ToggleTagOnSelection("strikethrough");
                        args.RetVal = true;
                        break;
                    case Gdk.Key.h:
                    case Gdk.Key.H:
                        ToggleTagOnSelection("highlight");
                        args.RetVal = true;
                        break;
                    case Gdk.Key.d:
                    case Gdk.Key.D:
                        InsertTimestamp();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.f:
                    case Gdk.Key.F:
                        ToggleInNoteSearchBar();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.plus:
                    case Gdk.Key.equal:
                    case Gdk.Key.KP_Add:
                        IncreaseFontSize();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.minus:
                    case Gdk.Key.KP_Subtract:
                        DecreaseFontSize();
                        args.RetVal = true;
                        break;
                    case Gdk.Key.Key_0:
                        ResetFontSizeToNormal();
                        args.RetVal = true;
                        break;
                }
            }
            else if (alt)
            {
                if (ev.Key == Gdk.Key.Right)
                {
                    ChangeCursorDepth(true);
                    args.RetVal = true;
                }
                else if (ev.Key == Gdk.Key.Left)
                {
                    ChangeCursorDepth(false);
                    args.RetVal = true;
                }
            }
        }

        #region Font Size Management (Exact Step Hierarchy from Tomboy NoteWindow.cs)

        public bool IsTagActive(string tagName)
        {
            TextBuffer buf = textView.Buffer;
            TextTag tag = buf.TagTable.Lookup(tagName);
            if (tag == null) return false;

            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                if (XmlNoteBufferBinder.FindDepthTag(start) != null && start.LineOffset < 2)
                {
                    start.LineOffset = 2;
                }
                return start.HasTag(tag) || start.BeginsTag(tag);
            }
            else
            {
                TextIter iter = buf.GetIterAtMark(buf.InsertMark);
                return iter.HasTag(tag) || (iter.BackwardChar() && iter.HasTag(tag));
            }
        }

        public void ApplyTagToSelectionOrCursor(string tagName)
        {
            TextBuffer buf = textView.Buffer;
            XmlNoteBufferBinder.EnsureCommonTags(buf);
            TextTag tag = buf.TagTable.Lookup(tagName);
            if (tag == null) return;

            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                buf.ApplyTag(tag, start, end);
            }
            else
            {
                TextIter lineStart = buf.GetIterAtMark(buf.InsertMark);
                lineStart.LineOffset = 0;
                TextIter lineEnd = lineStart;
                lineEnd.ForwardToLineEnd();
                if (lineStart.Offset != lineEnd.Offset)
                {
                    buf.ApplyTag(tag, lineStart, lineEnd);
                }
            }
        }

        public void RemoveTagFromSelectionOrCursor(string tagName)
        {
            TextBuffer buf = textView.Buffer;
            XmlNoteBufferBinder.EnsureCommonTags(buf);
            TextTag tag = buf.TagTable.Lookup(tagName);
            if (tag == null) return;

            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                buf.RemoveTag(tag, start, end);
            }
            else
            {
                TextIter lineStart = buf.GetIterAtMark(buf.InsertMark);
                lineStart.LineOffset = 0;
                TextIter lineEnd = lineStart;
                lineEnd.ForwardToLineEnd();
                buf.RemoveTag(tag, lineStart, lineEnd);
            }
        }

        public void IncreaseFontSize()
        {
            if (IsTagActive("size:small"))
            {
                RemoveTagFromSelectionOrCursor("size:small");
            }
            else if (IsTagActive("size:large"))
            {
                RemoveTagFromSelectionOrCursor("size:large");
                ApplyTagToSelectionOrCursor("size:huge");
            }
            else if (IsTagActive("size:huge"))
            {
                // Maximum size reached, do nothing
            }
            else
            {
                ApplyTagToSelectionOrCursor("size:large");
            }
            QueueSave(500);
        }

        public void DecreaseFontSize()
        {
            if (IsTagActive("size:small"))
            {
                // Minimum size reached, do nothing
            }
            else if (IsTagActive("size:large"))
            {
                RemoveTagFromSelectionOrCursor("size:large");
            }
            else if (IsTagActive("size:huge"))
            {
                RemoveTagFromSelectionOrCursor("size:huge");
                ApplyTagToSelectionOrCursor("size:large");
            }
            else
            {
                ApplyTagToSelectionOrCursor("size:small");
            }
            QueueSave(500);
        }

        public void SetFontSize(string sizeTag)
        {
            RemoveTagFromSelectionOrCursor("size:huge");
            RemoveTagFromSelectionOrCursor("size:large");
            RemoveTagFromSelectionOrCursor("size:small");

            if (!string.IsNullOrEmpty(sizeTag) && sizeTag != "normal")
            {
                ApplyTagToSelectionOrCursor(sizeTag);
            }
            QueueSave(500);
        }

        public void ResetFontSizeToNormal()
        {
            SetFontSize("normal");
        }

        #endregion

        #region Indentation & Bullet Lists Methods

        public void InsertBullet(ref TextIter iter, int depth, Pango.Direction direction)
        {
            TextBuffer buf = textView.Buffer;
            TextTag tag = XmlNoteBufferBinder.GetOrCreateDepthTag(buf, depth, direction);
            string bullet = IndentBullets[depth % IndentBullets.Length] + " ";
            buf.InsertWithTags(ref iter, bullet, tag);
        }

        public void IncreaseDepth(ref TextIter start)
        {
            TextBuffer buf = textView.Buffer;
            start = buf.GetIterAtLineOffset(start.Line, 0);

            TextIter lineEnd = start;
            lineEnd.ForwardToLineEnd();

            TextIter end = (lineEnd.LineOffset < 2 || start.EndsLine()) ? start : buf.GetIterAtLineOffset(start.Line, 2);

            DepthInfo? currDepth = XmlNoteBufferBinder.FindDepthTag(start);

            if (currDepth == null)
            {
                Pango.Direction direction = Pango.Direction.Ltr;
                if (start.Char.Length > 0 && !start.EndsLine())
                    direction = Pango.Global.UnicharDirection(start.Char[0]);

                InsertBullet(ref start, 0, direction);
            }
            else
            {
                string prefix = buf.GetText(start, end, false);
                if (IsBulletPrefix(prefix))
                {
                    buf.Delete(ref start, ref end);
                }
                int nextDepth = currDepth.Depth + 1;
                InsertBullet(ref start, nextDepth, currDepth.Direction);
            }
            QueueSave(500);
        }

        public void DecreaseDepth(ref TextIter start)
        {
            TextBuffer buf = textView.Buffer;
            start = buf.GetIterAtLineOffset(start.Line, 0);

            TextIter lineEnd = start;
            lineEnd.ForwardToLineEnd();

            TextIter end = (lineEnd.LineOffset < 2 || start.EndsLine()) ? start : buf.GetIterAtLineOffset(start.Line, 2);

            DepthInfo? currDepth = XmlNoteBufferBinder.FindDepthTag(start);

            if (currDepth != null)
            {
                string prefix = buf.GetText(start, end, false);
                if (IsBulletPrefix(prefix))
                {
                    buf.Delete(ref start, ref end);

                    int nextDepth = currDepth.Depth - 1;
                    if (nextDepth != -1)
                    {
                        InsertBullet(ref start, nextDepth, currDepth.Direction);
                    }
                }
                else
                {
                    buf.RemoveTag(currDepth.Tag, start, lineEnd);
                }
            }
            else
            {
                string prefix = buf.GetText(start, end, false);
                if (IsBulletPrefix(prefix))
                {
                    buf.Delete(ref start, ref end);
                }
            }
            QueueSave(500);
        }

        public void ChangeCursorDepth(bool increase)
        {
            TextBuffer buf = textView.Buffer;
            bool hasSelection = buf.GetSelectionBounds(out TextIter start, out TextIter end);

            int startLine = start.Line;
            int endLine = end.Line;

            if (hasSelection && end.LineOffset == 0 && endLine > startLine)
            {
                endLine--;
            }

            for (int i = startLine; i <= endLine; i++)
            {
                TextIter currLine = buf.GetIterAtLine(i);
                if (increase)
                    IncreaseDepth(ref currLine);
                else
                    DecreaseDepth(ref currLine);
            }
        }

        public void ToggleSelectionBullets()
        {
            TextBuffer buf = textView.Buffer;
            bool hasSelection = buf.GetSelectionBounds(out TextIter start, out TextIter end);

            start = buf.GetIterAtLineOffset(start.Line, 0);
            bool toggleOn = XmlNoteBufferBinder.FindDepthTag(start) == null;

            int startLine = start.Line;
            int endLine = end.Line;

            if (hasSelection && end.LineOffset == 0 && endLine > startLine)
            {
                endLine--;
            }

            for (int i = startLine; i <= endLine; i++)
            {
                TextIter currLine = buf.GetIterAtLine(i);
                if (toggleOn && XmlNoteBufferBinder.FindDepthTag(currLine) == null)
                {
                    IncreaseDepth(ref currLine);
                }
                else if (!toggleOn && XmlNoteBufferBinder.FindDepthTag(currLine) != null)
                {
                    DecreaseDepth(ref currLine);
                }
            }
            QueueSave(500);
        }

        public bool AddTab()
        {
            TextBuffer buf = textView.Buffer;
            TextIter iter = buf.GetIterAtMark(buf.InsertMark);
            iter.LineOffset = 0;

            DepthInfo? depth = XmlNoteBufferBinder.FindDepthTag(iter);
            if (depth != null)
            {
                ChangeCursorDepth(true);
                return true;
            }
            return false;
        }

        public bool RemoveTab()
        {
            TextBuffer buf = textView.Buffer;
            TextIter iter = buf.GetIterAtMark(buf.InsertMark);
            iter.LineOffset = 0;

            DepthInfo? depth = XmlNoteBufferBinder.FindDepthTag(iter);
            if (depth != null)
            {
                ChangeCursorDepth(false);
                return true;
            }
            return false;
        }

        public bool AddNewline(bool softBreak)
        {
            TextBuffer buf = textView.Buffer;
            TextMark insertMark = buf.InsertMark;
            TextIter iter = buf.GetIterAtMark(insertMark);
            iter.LineOffset = 0;

            DepthInfo? prevDepth = XmlNoteBufferBinder.FindDepthTag(iter);
            TextIter insert = buf.GetIterAtMark(insertMark);

            // Soft line break (\u2028)
            if (prevDepth != null && softBreak)
            {
                buf.Insert(ref insert, "\u2028");
                return true;
            }
            else if (prevDepth != null)
            {
                iter.ForwardChar();

                // Blank line with only bullet -> clear bullet and return to normal text
                if (iter.EndsLine() || insert.LineOffset < 3)
                {
                    TextIter start = buf.GetIterAtLine(iter.Line);
                    TextIter lineEnd = start;
                    lineEnd.ForwardToLineEnd();
                    TextIter end = (lineEnd.LineOffset < 2 || start.EndsLine()) ? start : buf.GetIterAtLineOffset(iter.Line, 2);

                    string prefix = buf.GetText(start, end, false);
                    if (IsBulletPrefix(prefix))
                    {
                        buf.Delete(ref start, ref end);
                    }
                    buf.RemoveTag(prevDepth.Tag, start, lineEnd);

                    iter = buf.GetIterAtMark(insertMark);
                    buf.Insert(ref iter, "\n");
                }
                else
                {
                    iter = buf.GetIterAtMark(insertMark);
                    buf.Insert(ref iter, "\n");

                    iter = buf.GetIterAtMark(insertMark);
                    TextIter start = buf.GetIterAtLine(iter.Line);

                    Pango.Direction direction = prevDepth.Direction;
                    if (iter.Char != "\n" && iter.Char.Length > 0)
                        direction = Pango.Global.UnicharDirection(iter.Char[0]);

                    InsertBullet(ref start, prevDepth.Depth, direction);
                }
                QueueSave(500);
                return true;
            }
            else if (LineNeedsBullet(iter))
            {
                TextIter start = buf.GetIterAtLineOffset(iter.Line, 0);
                TextIter end = buf.GetIterAtLineOffset(iter.Line, 0);

                while (end.Char == " ") end.ForwardChar();
                end.ForwardChars(2);

                buf.Delete(ref start, ref end);
                IncreaseDepth(ref start);

                iter = buf.GetIterAtMark(insertMark);
                buf.Insert(ref iter, "\n");

                iter = buf.GetIterAtMark(insertMark);
                iter.LineOffset = 0;
                InsertBullet(ref iter, 0, Pango.Direction.Ltr);

                QueueSave(500);
                return true;
            }

            return false;
        }

        private bool LineNeedsBullet(TextIter iter)
        {
            while (!iter.EndsLine())
            {
                switch (iter.Char)
                {
                    case " ":
                        iter.ForwardChar();
                        break;
                    case "*":
                    case "-":
                        TextIter next = iter;
                        next.ForwardChar();
                        return next.Char == " ";
                    default:
                        return false;
                }
            }
            return false;
        }

        public bool BackspaceKeyHandler()
        {
            TextBuffer buf = textView.Buffer;
            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                buf.Delete(ref start, ref end);
                QueueSave(500);
                return true;
            }

            TextIter iter = buf.GetIterAtMark(buf.InsertMark);
            if (iter.LineOffset <= 2)
            {
                TextIter lineStart = buf.GetIterAtLine(iter.Line);
                DepthInfo? depth = XmlNoteBufferBinder.FindDepthTag(lineStart);
                if (depth != null)
                {
                    DecreaseDepth(ref lineStart);
                    return true;
                }
            }

            return false;
        }

        public bool DeleteKeyHandler()
        {
            TextBuffer buf = textView.Buffer;
            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                buf.Delete(ref start, ref end);
                QueueSave(500);
                return true;
            }

            TextIter iter = buf.GetIterAtMark(buf.InsertMark);
            if (iter.EndsLine() && iter.Line < buf.LineCount - 1)
            {
                TextIter nextLine = buf.GetIterAtLine(iter.Line + 1);
                DepthInfo? depth = XmlNoteBufferBinder.FindDepthTag(nextLine);
                if (depth != null)
                {
                    TextIter endLine = nextLine;
                    endLine.ForwardChars(2);
                    buf.Delete(ref nextLine, ref endLine);
                    QueueSave(500);
                    return true;
                }
            }

            return false;
        }

        private static bool IsBulletPrefix(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2) return false;
            char first = text[0];
            return (first == '•' || first == '◦' || first == '‣') && text[1] == ' ';
        }

        #endregion

        /// <summary>
        /// Inserts current timestamp into the note using the <datetime> XML tag.
        /// </summary>
        private void InsertTimestamp()
        {
            if (textView == null || textView.Handle == IntPtr.Zero || textView.Buffer == null || textView.Buffer.Handle == IntPtr.Zero) return;

            try
            {
                string fmt = Preferences.Current.CustomTimestampFormat;
                if (string.IsNullOrWhiteSpace(fmt)) fmt = "dddd, MMMM d, yyyy h:mm tt";

                string timeText;
                try
                {
                    timeText = DateTime.Now.ToString(fmt);
                }
                catch
                {
                    timeText = DateTime.Now.ToString("dddd, MMMM d, yyyy h:mm tt");
                }

                TextBuffer buf = textView.Buffer;
                XmlNoteBufferBinder.EnsureCommonTags(buf);
                TextTag datetimeTag = buf.TagTable.Lookup("datetime") ?? new TextTag("datetime")
                {
                    Scale = 0.85,
                    Style = Pango.Style.Italic,
                    Foreground = "#777777"
                };

                TextIter iter = buf.GetIterAtMark(buf.InsertMark);

                if (!iter.StartsLine())
                {
                    buf.Insert(ref iter, "\n");
                }
                buf.InsertWithTags(ref iter, timeText, datetimeTag);
                buf.Insert(ref iter, "\n");

                QueueSave(300);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting timestamp: {ex.Message}");
            }
        }

        private Menu CreateTextFormattingMenu()
        {
            var menu = new Menu();

            var undoItem = new MenuItem("↩ 復原 (Ctrl+Z)");
            undoItem.Activated += (s, e) => undoManager.Undo();
            menu.Append(undoItem);

            var redoItem = new MenuItem("↪ 重做 (Ctrl+Shift+Z)");
            redoItem.Activated += (s, e) => undoManager.Redo();
            menu.Append(redoItem);

            menu.Append(new SeparatorMenuItem());

            undoManager.UndoChanged += (s, e) => {
                undoItem.Sensitive = undoManager.CanUndo;
                redoItem.Sensitive = undoManager.CanRedo;
            };
            undoItem.Sensitive = undoManager.CanUndo;
            redoItem.Sensitive = undoManager.CanRedo;

            var boldItem = new MenuItem("<b>Bold</b> (Ctrl+B)");
            ((Label)boldItem.Child).UseMarkup = true;
            boldItem.Activated += (s, e) => ToggleTagOnSelection("bold");
            menu.Append(boldItem);

            var italicItem = new MenuItem("<i>Italic</i> (Ctrl+I)");
            ((Label)italicItem.Child).UseMarkup = true;
            italicItem.Activated += (s, e) => ToggleTagOnSelection("italic");
            menu.Append(italicItem);

            var underlineItem = new MenuItem("<u>Underline</u> (Ctrl+U)");
            ((Label)underlineItem.Child).UseMarkup = true;
            underlineItem.Activated += (s, e) => ToggleTagOnSelection("underline");
            menu.Append(underlineItem);

            var strikeItem = new MenuItem("<s>Strikethrough</s> (Ctrl+S)");
            ((Label)strikeItem.Child).UseMarkup = true;
            strikeItem.Activated += (s, e) => ToggleTagOnSelection("strikethrough");
            menu.Append(strikeItem);

            var highlightItem = new MenuItem("🟨 Highlight (Ctrl+H)");
            highlightItem.Activated += (s, e) => ToggleTagOnSelection("highlight");
            menu.Append(highlightItem);

            var monoItem = new MenuItem("<tt>Fixed Width</tt>");
            ((Label)monoItem.Child).UseMarkup = true;
            monoItem.Activated += (s, e) => ToggleTagOnSelection("monospace");
            menu.Append(monoItem);

            menu.Append(new SeparatorMenuItem());

            var hugeItem = new MenuItem("<span size='large'>Huge</span> (Ctrl++)");
            ((Label)hugeItem.Child).UseMarkup = true;
            hugeItem.Activated += (s, e) => SetFontSize("size:huge");
            menu.Append(hugeItem);

            var largeItem = new MenuItem("Large");
            largeItem.Activated += (s, e) => SetFontSize("size:large");
            menu.Append(largeItem);

            var smallItem = new MenuItem("<span size='small'>Small</span> (Ctrl+-)");
            ((Label)smallItem.Child).UseMarkup = true;
            smallItem.Activated += (s, e) => SetFontSize("size:small");
            menu.Append(smallItem);

            var normalItem = new MenuItem("Normal Size (Ctrl+0)");
            normalItem.Activated += (s, e) => ResetFontSizeToNormal();
            menu.Append(normalItem);

            menu.Append(new SeparatorMenuItem());

            var bulletItem = new MenuItem("• Bulleted List");
            bulletItem.Activated += (s, e) => ToggleSelectionBullets();
            menu.Append(bulletItem);

            var indentItem = new MenuItem("➔ Increase Indent (Alt+Right)");
            indentItem.Activated += (s, e) => ChangeCursorDepth(true);
            menu.Append(indentItem);

            var outdentItem = new MenuItem("⬅ Decrease Indent (Alt+Left)");
            outdentItem.Activated += (s, e) => ChangeCursorDepth(false);
            menu.Append(outdentItem);

            menu.Append(new SeparatorMenuItem());

            var findItem = new MenuItem("🔍 Find in This Note (Ctrl+F)");
            findItem.Activated += (s, e) => ToggleInNoteSearchBar();
            menu.Append(findItem);

            menu.ShowAll();
            return menu;
        }

        private Menu CreateToolsMenu()
        {
            var menu = new Menu();

            var dateItem = new MenuItem("📅 Insert Timestamp (Ctrl+D)");
            dateItem.Activated += (s, e) => InsertTimestamp();
            menu.Append(dateItem);

            var exportHtmlItem = new MenuItem("🌐 輸出成 HTML (Export to HTML)...");
            exportHtmlItem.Activated += (s, e) => {
                SaveNote();
                HtmlExportService.PromptExportNote(note, this);
            };
            menu.Append(exportHtmlItem);

            var prefItem = new MenuItem("⚙ Preferences...");
            prefItem.Activated += (s, e) => new PreferencesWindow(this).ShowAll();
            menu.Append(prefItem);

            var infoItem = new MenuItem("ℹ Note Information");
            infoItem.Activated += (s, e) => {
                string safeTitle = GLib.Markup.EscapeText(note.Title);
                string safeNb = GLib.Markup.EscapeText(string.IsNullOrEmpty(note.Notebook) ? "None" : note.Notebook);
                string safePath = GLib.Markup.EscapeText(note.FilePath);
                var dlg = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                    $"Title: {safeTitle}\nNotebook: {safeNb}\nCreated: {note.CreateDate}\nLast Modified: {note.LastChangeDate}\nFile: {safePath}");
                dlg.Run();
                dlg.Hide();
                dlg.Dispose();
            };
            menu.Append(infoItem);

            menu.ShowAll();
            return menu;
        }

        private void SaveNote()
        {
            if (isClosed || note == null || isSaving || isDeleting) return;
            if (textView == null || textView.Handle == IntPtr.Zero || textView.Buffer == null || textView.Buffer.Handle == IntPtr.Zero) return;

            isSaving = true;
            try
            {
                TextBuffer buf = textView.Buffer;
                TextIter firstLineStart = buf.StartIter;
                TextIter firstLineEnd = firstLineStart;
                firstLineEnd.ForwardToLineEnd();
                string extractedTitle = buf.GetText(firstLineStart, firstLineEnd, false).Trim();

                if (!string.IsNullOrWhiteSpace(extractedTitle))
                {
                    note.Title = extractedTitle;
                    Title = extractedTitle;
                }

                // Window dimensions and position
                GetSize(out int w, out int h);
                if (note.Width != w || note.Height != h)
                {
                    note.Width = w;
                    note.Height = h;
                    isMetadataChanged = true;
                }

                GetPosition(out int x, out int y);
                if (note.X != x || note.Y != y)
                {
                    note.X = x;
                    note.Y = y;
                    isMetadataChanged = true;
                }

                // Cursor and Selection
                TextIter insertIter = buf.GetIterAtMark(buf.InsertMark);
                if (note.CursorPosition != insertIter.Offset)
                {
                    note.CursorPosition = insertIter.Offset;
                    isMetadataChanged = true;
                }

                TextIter selBoundIter = buf.GetIterAtMark(buf.SelectionBound);
                if (note.SelectionBoundPosition != selBoundIter.Offset)
                {
                    note.SelectionBoundPosition = selBoundIter.Offset;
                    isMetadataChanged = true;
                }

                // Synchronize Notebook
                string activeNb = notebookCombo.ActiveText;
                if (!string.IsNullOrEmpty(activeNb) && activeNb != note.Notebook)
                {
                    note.SetNotebook(activeNb);
                    isMetadataChanged = true;
                }

                if (isContentChanged)
                {
                    note.XmlContent = XmlNoteBufferBinder.ExportBufferToXml(buf, note.Title);
                    note.LastChangeDate = DateTime.Now;
                    note.LastMetadataChangeDate = DateTime.Now;
                }
                else if (isMetadataChanged)
                {
                    note.LastMetadataChangeDate = DateTime.Now;
                }
                else
                {
                    // Neither content nor metadata changed! Do not overwrite LastChangeDate or touch file
                    return;
                }

                note.Save(NoteStorage.NoteDirectory);
                isContentChanged = false;
                isMetadataChanged = false;
                onSaveCallback?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving note: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        protected override bool OnDeleteEvent(Gdk.Event ev)
        {
            if (saveTimeoutId != 0)
            {
                GLib.Source.Remove(saveTimeoutId);
                saveTimeoutId = 0;
            }
            if (urlHighlightTimeoutId != 0)
            {
                GLib.Source.Remove(urlHighlightTimeoutId);
                urlHighlightTimeoutId = 0;
            }

            if (!isDeleting && isContentChanged)
            {
                SaveNote();
            }

            isClosed = true;

            latexSession?.Dispose();
            latexSession = null;

            Preferences.SettingChanged -= OnPreferencesChanged;

            Hide();
            Dispose();
            return true;
        }

        protected override void OnDestroyed()
        {
            isClosed = true;
            if (saveTimeoutId != 0)
            {
                GLib.Source.Remove(saveTimeoutId);
                saveTimeoutId = 0;
            }
            if (urlHighlightTimeoutId != 0)
            {
                GLib.Source.Remove(urlHighlightTimeoutId);
                urlHighlightTimeoutId = 0;
            }
            ClearSearchHighlights();
            latexSession?.Dispose();
            latexSession = null;
            Preferences.SettingChanged -= OnPreferencesChanged;
            base.OnDestroyed();
        }

        private void OnPreferencesChanged()
        {
            ApplyCustomPreferences();
            InitLatexSession();
        }

        private void InitLatexSession()
        {
            if (Preferences.Current.EnableLatexMath && Tomboy.Latex.LatexManager.IsLatexAvailable())
            {
                latexSession ??= new Tomboy.Latex.LatexNoteSession(
                    textView,
                    freezeUndo: () => undoManager.FreezeUndo(),
                    thawUndo: () => undoManager.ThawUndo(),
                    getHeader: () => Preferences.Current.LatexHeader,
                    getFooter: () => Preferences.Current.LatexFooter,
                    isDollarEnabled: () => Preferences.Current.LatexDollarEnabled
                );
                latexSession.Initialize();
            }
            else
            {
                latexSession?.Dispose();
                latexSession = null;
            }
        }

        private void ApplyCustomPreferences()
        {
            if (textView == null || textView.Handle == IntPtr.Zero) return;
            var cfg = Preferences.Current;
            if (cfg.EnableCustomFont && !string.IsNullOrWhiteSpace(cfg.CustomFontFace))
            {
                try
                {
                    textView.OverrideFont(Pango.FontDescription.FromString(cfg.CustomFontFace));
                }
                catch { }
            }
            else
            {
                textView.OverrideFont(null);
            }
        }

        private void ToggleTagOnSelection(string tagName)
        {
            if (textView == null || textView.Handle == IntPtr.Zero || textView.Buffer == null || textView.Buffer.Handle == IntPtr.Zero) return;

            TextBuffer buf = textView.Buffer;
            XmlNoteBufferBinder.EnsureCommonTags(buf);
            TextTag tag = buf.TagTable.Lookup(tagName);
            if (tag == null) return;

            if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
            {
                bool hasTag = start.HasTag(tag);
                if (hasTag)
                {
                    buf.RemoveTag(tag, start, end);
                }
                else
                {
                    buf.ApplyTag(tag, start, end);
                }
            }
            QueueSave(500);
        }

        public void UpdateNotebookDisplay(string? notebookName)
        {
            if (notebookCombo == null || notebookCombo.Handle == IntPtr.Zero) return;

            string target = string.IsNullOrWhiteSpace(notebookName) ? "(None)" : notebookName.Trim();
            ITreeModel model = notebookCombo.Model;
            TreeIter iter = TreeIter.Zero;
            if (model != null && model.GetIterFirst(out iter))
            {
                int index = 0;
                do
                {
                    string text = (string)model.GetValue(iter, 0);
                    if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase))
                    {
                        notebookCombo.Active = index;
                        note.Notebook = string.Equals(target, "(None)", StringComparison.OrdinalIgnoreCase) ? string.Empty : target;
                        return;
                    }
                    index++;
                } while (model.IterNext(ref iter));
            }
        }
    }
}
