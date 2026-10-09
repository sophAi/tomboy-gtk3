using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gdk;
using Gtk;
using Tomboy.Models;
using Tomboy.Services;

namespace Tomboy.Views
{
    public class PreferencesWindow : Gtk.Window
    {
        // 1. 正在編輯 (Editing) Controls
        private CheckButton checkSpell = null!;
        private CheckButton checkWikiWords = null!;
        private CheckButton checkAutoBullets = null!;
        private CheckButton checkCustomFont = null!;
        private FontButton fontButton = null!;
        private CheckButton checkSearchColor = null!;
        private ColorButton searchColorButton = null!;
        private ColorButton titleColorButton = null!;
        private ComboBoxText comboRenameBehavior = null!;

        // 2. 快速鍵 (Hotkeys) Controls
        private CheckButton checkListenHotkeys = null!;
        private Entry entryShowMenu = null!;
        private Entry entryOpenStartHere = null!;
        private Entry entryCreateNewNote = null!;
        private Entry entryOpenSearch = null!;

        // 3. 同步化 (Synchronization) Controls
        private ComboBoxText comboSyncService = null!;
        private VBox syncPrefsContainer = null!;
        private Widget sshPrefsWidget = null!;
        private Widget localPrefsWidget = null!;
        private Entry entrySshServer = null!;
        private Entry entrySshUsername = null!;
        private Entry entrySshFolder = null!;
        private Entry entryLocalPath = null!;
        private CheckButton checkAutosync = null!;
        private SpinButton spinAutosync = null!;
        private Button btnSaveSync = null!;
        private Button btnClearSync = null!;
        private Button btnAdvancedSync = null!;

        // 4. 附加元件 (Add-ins) Controls
        private TreeView addinTreeView = null!;
        private ListStore addinListStore = null!;
        private Button btnEnableAddin = null!;
        private Button btnDisableAddin = null!;
        private Button btnAddinPrefs = null!;
        private Button btnAddinInfo = null!;

        // 5. 進階 (Advanced) Controls
        private SpinButton spinMenuMin = null!;
        private SpinButton spinMenuMax = null!;
        private CheckButton checkStartupNotes = null!;

        public PreferencesWindow(Gtk.Window? parent = null) : base(Gtk.WindowType.Toplevel)
        {
            Title = "Tomboy 偏好設定";
            Icon = IconService.GetIcon("tomboy", 48) ?? IconService.GetIcon("tomboy", 22);
            if (parent != null && parent.Handle != IntPtr.Zero)
            {
                TransientFor = parent;
            }
            SetDefaultSize(580, 520);
            SetPosition(WindowPosition.CenterOnParent);
            BorderWidth = 6;

            var mainVBox = new VBox(false, 8);

            // Notebook with 5 tabs
            var notebook = new Notebook();
            notebook.TabPos = PositionType.Top;

            notebook.AppendPage(CreateEditingTab(), new Label("正在編輯"));
            notebook.AppendPage(CreateHotkeysTab(), new Label("快速鍵"));
            notebook.AppendPage(CreateSyncTab(), new Label("同步化"));
            notebook.AppendPage(CreateAddinsTab(), new Label("附加元件"));
            notebook.AppendPage(CreateAdvancedTab(), new Label("進階"));

            mainVBox.PackStart(notebook, true, true, 0);

            // Bottom Button Box with Close button
            var bottomBox = new HButtonBox();
            bottomBox.LayoutStyle = ButtonBoxStyle.End;
            var closeBtn = new Button(Stock.Close);
            closeBtn.CanDefault = true;
            closeBtn.Clicked += (s, e) => Destroy();
            bottomBox.PackStart(closeBtn, false, false, 0);

            var accelGroup = new AccelGroup();
            AddAccelGroup(accelGroup);
            closeBtn.AddAccelerator("activate", accelGroup, (uint)Gdk.Key.Escape, 0, 0);

            mainVBox.PackEnd(bottomBox, false, false, 0);

            Add(mainVBox);
            ShowAll();

            // Refresh dynamic states after window shown
            UpdateSyncControlsState();
            UpdateAddinButtons();
        }

        #region Page 1: 正在編輯 (Editing)
        private Widget CreateEditingTab()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 10) { BorderWidth = 12 };

            // 1. Spell Checking
            checkSpell = new CheckButton("拼字隨打即查(_S)") { Active = cfg.EnableSpellchecking };
            checkSpell.Toggled += (s, e) => {
                var c = Preferences.Current;
                c.EnableSpellchecking = checkSpell.Active;
                Preferences.Current = c;
            };
            vbox.PackStart(checkSpell, false, false, 0);

