# Tomboy XML 筆記規格參考手冊

Tomboy 筆記檔案以 XML 格式儲存於 `~/.local/share/tomboy/<UUID>.note`。以下為標準 XML 結構與各元素規範：

## 1. 根結構與命名空間

```xml
<?xml version="1.0" encoding="utf-8"?>
<note version="0.3"
      xmlns:link="http://beatniksoftware.com/tomboy/link"
      xmlns:size="http://beatniksoftware.com/tomboy/size"
      xmlns="http://beatniksoftware.com/tomboy">
  <title>筆記標題</title>
  <text xml:space="preserve"><note-content version="0.1">筆記標題

內文...
</note-content></text>
  <last-change-date>2026-10-31T16:00:00.0000000+08:00</last-change-date>
  <last-metadata-change-date>2026-10-31T16:00:00.0000000+08:00</last-metadata-change-date>
  <create-date>2026-10-31T16:00:00.0000000+08:00</create-date>
  <cursor-position>0</cursor-position>
  <selection-bound-position>-1</selection-bound-position>
  <width>650</width>
  <height>500</height>
  <x>-1</x>
  <y>-1</y>
  <tags>
    <tag>system:notebook:筆記本名稱</tag>
  </tags>
  <open-on-startup>False</open-on-startup>
</note>
```

---

## 2. 標籤與樣式元素

在 `<note-content>` 內可使用的樣式標籤：

| 效果 | 標籤語法 | 說明 |
| :--- | :--- | :--- |
| **超大標題** | `<size:huge>文字</size:huge>` | 用於主章節標題（對應 Markdown `#`） |
| **大標題** | `<size:large>文字</size:large>` | 用於子標題（對應 Markdown `##`） |
| **小字** | `<size:small>文字</size:small>` | 註腳或次要資訊 |
| **粗體** | `<bold>文字</bold>` | 強調文字（對應 Markdown `**文字**`） |
| **斜體** | `<italic>文字</italic>` | 斜體說明（對應 Markdown `*文字*`） |
| **刪除線** | `<strikethrough>文字</strikethrough>` | 廢棄或已完成項目 |
| **螢光標記** | `<highlight>文字</highlight>` | 黃色背景醒目提示 |
| **等寬字型** | `<monospace>代碼</monospace>` | 程式碼、指令（對應 Markdown `` `代碼` ``） |
| **外部連結** | `<link:url>http://...</link:url>` | 網址超連結 |
| **內部筆記連結** | `<link:internal>其他筆記標題</link:internal>` | 指向現有其他 Tomboy 筆記的 Wiki 超連結 |
| **時間戳記** | `<datetime>2026-10-31 下午 04:00</datetime>` | 原生時間標記 |

---

## 3. 階層縮排清單 (Nested Lists)

Tomboy 使用嚴格的巢狀結構：
- 第一層：`<list><list-item dir="ltr">文字</list-item></list>`
- 巢狀第二層：位於上一層 `<list-item>` 內部：
  ```xml
  <list>
    <list-item dir="ltr">第一層項目
      <list>
        <list-item dir="ltr">第二層縮排項目</list-item>
      </list>
    </list-item>
    <list-item dir="ltr">回到第一層項目</list-item>
  </list>
  ```
- 每一層清單項目在 Tomboy GUI 中會依序以 `•` (Level 0)、`◦` (Level 1)、`‣` (Level 2) 顯示並具有標準左邊距（LeftMargin）。

---

## 4. LaTeX 數學公式規範 (tomboy-latex)

Tomboy-GTK3 整合了 `tomboy-latex` 模組，其運作與儲存規範如下：

- **XML 儲存格式**：以純文字 `\[ ... \]` 儲存於 `<note-content>` 內，**不包覆任何額外 XML 標籤**。
- **動態渲染機制**：
  - Tomboy 開啟或編輯筆記時，`LatexNoteSession` 會在記憶體中掃描 `\[...\]`。
  - 當公式包含常見數學符號（如 `\frac`, `^`, `_`, `=`, `+`, `-`, `\int` 等）時，調用背景 `latex` 與 `dvipng` 編譯為圖片，並於游標離開時顯示渲染圖。
  - GTK3 的暫時性標籤 `latex_code` 與 `latex_image` 於存檔時會自動被過濾，確保 XML 的跨版本相容性與純淨度。
- **自動化工具防護**：
  - 轉換腳本已針對 `$$...$$` 與 `$formula$`（具數學運算符者）自動標準化轉為 `\[...\]`。
  - 轉換時預先保護 LaTeX 公式內部，避免下標 `_` 或運算符 `*` 被 Markdown 誤判為斜體或粗體。
