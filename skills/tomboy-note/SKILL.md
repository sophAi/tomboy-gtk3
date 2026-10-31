---
name: tomboy-note
description: >-
  Create, update, and manage Tomboy notes (.note XML) from conversation summaries,
  debugging logs, or operational workflows. Use this skill when the user asks to
  document a workflow, summarize the current session into Tomboy, archive notes,
  or create/update notes adhering to Tomboy's native formatting and notebook structure.
---

# Tomboy 筆記歸檔與工作流程文件化規範

本技能提供標準化流程與工具，讓 `antigravity-cli` 在任何對話中均能將技術總結、除錯紀錄或工作流程文件化，並自動生成完全相容於 Tomboy GUI 與 SSH 同步架構的 native 筆記檔案。

---

## 🛠️ 輔助工具：`tomboy_note_writer.py`

技能內建專屬的 Markdown 轉 Tomboy XML 轉換工具：
```bash
python3 ~/.gemini/config/skills/tomboy-note/scripts/tomboy_note_writer.py --title "<標題>" [--notebook "<筆記本名稱>"] [--content "<Markdown內容>" | --file "<檔案路徑>"]
```

### 工具特性：
- **自動產生合規 XML**：自動配置 UUID、RFC 3339 帶時區之時間戳記與 Tomboy 命名空間。
- **自動對應排版標籤**：
  - `# 標題` -> `<size:huge>`
  - `## 子標題` -> `<size:large>`
  - `**粗體**` -> `<bold>`
  - `*斜體*` -> `<italic>`
  - `` `代碼` `` -> `<monospace>`
  - 項目符號（`•`, `◦`, `‣` 或 `-`, `*` 搭配縮排）-> Tomboy 原生巢狀 `<list><list-item dir="ltr">`。
  - 網址超連結 -> `<link:url>`。
- **LaTeX 數學公式支援與防衝突機制**：
  - 自動將 Markdown 區塊數學公式 `$$...$$` 轉換為 Tomboy 原生 `\[...\]`。
  - 自動識別帶有數學指令的行內公式 `$formula$`（如 `$E=mc^2$`）並安全轉為 `\[formula\]`。
  - 完整保護公式內部的下標 `_`、乘號 `*`、次方 `^` 等符號，避免被 Markdown 斜體/粗體誤判破壞。
  - 保留一般金額（如 `$50`）或環境變數（如 `$PATH`）不被誤轉。
- **支援筆記更新**：若同名筆記已存在，自動保持其原始建立日期 (`create-date`) 與檔案 UUID，安全就地更新。

---

## 📋 筆記撰寫與格式規範

當使用者要求將工作流程或對話總結歸檔成 Tomboy 筆記時，請遵循以下步驟：

### 1. 命名與分類規範
- **標題命名**：應具備可搜尋性與主題性，例如：
  - `除錯紀錄: <問題摘要或關鍵錯誤碼>`
  - `工作流程: <軟體/功能建置與發布步驟>`
  - `<主題關鍵字>: <具體內容說明>`（例如：`Tomboy: GTK3排版崩潰除錯與縮排貼上修正`）
- **筆記本分類（Notebook）**：
  若無特別指定，請依主題歸類至常用筆記本，例如：
  - `共用 - 自由軟體`（Linux 工具、開源專案、CLI 應用）
  - `共用 - vim與shell`（命令列工具、腳本開發）
  - `共用 - gcc gpu與AI`（編譯、模型與 AI 工具）
  - 若為特定專案亦可指定專案名稱。

### 2. 結構範本
參考標準範本：[template_workflow_note.md](./references/template_workflow_note.md)
包含以下核心區塊：
1. **背景與目的**：1~2 句話說明對話處理的關鍵問題或目標。
2. **前置需求與環境**：記錄環境、依賴套件或相依檔案。
3. **詳細步驟與操作指令**：使用階層縮排清單完整記錄操作指令與設定檔變更。
4. **關鍵決策與避坑指南**：記錄本次排查發現的根本原因（Root Cause）與避免重複踩坑的關鍵技巧。
5. **相關資源**：相關連結或本機檔案路徑。

---

## 🚀 建立筆記標準操作流程範例

在對話中呼叫工具執行命令：

```bash
python3 ~/.gemini/config/skills/tomboy-note/scripts/tomboy_note_writer.py \
  --title "工作流程: Tomboy GTK3崩潰修復與縮排保留" \
  --notebook "共用 - 自由軟體" \
  --content "
## 背景與目的
解決 Tomboy 在移動滑鼠時因 GTK3 排版越界導致崩潰，以及跨筆記貼上縮排失效的問題。

## 核心修復步驟
• 第一步：消除 Invisible 標籤引發的排版越界
  ◦ 檔案：\`XmlNoteBufferBinder.cs\` 與 \`LatexNoteSession.cs\`
  ◦ 改用 \`Size = 1\` 代替 \`Invisible = true\`
• 第二步：嚴格化 LaTeX 符號比對
  ◦ 內部必須包含常見數學指令，防止日常指令路徑遭誤認
• 第三步：跨筆記貼上自動重建 Indent
  ◦ 解析開頭清單符號並自動套用 \`depth\` 標籤

## 避坑指南
• GTK3 的 \`gtktextlayout\` 在計算含有 \`Invisible\` 標籤的行底時會混淆實體位元組與可見位元組，導致越界致命錯誤。
"
```

建立完成後，向使用者回報儲存路徑與標題，並提醒可於 Tomboy 介面或搜尋視窗直接開啟查閱。