            var spellTip = new Label("<span size='small' foreground='#555555'>拼字有錯誤的地方會被劃上紅色底線，在上面按下滑鼠右鍵時將出現正確拼字的建議。</span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(spellTip, false, false, 0);

            // 2. WikiWords
            checkWikiWords = new CheckButton("將 _WikiWord 加強顯示") { Active = cfg.EnableWikiwords };
            checkWikiWords.Toggled += (s, e) => {
                var c = Preferences.Current;
                c.EnableWikiwords = checkWikiWords.Active;
                Preferences.Current = c;
            };
            vbox.PackStart(checkWikiWords, false, false, 0);

            var wikiTip = new Label("<span size='small' foreground='#555555'>啟用這個選項來加強顯示像 <b>ThatLookLikeThis</b> 這種將兩個或以上英文字合併的字。點選這個字就會加一頁新筆記，以這個字命名。</span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(wikiTip, false, false, 0);

            // 3. Auto-bulleted lists
            checkAutoBullets = new CheckButton("啟用自動項目清單(_B)") { Active = cfg.EnableAutoBulletedLists };
            checkAutoBullets.Toggled += (s, e) => {
                var c = Preferences.Current;
                c.EnableAutoBulletedLists = checkAutoBullets.Active;
                Preferences.Current = c;
            };
            vbox.PackStart(checkAutoBullets, false, false, 0);

            // 4. Custom font
            var fontHBox = new HBox(false, 8);
            checkCustomFont = new CheckButton("使用自選字型(_F)") { Active = cfg.EnableCustomFont };
            fontButton = new FontButton(cfg.CustomFontFace) { Sensitive = checkCustomFont.Active };

            checkCustomFont.Toggled += (s, e) => {
                fontButton.Sensitive = checkCustomFont.Active;
                var c = Preferences.Current;
                c.EnableCustomFont = checkCustomFont.Active;
                Preferences.Current = c;
            };
            fontButton.FontSet += (s, e) => {
                var c = Preferences.Current;
                c.CustomFontFace = fontButton.FontName;
                Preferences.Current = c;
            };

            fontHBox.PackStart(checkCustomFont, false, false, 0);
            fontHBox.PackStart(fontButton, true, true, 0);
            vbox.PackStart(fontHBox, false, false, 0);

            // 5. Custom search match highlight color
            var colorHBox = new HBox(false, 8);
            checkSearchColor = new CheckButton("使用自選搜尋相符色彩(_C)") { Active = cfg.EnableCustomSearchMatchColor };
            searchColorButton = new ColorButton();
            try
            {
                var gdkRgba = new RGBA();
                if (gdkRgba.Parse(cfg.CustomSearchMatchColor))
                {
                    searchColorButton.Rgba = gdkRgba;
                }
            }
            catch { }
            searchColorButton.Sensitive = checkSearchColor.Active;

            checkSearchColor.Toggled += (s, e) => {
                searchColorButton.Sensitive = checkSearchColor.Active;
                var c = Preferences.Current;
                c.EnableCustomSearchMatchColor = checkSearchColor.Active;
                Preferences.Current = c;
            };
            searchColorButton.ColorSet += (s, e) => {
                var rgba = searchColorButton.Rgba;
                string hex = $"#{(int)(rgba.Red * 255):x2}{(int)(rgba.Green * 255):x2}{(int)(rgba.Blue * 255):x2}";
                var c = Preferences.Current;
                c.CustomSearchMatchColor = hex;
                Preferences.Current = c;
            };

            colorHBox.PackStart(checkSearchColor, false, false, 0);
            colorHBox.PackStart(searchColorButton, false, false, 0);
            vbox.PackStart(colorHBox, false, false, 0);

            // 5b. Custom note title color
            var titleColorHBox = new HBox(false, 8);
            var titleColorLabel = new Label("筆記標題文字色彩(_T)：") { Xalign = 0 };
            titleColorButton = new ColorButton();
            try
            {
                var gdkRgba = new RGBA();
                if (gdkRgba.Parse(!string.IsNullOrWhiteSpace(cfg.NoteTitleColor) ? cfg.NoteTitleColor : "#005A9E"))
                {
                    titleColorButton.Rgba = gdkRgba;
                }
            }
            catch { }

            titleColorButton.ColorSet += (s, e) => {
                var rgba = titleColorButton.Rgba;
                string hex = $"#{(int)(rgba.Red * 255):x2}{(int)(rgba.Green * 255):x2}{(int)(rgba.Blue * 255):x2}";
                var c = Preferences.Current;
                c.NoteTitleColor = hex;
                Preferences.Current = c;
            };

            titleColorHBox.PackStart(titleColorLabel, false, false, 0);
            titleColorHBox.PackStart(titleColorButton, false, false, 0);
            vbox.PackStart(titleColorHBox, false, false, 0);

            // 6. Note renaming behavior
            var renameHBox = new HBox(false, 8);
            var renameLabel = new Label("當重新命名已連結的筆記時：") { Xalign = 0 };
            comboRenameBehavior = new ComboBoxText();
            comboRenameBehavior.AppendText("詢問我要怎麼做");
            comboRenameBehavior.AppendText("永不重新命名連結");
            comboRenameBehavior.AppendText("永遠重新命名連結");
            comboRenameBehavior.Active = Math.Clamp(cfg.NoteRenameBehavior, 0, 2);
            comboRenameBehavior.Changed += (s, e) => {
                var c = Preferences.Current;
                c.NoteRenameBehavior = comboRenameBehavior.Active;
                Preferences.Current = c;
            };

            renameHBox.PackStart(renameLabel, false, false, 0);
            renameHBox.PackStart(comboRenameBehavior, true, true, 0);
            vbox.PackStart(renameHBox, false, false, 0);

            // 7. New Note Template
            vbox.PackStart(new HSeparator(), false, false, 4);

            var tmplLabel = new Label("<b>新增筆記範本</b>") { UseMarkup = true, Xalign = 0 };
            vbox.PackStart(tmplLabel, false, false, 0);

            var tmplTip = new Label("<span size='small' foreground='#555555'>使用新增筆記範本以指定在建立一個新的筆記時所用的文字。</span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(tmplTip, false, false, 0);

            var tmplBtnBox = new HBox(false, 0);
            var openTmplBtn = new Button("開啟新增筆記範本");
            openTmplBtn.Clicked += OnOpenTemplateNoteClicked;
            tmplBtnBox.PackStart(openTmplBtn, false, false, 0);
            vbox.PackStart(tmplBtnBox, false, false, 0);

            return vbox;
        }

        private void OnOpenTemplateNoteClicked(object? sender, EventArgs e)
        {
            var notes = NoteStorage.LoadAllNotes();
            var tmplNote = notes.FirstOrDefault(n => n.Title == "New Note Template" || n.Title == "新增筆記範本" || n.Tags.Contains("system:template"));
            if (tmplNote == null)
            {
                tmplNote = new NoteItem
                {
                    Title = "新增筆記範本",
                    XmlContent = "<note-content version=\"0.1\" xmlns=\"http://beatniksoftware.com/tomboy\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\" xml:space=\"preserve\"><note-title>新增筆記範本</note-title>\n\n請在此處描述您的新增筆記範本內容。\n</note-content>"
                };
                tmplNote.Tags.Add("system:template");
                tmplNote.Save(NoteStorage.NoteDirectory);
            }

            var win = new NoteWindow(tmplNote, new List<string>());
            win.ShowAll();
        }
        #endregion

        #region Page 2: 快速鍵 (Hotkeys)
        private Widget CreateHotkeysTab()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 12) { BorderWidth = 12 };

            checkListenHotkeys = new CheckButton("啟用快速鍵(_H)") { Active = cfg.EnableKeybindings };
            vbox.PackStart(checkListenHotkeys, false, false, 0);

            var hotkeysTip = new Label("<span size='small' foreground='#555555'>快速鍵允許您從任何地方以按鍵組合快速存取筆記頁，例如 <b>&lt;Control&gt;&lt;Shift&gt;F11</b> 或 <b>&lt;Alt&gt;N</b></span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(hotkeysTip, false, false, 0);

            var align = new Alignment(0.0f, 0.0f, 1.0f, 0.0f);
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };

            // Row 0: Show notes menu
            var lbl0 = new Label("顯示筆記本選單(_M):") { UseUnderline = true, Xalign = 0 };
            entryShowMenu = new Entry(cfg.KeybindingShowNoteMenu) { Sensitive = checkListenHotkeys.Active };
            lbl0.MnemonicWidget = entryShowMenu;
            grid.Attach(lbl0, 0, 0, 1, 1);
            grid.Attach(entryShowMenu, 1, 0, 1, 1);

            // Row 1: Open Start Here
            var lbl1 = new Label("開啟「起始頁面」(_S):") { UseUnderline = true, Xalign = 0 };
            entryOpenStartHere = new Entry(cfg.KeybindingOpenStartHere) { Sensitive = checkListenHotkeys.Active };
            lbl1.MnemonicWidget = entryOpenStartHere;
            grid.Attach(lbl1, 0, 1, 1, 1);
            grid.Attach(entryOpenStartHere, 1, 1, 1, 1);

            // Row 2: Create new note
            var lbl2 = new Label("新增筆記頁(_N):") { UseUnderline = true, Xalign = 0 };
            entryCreateNewNote = new Entry(cfg.KeybindingCreateNewNote) { Sensitive = checkListenHotkeys.Active };
            lbl2.MnemonicWidget = entryCreateNewNote;
            grid.Attach(lbl2, 0, 2, 1, 1);
            grid.Attach(entryCreateNewNote, 1, 2, 1, 1);

            // Row 3: Open Search All Notes
            var lbl3 = new Label("開啟「搜尋所有筆記(_A)」:") { UseUnderline = true, Xalign = 0 };
            entryOpenSearch = new Entry(cfg.KeybindingOpenRecentChanges) { Sensitive = checkListenHotkeys.Active };
            lbl3.MnemonicWidget = entryOpenSearch;
            grid.Attach(lbl3, 0, 3, 1, 1);
            grid.Attach(entryOpenSearch, 1, 3, 1, 1);

            checkListenHotkeys.Toggled += (s, e) => {
                bool active = checkListenHotkeys.Active;
                entryShowMenu.Sensitive = active;
                entryOpenStartHere.Sensitive = active;
                entryCreateNewNote.Sensitive = active;
                entryOpenSearch.Sensitive = active;

                var c = Preferences.Current;
                c.EnableKeybindings = active;
                Preferences.Current = c;
            };

            System.Action saveHotkeys = () => {
                var c = Preferences.Current;
                c.KeybindingShowNoteMenu = entryShowMenu.Text.Trim();
                c.KeybindingOpenStartHere = entryOpenStartHere.Text.Trim();
                c.KeybindingCreateNewNote = entryCreateNewNote.Text.Trim();
                c.KeybindingOpenRecentChanges = entryOpenSearch.Text.Trim();
                Preferences.Current = c;
            };

            entryShowMenu.Changed += (s, e) => saveHotkeys();
            entryOpenStartHere.Changed += (s, e) => saveHotkeys();
            entryCreateNewNote.Changed += (s, e) => saveHotkeys();
            entryOpenSearch.Changed += (s, e) => saveHotkeys();

            align.Add(grid);
            vbox.PackStart(align, false, false, 0);

            return vbox;
        }
        #endregion

