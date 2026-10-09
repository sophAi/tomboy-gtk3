# Tomboy Notes (.NET 8 + GTK3 + LaTeX)

Modernized Linux desktop note-taking application ported to **.NET 8** and **GTK+ 3 (GtkSharp)**, with built-in **LaTeX math formula rendering** and classic Tomboy compatibility.

---

## 🌟 特色功能 (Features)

- **現代化架構**：以 .NET 8 LTS 與 GtkSharp (GTK3) 重構，擺脫舊版 Mono 2.x 與 GTK2 依賴，相容 Linux Mint 21 / 22 / 23 與 Ubuntu 22.04 / 24.04 / 26.04+。
- **系統匣圖示 (System Tray)**：完美支援 Linux Mint MATE Panel、Cinnamon 與 XFCE 系統匣，支援右鍵選單快速新建筆記、搜尋筆記與喜好設定。
- **整合 LaTeX 數學公式渲染**：
  - 輸入 `\[ E = mc^2 \]` 自動排程渲染高品質數學公式影像。
  - 點選公式影像即時切換回 LaTeX 原始語法進行編輯，離開後自動重新渲染。
  - 完整支援複製、剪下與貼上含 LaTeX 公式之內容，不會造成筆記損毀或重複圖檔。
- **Wiki-style 筆記互聯與自動超連結 (Interlinking)**：
  - 輸入其他筆記標題時**即時動態偵測並自動轉為超連結**，支援中文無空格自然句識別與英文單詞邊界防護（避免如 `latest` 誤連結 `test`）。
  - 依標題長度降冪優先匹配，自動排除筆記首行標題、外部 URL 與 LaTeX 區塊。
  - 點選已開啟之筆記超連結時直接切換定焦（`Present`）該視窗，避免產生重複視窗；若筆記不存在則自動建立新筆記。
  - 支援選取文字後點擊工具列「連結 (Link)」按鈕一鍵跳轉或建立關聯筆記。
- **建立新筆記自動全選定焦標題**：
  - 點擊「新增筆記」或使用快捷鍵建立新筆記時，輸入游標自動聚焦於標題行並選取整個預設標題，開始打字即立即取代整行標題。
- **URL / URI 自動超連結偵測**：
  - 輸入或貼上網址即時偵測並套用超連結樣式（`http://`, `https://`, `ftp://`, `sftp://`, `file://`, `mailto:`, `www.*`, 電子郵件信箱, 本地路徑等）。
  - 自動處理中英文標點符號過濾，保留成對括號網址（如 Wikipedia 條目）。
  - 滑鼠懸停自動切換手型游標（`Hand2`），左鍵開啟預設瀏覽器，右鍵選單提供「開啟連結」與「複製連結位址」。
- **搜尋視窗與筆記內搜尋深度連動**：
  - 比對 Tomboy 原生體驗，在「搜尋筆記」視窗輸入關鍵字搜尋後，雙擊、按 Enter 或右鍵開啟筆記時，該筆記視窗會直接開啟搜尋列並自動帶入剛才搜尋的字串，高亮所有相符項目並自動捲動聚焦至第一個搜尋結果。
  - 若搜尋欄沒有內容，則開啟筆記時預設不開啟搜尋框，維持簡潔編輯版面。
- **工具列版面與視窗尺寸最佳化**：
  - 移除佔據橫向空間的「Notebook:」標籤文字，直接顯示筆記本下拉選單並搭配 Tooltip 提示。
  - 預設開啟筆記視窗寬度調整為 650px，確保在各式 Linux 桌面主題與字型縮放下，工具列所有按鈕與筆記本下拉選單皆完整呈現，不會被擠入折疊選單。
- **命令行、系統常駐與快速啟動器整合**：
  - **托盤常駐模式 (`--tray`, `--background`, `-b`)**：支援開機或背景啟動時僅常駐於 MATE Panel / 系統通知區，**完全不彈出主搜尋筆記視窗**，安靜待命；隨時點擊托盤圖示即可切換顯示視窗或搜尋筆記；若實例已在背景執行，再次執行 `tomboy --tray` 亦會靜默退出，避免干擾使用者。
  - **開機自動啟動 (Autostart)**：完美相容 Linux Mint / Ubuntu「啟動應用程式」，設定指令為 `tomboy --tray` 即可實現開機登入後無縫背景常駐。
  - **命令列操作參數**：完整支援 `--open-note <路徑或標題>`、`--search [關鍵字]`、`--new-note [標題]`、`--start-here`、`-q / --quit` 等參數，完美整合 **Synapse**、**Ulauncher**、**Kupfer** 等快速啟動器與桌面自動化腳本。
- **個人化色彩樣式偏好設定 (Preferences)**：
  - 於「偏好設定」視窗與 `preferences.json` 整合統一色彩管理區塊，支援自由自訂「筆記標題色彩 (`NoteTitleColor`)」、「筆記內部連結色彩 (`NoteLinkColor`)」與「外部網址超連結色彩 (`UrlLinkColor`)」。
  - 點擊色票即可透過 GTK 色彩選擇對話框即時微調，並同步更新至目前開啟的所有筆記與後續開啟之視窗。
- **筆記本分類與拖曳管理 (Notebooks)**：
  - 支援筆記本篩選分類，筆記刪除後自動保持在目前選擇的筆記本。
  - 支援拖曳筆記至指定筆記本。