        #region Page 3: 同步化 (Synchronization)
        private Widget CreateSyncTab()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 8) { BorderWidth = 12 };

            // 1. Service Selection ComboBox
            var serviceHBox = new HBox(false, 6);
            var serviceLabel = new Label("服務(_V):") { UseUnderline = true, Xalign = 0 };
            comboSyncService = new ComboBoxText();
            comboSyncService.Append("sshfs", "SSH");
            comboSyncService.Append("local", "本地端資料夾");
            serviceLabel.MnemonicWidget = comboSyncService;

            string selected = cfg.SyncSelectedServiceAddin;
            if (selected == "local") comboSyncService.ActiveId = "local";
            else comboSyncService.ActiveId = "sshfs";

            comboSyncService.Changed += OnSyncServiceChanged;

            serviceHBox.PackStart(serviceLabel, false, false, 0);
            serviceHBox.PackStart(comboSyncService, true, true, 0);
            vbox.PackStart(serviceHBox, false, false, 0);

            // 2. Dynamic Service Prefs Container
            syncPrefsContainer = new VBox(false, 6);

            // 2.1 SSH Prefs Widget
            sshPrefsWidget = CreateSshPrefsWidget();
            syncPrefsContainer.PackStart(sshPrefsWidget, true, true, 0);

            // 2.2 Local Prefs Widget
            localPrefsWidget = CreateLocalPrefsWidget();
            syncPrefsContainer.PackStart(localPrefsWidget, true, true, 0);

            vbox.PackStart(syncPrefsContainer, true, true, 4);

            // 3. Autosync Section
            var autoBox = new HBox(false, 6);
            checkAutosync = new CheckButton("自動在背景進行同步於每(_Y)") { Active = cfg.SyncAutosyncTimeout >= 5 };
            spinAutosync = new SpinButton(5, 1000, 1)
            {
                Value = cfg.SyncAutosyncTimeout >= 5 ? cfg.SyncAutosyncTimeout : 10,
                Sensitive = checkAutosync.Active
            };
            var minLabel = new Label("分鐘");

            System.Action updateAutosync = () => {
                var c = Preferences.Current;
                c.SyncAutosyncTimeout = checkAutosync.Active ? (int)spinAutosync.Value : -1;
                Preferences.Current = c;
            };

            checkAutosync.Toggled += (s, e) => {
                spinAutosync.Sensitive = checkAutosync.Active;
                updateAutosync();
            };
            spinAutosync.ValueChanged += (s, e) => updateAutosync();

            autoBox.PackStart(checkAutosync, false, false, 0);
            autoBox.PackStart(spinAutosync, false, false, 0);
            autoBox.PackStart(minLabel, false, false, 0);
            vbox.PackStart(autoBox, false, false, 4);

            // 4. Action Buttons Box
            var btnBox = new HButtonBox { LayoutStyle = ButtonBoxStyle.End, Spacing = 6 };

            btnAdvancedSync = new Button("進階(_A)…");
            btnAdvancedSync.Clicked += OnAdvancedSyncClicked;
            btnBox.PackStart(btnAdvancedSync, false, false, 0);
            btnBox.SetChildSecondary(btnAdvancedSync, true);

            btnClearSync = new Button(Stock.Clear) { Label = "清除" };
            btnClearSync.Clicked += OnClearSyncClicked;
            btnBox.PackStart(btnClearSync, false, false, 0);

            btnSaveSync = new Button(Stock.Save) { Label = "儲存" };
            btnSaveSync.Clicked += OnSaveSyncClicked;
            btnBox.PackStart(btnSaveSync, false, false, 0);

            vbox.PackEnd(btnBox, false, false, 0);

            return vbox;
        }

        private Widget CreateSshPrefsWidget()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 6);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 6 };

            // Server
            var lblServer = new Label("伺服器(_R):") { UseUnderline = true, Xalign = 0 };
            string serverDisplay = cfg.SyncSshfsServer;
            if (cfg.SyncSshfsPort > 0 && cfg.SyncSshfsPort != 22 && !serverDisplay.Contains(':'))
            {
                serverDisplay += $":{cfg.SyncSshfsPort}";
            }
            entrySshServer = new Entry(serverDisplay);
            lblServer.MnemonicWidget = entrySshServer;
            grid.Attach(lblServer, 0, 0, 1, 1);
            grid.Attach(entrySshServer, 1, 0, 1, 1);

            // Username
            var lblUser = new Label("使用者名稱(_N):") { UseUnderline = true, Xalign = 0 };
            entrySshUsername = new Entry(cfg.SyncSshfsUsername);
            lblUser.MnemonicWidget = entrySshUsername;
            grid.Attach(lblUser, 0, 1, 1, 1);
            grid.Attach(entrySshUsername, 1, 1, 1, 1);

            // Folder Path
            var lblFolder = new Label("資料夾路徑[選擇性的](_F):") { UseUnderline = true, Xalign = 0 };
            entrySshFolder = new Entry(cfg.SyncSshfsFolder);
            lblFolder.MnemonicWidget = entrySshFolder;
            grid.Attach(lblFolder, 0, 2, 1, 1);
            grid.Attach(entrySshFolder, 1, 2, 1, 1);

            entrySshServer.Changed += (s, e) => UpdateSyncControlsState();
            entrySshUsername.Changed += (s, e) => UpdateSyncControlsState();
            entrySshFolder.Changed += (s, e) => UpdateSyncControlsState();

            vbox.PackStart(grid, false, false, 0);

            // SSH key helper description
            var infoLabel = new Label("<span size='small' foreground='#555555'>SSH 同步化需要有這個伺服器及使用者的 SSH 金鑰，並加入已執行的 SSH 伺服程式。</span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(infoLabel, false, false, 4);

            return vbox;
        }

        private Widget CreateLocalPrefsWidget()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 6);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
            var lblPath = new Label("資料夾路徑(_F):") { UseUnderline = true, Xalign = 0 };
            entryLocalPath = new Entry(cfg.SyncLocalPath);
            lblPath.MnemonicWidget = entryLocalPath;

            var browseBtn = new Button("瀏覽…");
            browseBtn.Clicked += (s, e) => {
                var dlg = new FileChooserDialog("選擇同步資料夾", this, FileChooserAction.SelectFolder, "取消", ResponseType.Cancel, "確定", ResponseType.Accept);
                if (dlg.Run() == (int)ResponseType.Accept)
                {
                    entryLocalPath.Text = dlg.Filename;
                }
                dlg.Hide();
                dlg.Dispose();
            };

            var pathHBox = new HBox(false, 6);
            pathHBox.PackStart(entryLocalPath, true, true, 0);
            pathHBox.PackEnd(browseBtn, false, false, 0);

            grid.Attach(lblPath, 0, 0, 1, 1);
            grid.Attach(pathHBox, 1, 0, 1, 1);

            entryLocalPath.Changed += (s, e) => UpdateSyncControlsState();

            vbox.PackStart(grid, false, false, 0);
            return vbox;
        }

        private void OnSyncServiceChanged(object? sender, EventArgs e)
        {
            UpdateSyncControlsState();
        }

        private void UpdateSyncControlsState()
        {
            string currentService = comboSyncService.ActiveId ?? "sshfs";
            var cfg = Preferences.Current;

            bool isConfigured = !string.IsNullOrWhiteSpace(cfg.SyncSelectedServiceAddin);
            bool isCurrentConfigured = (cfg.SyncSelectedServiceAddin == currentService);

            if (currentService == "sshfs")
            {
                sshPrefsWidget.Show();
                localPrefsWidget.Hide();
            }
            else
            {
                sshPrefsWidget.Hide();
                localPrefsWidget.Show();
            }

            // If configured with current service, inputs and combo are locked
            comboSyncService.Sensitive = !isConfigured;
            sshPrefsWidget.Sensitive = !isCurrentConfigured;
            localPrefsWidget.Sensitive = !isCurrentConfigured;

            btnClearSync.Sensitive = isCurrentConfigured;

            if (currentService == "sshfs")
            {
                bool hasRequired = !string.IsNullOrWhiteSpace(entrySshServer.Text) && !string.IsNullOrWhiteSpace(entrySshUsername.Text);
                btnSaveSync.Sensitive = !isCurrentConfigured && hasRequired;
            }
            else
            {
                bool hasRequired = !string.IsNullOrWhiteSpace(entryLocalPath.Text);
                btnSaveSync.Sensitive = !isCurrentConfigured && hasRequired;
            }
        }

        private void OnClearSyncClicked(object? sender, EventArgs e)
        {
            var dialog = new MessageDialog(
                this,
                DialogFlags.Modal,
                MessageType.Question,
                ButtonsType.YesNo,
                "您確定嗎？\n\n不建議清除同步化設定值。當您儲存新的設定值時可能被強迫要再次同步所有的筆記。"
            );

            int resp = dialog.Run();
            dialog.Hide();
            dialog.Dispose();

            if (resp != (int)ResponseType.Yes) return;

            // Unmount if SSH
            SshSyncService.Unmount();

            var c = Preferences.Current;
            c.SyncSelectedServiceAddin = string.Empty;
            c.SyncSshfsServer = string.Empty;
            c.SyncSshfsPort = -1;
            c.SyncSshfsUsername = string.Empty;
            c.SyncSshfsFolder = string.Empty;
            c.SyncLocalPath = string.Empty;
            Preferences.Current = c;

            UpdateSyncControlsState();
        }

        private void OnSaveSyncClicked(object? sender, EventArgs e)
        {
            string service = comboSyncService.ActiveId ?? "sshfs";

            if (service == "sshfs")
            {
                string rawServer = entrySshServer.Text.Trim();
                string username = entrySshUsername.Text.Trim();
                string folder = entrySshFolder.Text.Trim();

                if (string.IsNullOrWhiteSpace(rawServer) || string.IsNullOrWhiteSpace(username))
                {
                    var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok, "伺服器或使用者名稱欄位是空的。");
                    msg.Run();
                    msg.Hide();
                    msg.Dispose();
                    return;
                }

                int port = -1;
                string server = rawServer;
                int colon = rawServer.LastIndexOf(':');
                if (colon > 0 && int.TryParse(rawServer.Substring(colon + 1), out int p))
                {
                    port = p;
                    server = rawServer.Substring(0, colon);
                }

                // Visual feedback: Watch cursor
                if (GdkWindow != null)
                {
                    GdkWindow.Cursor = new Cursor(CursorType.Watch);
                }

                try
                {
                    // Attempt Mount & test
                    if (!SshSyncService.Mount(server, port, username, folder, out string mountError))
                    {
                        var errDlg = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Close, $"連線錯誤\n\n{GLib.Markup.EscapeText(mountError)}");
                        errDlg.Run();
                        errDlg.Hide();
                        errDlg.Dispose();
                        return;
                    }

                    // Success! Save config
                    var c = Preferences.Current;
                    c.SyncSelectedServiceAddin = "sshfs";
                    c.SyncSshfsServer = server;
                    c.SyncSshfsPort = port;
                    c.SyncSshfsUsername = username;
                    c.SyncSshfsFolder = folder;
                    Preferences.Current = c;

                    UpdateSyncControlsState();

                    // Prompt user to sync now
                    var succDlg = new MessageDialog(
                        this,
                        DialogFlags.Modal,
                        MessageType.Info,
                        ButtonsType.YesNo,
                        "連線成功\n\nTomboy 已準備好同步您的筆記。是否要立刻同步它們？"
                    );
                    int res = succDlg.Run();
                    succDlg.Hide();
                    succDlg.Dispose();

                    if (res == (int)ResponseType.Yes)
                    {
                        var syncRes = SyncService.SynchronizeConfigured();
                        var resDlg = new MessageDialog(this, DialogFlags.Modal, syncRes.Success ? MessageType.Info : MessageType.Warning, ButtonsType.Ok, GLib.Markup.EscapeText(syncRes.Message));
                        resDlg.Run();
                        resDlg.Hide();
                        resDlg.Dispose();
                    }
                }
                finally
                {
                    if (GdkWindow != null)
                    {
                        GdkWindow.Cursor = null;
                    }
                }
            }
            else
            {
                // Local Folder
                string path = entryLocalPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok, "請指定本機資料夾路徑。");
                    msg.Run();
                    msg.Hide();
                    msg.Dispose();
                    return;
                }

                try
                {
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, $"無法建立目錄：{GLib.Markup.EscapeText(ex.Message)}");
                    msg.Run();
                    msg.Hide();
                    msg.Dispose();
                    return;
                }

                var c = Preferences.Current;
                c.SyncSelectedServiceAddin = "local";
                c.SyncLocalPath = path;
                Preferences.Current = c;

                UpdateSyncControlsState();

                var succDlg = new MessageDialog(
                    this,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.YesNo,
                    "連線成功\n\nTomboy 已準備好同步您的筆記。是否要立刻同步它們？"
                );
                int res = succDlg.Run();
                succDlg.Hide();
                succDlg.Dispose();

                if (res == (int)ResponseType.Yes)
                {
                    var syncRes = SyncService.SynchronizeConfigured();
                    var resDlg = new MessageDialog(this, DialogFlags.Modal, syncRes.Success ? MessageType.Info : MessageType.Warning, ButtonsType.Ok, GLib.Markup.EscapeText(syncRes.Message));
                    resDlg.Run();
                    resDlg.Hide();
                    resDlg.Dispose();
                }
            }
        }

        private void OnAdvancedSyncClicked(object? sender, EventArgs e)
        {
            var cfg = Preferences.Current;
            var dlg = new Dialog("其他同步化選項", this, DialogFlags.Modal | DialogFlags.DestroyWithParent, "關閉", ResponseType.Close);
            dlg.SetDefaultSize(420, 240);

            var vbox = new VBox(false, 10) { BorderWidth = 16 };
            var lbl = new Label("當偵測到本地端筆記與設定的同步化伺服器之間有衝突時：") { Wrap = true, Xalign = 0 };
            vbox.PackStart(lbl, false, false, 0);

            var r1 = new RadioButton("永遠詢問我要怎麼做。");
            var r2 = new RadioButton(r1, "重新命名我的本地端筆記。");
            var r3 = new RadioButton(r1, "以該伺服器的更新取代我的本地端筆記。");

            switch (cfg.SyncConfiguredConflictBehavior)
            {
                case 1: r2.Active = true; break;
                case 2: r3.Active = true; break;
                default: r1.Active = true; break;
            }

            System.Action updateConflict = () => {
                var c = Preferences.Current;
                if (r2.Active) c.SyncConfiguredConflictBehavior = 1;
                else if (r3.Active) c.SyncConfiguredConflictBehavior = 2;
                else c.SyncConfiguredConflictBehavior = 0;
                Preferences.Current = c;
            };

            r1.Toggled += (s, ev) => updateConflict();
            r2.Toggled += (s, ev) => updateConflict();
            r3.Toggled += (s, ev) => updateConflict();

            vbox.PackStart(r1, false, false, 0);
            vbox.PackStart(r2, false, false, 0);
            vbox.PackStart(r3, false, false, 0);

            dlg.ContentArea.PackStart(vbox, true, true, 0);
            dlg.ShowAll();
            dlg.Run();
            dlg.Hide();
            dlg.Dispose();
        }
        #endregion

        #region Page 4: 附加元件 (Add-ins)
        private class AddinInfo
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Version { get; set; } = "1.0.0";
            public string Description { get; set; } = string.Empty;
            public string Author { get; set; } = "Tomboy Team";
            public string Copyright { get; set; } = "© 2004-2026 Tomboy Project";
            public bool Enabled { get; set; } = true;
            public bool HasPreferences { get; set; } = false;
        }

        private Widget CreateAddinsTab()
        {
            var vbox = new VBox(false, 8) { BorderWidth = 12 };

            var headerLabel = new Label("已安裝下列附加元件") { Xalign = 0 };
            vbox.PackStart(headerLabel, false, false, 0);

            var hbox = new HBox(false, 8);

            // TreeView
            addinListStore = new ListStore(typeof(Pixbuf), typeof(string), typeof(string), typeof(bool), typeof(object));
            addinTreeView = new TreeView(addinListStore) { HeadersVisible = true };

            var colStatus = new TreeViewColumn { Title = "狀態", Sizing = TreeViewColumnSizing.Autosize };
            var crtPix = new CellRendererPixbuf();
            colStatus.PackStart(crtPix, false);
            colStatus.AddAttribute(crtPix, "pixbuf", 0);
            addinTreeView.AppendColumn(colStatus);

            var colName = new TreeViewColumn { Title = "名稱", Expand = true };
            var crtName = new CellRendererText();
            colName.PackStart(crtName, true);
            colName.AddAttribute(crtName, "text", 1);
            addinTreeView.AppendColumn(colName);

            var colVer = new TreeViewColumn { Title = "版本", Sizing = TreeViewColumnSizing.Autosize };
            var crtVer = new CellRendererText();
            colVer.PackStart(crtVer, true);
            colVer.AddAttribute(crtVer, "text", 2);
            addinTreeView.AppendColumn(colVer);

            addinTreeView.Selection.Changed += (s, e) => UpdateAddinButtons();

            var scroll = new ScrolledWindow { ShadowType = ShadowType.In };
            scroll.Add(addinTreeView);
            hbox.PackStart(scroll, true, true, 0);

            // Action Buttons
            var btnBox = new VButtonBox { LayoutStyle = ButtonBoxStyle.Start, Spacing = 6 };

            btnEnableAddin = new Button("啟用(_E)") { Sensitive = false };
            btnEnableAddin.Clicked += OnEnableAddinClicked;
            btnBox.PackStart(btnEnableAddin, false, false, 0);

            btnDisableAddin = new Button("停用(_D)") { Sensitive = false };
            btnDisableAddin.Clicked += OnDisableAddinClicked;
            btnBox.PackStart(btnDisableAddin, false, false, 0);

            btnAddinPrefs = new Button(Stock.Preferences) { Sensitive = false };
            btnAddinPrefs.Clicked += OnAddinPrefsClicked;
            btnBox.PackStart(btnAddinPrefs, false, false, 0);

            btnAddinInfo = new Button(Stock.Info) { Sensitive = false };
            btnAddinInfo.Clicked += OnAddinInfoClicked;
            btnBox.PackStart(btnAddinInfo, false, false, 0);

            hbox.PackEnd(btnBox, false, false, 0);
            vbox.PackStart(hbox, true, true, 0);

            var linkBtn = new LinkButton("https://wiki.gnome.org/Apps/Tomboy/PluginList", "取得更多附加元件…");
            vbox.PackEnd(linkBtn, false, false, 0);

            PopulateAddinsList();

            return vbox;
        }

        private void PopulateAddinsList()
        {
            addinListStore.Clear();

            var addins = new List<AddinInfo>
            {
                new AddinInfo { Id = "ExportToHtml", Name = "HTML 匯出", Description = "將筆記匯出為標準 HTML 網頁文件格式。" },
                new AddinInfo { Id = "PrintNotes", Name = "列印筆記", Description = "列印選取的筆記或輸出為 PDF 文件。" },
                new AddinInfo { Id = "LatexMath", Name = "LaTeX 數學公式", Description = "在筆記中輸入以 \\[ ... \\] 或 \\( ... \\) 包裹的 LaTeX 數學公式並即時彩現影像。", HasPreferences = true },
                new AddinInfo { Id = "InsertTimestamp", Name = "插入時間戳記", Description = "在目前游標位置插入自訂的時間與日期字串 (Ctrl+D)。", HasPreferences = true },
                new AddinInfo { Id = "FixedWidth", Name = "固定寬度字型", Description = "將選取的文字設定為固定寬度 (等寬) 字型。" },
                new AddinInfo { Id = "Underline", Name = "底線", Description = "為選取的文字加上底線格式 (Ctrl+U)。" },
                new AddinInfo { Id = "NoteOfTheDay", Name = "今日筆記", Description = "每日自動建立當天的日誌與備忘筆記。" },
                new AddinInfo { Id = "Backlinks", Name = "連回連結", Description = "檢視並導覽有哪些筆記含有連結到目前這篇筆記。" },
                new AddinInfo { Id = "RemoveBrokenLinks", Name = "移除損壞的連結", Description = "自動尋找並清理指向已不存在筆記的失效超連結。" },
                new AddinInfo { Id = "SshSyncService", Name = "SSH 同步化服務", Description = "透過 SSH/sshfs 將筆記同步至遠端伺服器。" },
                new AddinInfo { Id = "AdvancedPreferences", Name = "進階偏好設定", Version = "1.0.0", Author = "Alex Tereschenko", Description = "提供自訂選單筆記數量與啟動行為等進階設定。" }
            };

            var cfg = Preferences.Current;
            var okIcon = IconService.GetIcon("tomboy", 16) ?? RenderIconPixbuf(Stock.Apply, IconSize.Menu);

            foreach (var ai in addins)
            {
                if (ai.Id == "LatexMath")
                {
                    ai.Enabled = cfg.EnableLatexMath;
                }
                else if (cfg.AddinsEnabled.TryGetValue(ai.Id, out bool enabled))
                {
                    ai.Enabled = enabled;
                }
                addinListStore.AppendValues(okIcon, ai.Name, ai.Version, ai.Enabled, ai);
            }
        }

        private AddinInfo? GetSelectedAddin()
        {
            if (addinTreeView.Selection.GetSelected(out var model, out var iter))
            {
                return model.GetValue(iter, 4) as AddinInfo;
            }
            return null;
        }

        private void UpdateAddinButtons()
        {
            var addin = GetSelectedAddin();
            if (addin == null)
            {
                btnEnableAddin.Sensitive = false;
                btnDisableAddin.Sensitive = false;
                btnAddinPrefs.Sensitive = false;
                btnAddinInfo.Sensitive = false;
            }
            else
            {
                btnEnableAddin.Sensitive = !addin.Enabled;
                btnDisableAddin.Sensitive = addin.Enabled;
                btnAddinPrefs.Sensitive = addin.HasPreferences;
                btnAddinInfo.Sensitive = true;
            }
        }

        private void OnEnableAddinClicked(object? sender, EventArgs e)
        {
            var addin = GetSelectedAddin();
            if (addin == null) return;
            addin.Enabled = true;
            var cfg = Preferences.Current;
            if (addin.Id == "LatexMath")
            {
                cfg.EnableLatexMath = true;
            }
            else
            {
                cfg.AddinsEnabled[addin.Id] = true;
            }
            Preferences.Current = cfg;
            PopulateAddinsList();
            UpdateAddinButtons();
        }

        private void OnDisableAddinClicked(object? sender, EventArgs e)
        {
            var addin = GetSelectedAddin();
            if (addin == null) return;
            addin.Enabled = false;
            var cfg = Preferences.Current;
            if (addin.Id == "LatexMath")
            {
                cfg.EnableLatexMath = false;
            }
            else
            {
                cfg.AddinsEnabled[addin.Id] = false;
            }
            Preferences.Current = cfg;
            PopulateAddinsList();
            UpdateAddinButtons();
        }

        private void OnAddinPrefsClicked(object? sender, EventArgs e)
        {
            var addin = GetSelectedAddin();
            if (addin == null) return;

            if (addin.Id == "LatexMath")
            {
                var dlg = new Dialog("LaTeX 數學公式 偏好設定", this, DialogFlags.Modal | DialogFlags.DestroyWithParent, "關閉", ResponseType.Close);
                dlg.SetDefaultSize(520, 480);

                var latexWidget = new Tomboy.Latex.LatexPreferencesWidget(
                    Preferences.Current.LatexHeader,
                    Preferences.Current.LatexFooter,
                    Preferences.Current.LatexDollarEnabled
                );

                latexWidget.SettingsApplied += (h, f, d) =>
                {
                    var c = Preferences.Current;
                    c.LatexHeader = h;
                    c.LatexFooter = f;
                    c.LatexDollarEnabled = d;
                    Preferences.Current = c;
                };

                dlg.ContentArea.PackStart(latexWidget, true, true, 8);
                dlg.ShowAll();
                dlg.Run();
                dlg.Hide();
                dlg.Dispose();
            }
            else if (addin.Id == "InsertTimestamp")
            {
                var dlg = new Dialog("插入時間戳記 偏好設定", this, DialogFlags.Modal | DialogFlags.DestroyWithParent, "關閉", ResponseType.Close);
                dlg.SetDefaultSize(440, 220);

                var vbox = new VBox(false, 10) { BorderWidth = 16 };
                vbox.PackStart(new Label("自訂時間戳記格式 (用於 Ctrl+D)：") { Xalign = 0 }, false, false, 0);

                var entry = new Entry(Preferences.Current.CustomTimestampFormat);
                vbox.PackStart(entry, false, false, 0);

                var previewLabel = new Label(string.Empty) { UseMarkup = true, Xalign = 0 };
                vbox.PackStart(previewLabel, false, false, 0);

                System.Action updatePreview = () => {
                    try
                    {
                        string str = DateTime.Now.ToString(entry.Text);
                        previewLabel.Text = $"<b>預覽：</b> {str}";
                        var c = Preferences.Current;
                        c.CustomTimestampFormat = entry.Text;
                        Preferences.Current = c;
                    }
                    catch
                    {
                        previewLabel.Text = "<span foreground='red'>無效的時間格式字串</span>";
                    }
                };

                entry.Changed += (s, ev) => updatePreview();
                updatePreview();

                dlg.ContentArea.PackStart(vbox, true, true, 0);
                dlg.ShowAll();
                dlg.Run();
                dlg.Hide();
                dlg.Dispose();
            }
        }

        private void OnAddinInfoClicked(object? sender, EventArgs e)
        {
            var addin = GetSelectedAddin();
            if (addin == null) return;

            var dlg = new Dialog(addin.Name, this, DialogFlags.Modal | DialogFlags.DestroyWithParent, "關閉", ResponseType.Close);
            dlg.SetDefaultSize(440, 260);

            var hbox = new HBox(false, 12) { BorderWidth = 16 };
            var icon = new Image(Stock.DialogInfo, IconSize.Dialog) { Yalign = 0.0f };
            hbox.PackStart(icon, false, false, 0);

            var vbox = new VBox(false, 8);
            var titleLbl = new Label($"<span size='large' weight='bold'>{addin.Name} {addin.Version}</span>") { UseMarkup = true, Xalign = 0 };
            vbox.PackStart(titleLbl, false, false, 0);

            var descLbl = new Label(addin.Description) { Wrap = true, Xalign = 0 };
            vbox.PackStart(descLbl, false, false, 0);

            var authorLbl = new Label($"<b>作者:</b> {addin.Author}\n<b>版權:</b> {addin.Copyright}") { UseMarkup = true, Xalign = 0 };
            vbox.PackStart(authorLbl, false, false, 0);

            hbox.PackStart(vbox, true, true, 0);
            dlg.ContentArea.PackStart(hbox, true, true, 0);
            dlg.ShowAll();
            dlg.Run();
            dlg.Hide();
            dlg.Dispose();
        }
        #endregion

        #region Page 5: 進階 (Advanced)
        private Widget CreateAdvancedTab()
        {
            var cfg = Preferences.Current;
            var vbox = new VBox(false, 12) { BorderWidth = 12 };

            // 1. Menu Min/Max Count
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };

            var lblMin = new Label("最近使用文件清單中顯示的最小筆記數:") { Xalign = 0 };
            spinMenuMin = new SpinButton(1, cfg.MenuMaxNoteCount, 1) { Value = cfg.MenuMinNoteCount };
            grid.Attach(lblMin, 0, 0, 1, 1);
            grid.Attach(spinMenuMin, 1, 0, 1, 1);

            var lblMax = new Label("最近使用文件清單中顯示的最大筆記數:") { Xalign = 0 };
            spinMenuMax = new SpinButton(cfg.MenuMinNoteCount, 100, 1) { Value = cfg.MenuMaxNoteCount };
            grid.Attach(lblMax, 0, 1, 1, 1);
            grid.Attach(spinMenuMax, 1, 1, 1, 1);

            spinMenuMin.ValueChanged += (s, e) => {
                int val = (int)spinMenuMin.Value;
                var c = Preferences.Current;
                c.MenuMinNoteCount = val;
                Preferences.Current = c;
                spinMenuMax.SetRange(val, 100);
            };

            spinMenuMax.ValueChanged += (s, e) => {
                int val = (int)spinMenuMax.Value;
                var c = Preferences.Current;
                c.MenuMaxNoteCount = val;
                Preferences.Current = c;
                spinMenuMin.SetRange(1, val);
            };

            vbox.PackStart(grid, false, false, 0);

            // 2. Enable startup notes
            checkStartupNotes = new CheckButton("啟用啟動時開啟筆記") { Active = cfg.EnableStartupNotes };
            checkStartupNotes.Toggled += (s, e) => {
                var c = Preferences.Current;
                c.EnableStartupNotes = checkStartupNotes.Active;
                Preferences.Current = c;
            };
            vbox.PackStart(checkStartupNotes, false, false, 0);

            var startupTip = new Label("<span size='small' foreground='#555555'>啟動 Tomboy 時自動開啟上次未關閉或標記為在啟動時開啟的筆記。</span>")
            {
                UseMarkup = true,
                Wrap = true,
                Xalign = 0
            };
            vbox.PackStart(startupTip, false, false, 0);

            vbox.PackStart(new HSeparator(), false, false, 6);

            // 3. Storage & Cache Tools
            var frame = new Frame("存放與維護");
            var fVBox = new VBox(false, 8) { BorderWidth = 10 };

            var dirLabel = new Label($"筆記存放目錄：<b>{NoteStorage.NoteDirectory}</b>") { UseMarkup = true, Xalign = 0 };
            fVBox.PackStart(dirLabel, false, false, 0);

            var btnHBox = new HBox(false, 8);
            var openDirBtn = new Button("📂 開啟筆記目錄");
            openDirBtn.Clicked += (s, e) => {
                try { Process.Start(new ProcessStartInfo(NoteStorage.NoteDirectory) { UseShellExecute = true }); } catch { }
            };
            btnHBox.PackStart(openDirBtn, false, false, 0);

            var clearCacheBtn = new Button("🧹 清除同步快取");
            clearCacheBtn.Clicked += (s, e) => {
                try
                {
                    SshSyncService.Unmount();
                    if (Directory.Exists(SshSyncService.MountPath))
                    {
                        Directory.Delete(SshSyncService.MountPath, true);
                    }
                    var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, "同步快取已清除。");
                    msg.Run();
                    msg.Hide();
                    msg.Dispose();
                }
                catch (Exception ex)
                {
                    var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, $"清除快取時出錯: {GLib.Markup.EscapeText(ex.Message)}");
                    msg.Run();
                    msg.Hide();
                    msg.Dispose();
                }
            };
            btnHBox.PackStart(clearCacheBtn, false, false, 0);

            fVBox.PackStart(btnHBox, false, false, 0);
            frame.Add(fVBox);
            vbox.PackStart(frame, false, false, 0);

            return vbox;
        }
        #endregion
    }
}