- **經典 XML 相容性與樣式**：
  - 完全相容 Tomboy 原生 XML 儲存格式 (`~/.local/share/tomboy`)。
  - 支援粗體、斜體、底線、刪除線、螢光標記、字級調整、等寬字型、項目符號縮排層級。
  - 支援匯出為 HTML 網頁。
  - 筆記內搜尋列 (Ctrl+F) 支援高亮與 Enter 連續搜尋下一個相符項目。

---

## 💻 命令行參數與系統常駐用法 (CLI & Tray Usage)

Tomboy 提供完整的命令行介面，支援背景托盤常駐與快速筆記操作：

| 命令列參數 | 說明 |
| :--- | :--- |
| `tomboy --tray` 或 `-b` | **常駐托盤模式**：啟動並常駐於系統匣 (MATE Panel)，**不顯示主搜尋視窗**；點擊托盤圖示可隨時展開視窗。若 Tomboy 已在背景執行，則靜默退出不重複開啟。 |
| `tomboy` | 正常啟動，顯示「搜尋所有筆記」主視窗並常駐托盤。 |
| `tomboy --search [關鍵字]` | 開啟主搜尋視窗，並可選指定預先填入搜尋關鍵字。 |
| `tomboy --new-note [標題]` | 立即建立並開啟新筆記（可選指定初始標題）。 |
| `tomboy --open-note <標題/URI/路徑>` | 依標題、`note://tomboy/...` URI 或 `.note` 檔案路徑開啟指定筆記。 |
| `tomboy --start-here` | 快速開啟「Start Here」首頁筆記。 |
| `tomboy -q` 或 `--quit` | 關閉目前正在背景執行的 Tomboy 實例。 |
| `tomboy --help` | 顯示所有命令行參數說明。 |
| `tomboy --version` | 顯示程式版本資訊。 |

### ⚙️ 設定 Linux Mint / Ubuntu 開機自動常駐 (Autostart)

如需讓 Tomboy 在登入桌面時自動常駐於工作列/通知區域，且不干擾開啟主視窗：

#### 方法 A：透過圖形化介面設定
1. 開啟 **控制中心 (Control Center)** → **啟動應用程式 (Startup Applications)**。
2. 點選 **加入 (Add)**：
   - **名稱 (Name)**：`Tomboy Notes`
   - **指令 (Command)**：`tomboy --tray`
   - **註解 (Comment)**：`Start Tomboy in system tray`
3. 儲存即可。

#### 方法 B：建立桌面自啟動檔案
亦可直接建立 `~/.config/autostart/tomboy.desktop`：
```ini
[Desktop Entry]
Type=Application
Exec=tomboy --tray
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name=Tomboy Notes
Comment=Start Tomboy resident in system tray
Icon=tomboy
```

---

## 📂 專案結構 (Project Structure)

```text
tomboy-gtk3/
├── Tomboy.sln              # .NET 解決方案檔
├── build_deb.sh            # 一鍵編譯與打包 .deb 套件腳本
├── README.md               # 專案說明文件
├── LICENSE                 # GPL-2.0 開源授權
├── .gitignore              # Git 忽略設定
├── data/                   # 桌面整合資源
│   ├── tomboy.desktop      # 應用程式桌面啟動項目
│   ├── org.gnome.Tomboy.service # D-Bus 啟動服務
│   └── icons/              # Hicolor 多尺寸與 SVG 圖示
└── src/                    # 原始碼目錄
    ├── Tomboy/             # Tomboy GTK3 主應用程式專案
    └── Tomboy.Latex/       # LaTeX 公式渲染整合專案
```

---

## 🛠️ 開發與建置需求 (Prerequisites)

### 1. 安裝建置工具 (.NET 8 SDK 與 dpkg)

在 Linux Mint / Ubuntu 上執行：
```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0 dpkg build-essential
```

### 2. 安裝 LaTeX 執行階段相依套件 (若需公式渲染)

```bash
sudo apt install -y texlive-latex-base dvipng
```

---

## 🚀 編譯與執行 (Build & Run)

### 從原始碼編譯方案：
```bash
dotnet build
```

### 直接本機執行 Tomboy：
```bash
dotnet run --project src/Tomboy
```

---

## 📦 打包成 Debian (.deb) 安裝檔

本專案提供自包含（Self-Contained）一鍵打包腳本 `build_deb.sh`，可將 .NET 執行環境與所有相依性一同打包，安裝於未安裝 .NET SDK 的系統上亦可直接執行：

```bash
# 賦予執行權限並執行打包腳本
./build_deb.sh
```

打包完成後將於目錄下生成：
```text
tomboy_gtk3_2.0.0_amd64.deb
```

### 安裝產生的 deb 套件：
```bash
# 若先前有執行中的 Tomboy，建議先關閉舊實例
tomboy -q || killall tomboy

# 安裝套件
sudo dpkg -i tomboy_gtk3_2.0.0_amd64.deb
sudo apt-get install -f   # 若缺少基礎 GTK 執行套件時自動補齊
```

---

## 📄 授權條款 (License)

本專案遵循 GNU General Public License v2.0 (GPL-2.0) 授權發佈。詳細內容請參閱 [LICENSE](LICENSE) 檔案。
