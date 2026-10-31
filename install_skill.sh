#!/usr/bin/env bash
# ==============================================================================
# Tomboy Note Skill Installer for Antigravity-CLI
# 適用於 Antigravity / Gemini CLI 之 tomboy-note 技能全域安裝工具
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_SKILL_DIR="${SCRIPT_DIR}/skills/tomboy-note"

TARGET_BASE_DIR="${HOME}/.gemini/config"
TARGET_SKILL_DIR="${TARGET_BASE_DIR}/skills/tomboy-note"
AGENTS_MD="${TARGET_BASE_DIR}/AGENTS.md"

DIRECTIVE_MARKER="<!-- Tomboy Note Archiving Directive -->"
DIRECTIVE_CONTENT="
${DIRECTIVE_MARKER}
- 當使用者要求將對話總結、工作流程或技術問題排查紀錄文件化歸檔至 Tomboy 筆記時，請依據 \`tomboy-note\` 技能規範，透過 \`${TARGET_SKILL_DIR}/scripts/tomboy_note_writer.py\` 建立或更新相應的 Tomboy 筆記。
"

function print_usage() {
    cat << EOF
用法:
  ./install_skill.sh [選項]

選項:
  -h, --help        顯示此說明訊息
  -u, --uninstall   移除已安裝的 tomboy-note 技能與全域規則
EOF
}

function uninstall_skill() {
    echo "==> 正在移除 tomboy-note 技能..."
    if [ -d "${TARGET_SKILL_DIR}" ]; then
        rm -rf "${TARGET_SKILL_DIR}"
        echo "已刪除目錄: ${TARGET_SKILL_DIR}"
    fi

    if [ -f "${AGENTS_MD}" ]; then
        if grep -q "${DIRECTIVE_MARKER}" "${AGENTS_MD}"; then
            # 刪除 directive 區塊
            python3 -c "
with open('${AGENTS_MD}', 'r', encoding='utf-8') as f:
    lines = f.readlines()
out = []
skip = False
for line in lines:
    if '${DIRECTIVE_MARKER}' in line:
        skip = True
        continue
    if skip and line.startswith('- 當使用者要求將對話總結'):
        skip = False
        continue
    out.append(line)
with open('${AGENTS_MD}', 'w', encoding='utf-8') as f:
    f.writelines(out)
"
            echo "已從 ${AGENTS_MD} 移除全域指令引導規則。"
        fi
    fi
    echo "==> 卸載完成！"
    exit 0
}

# 參數解析
if [ $# -gt 0 ]; then
    case "$1" in
        -u|--uninstall)
            uninstall_skill
            ;;
        -h|--help)
            print_usage
            exit 0
            ;;
        *)
            echo "錯誤: 不支援的選項 $1"
            print_usage
            exit 1
            ;;
    esac
fi

echo "============================================================"
echo "  開始安裝 Tomboy Note Skill 至 Antigravity-CLI"
echo "============================================================"

# 1. 檢查 Python 3
if ! command -v python3 &> /dev/null; then
    echo "錯誤: 系統未安裝 python3，請先安裝 Python 3！" >&2
    exit 1
fi

# 2. 檢查來源目錄
if [ ! -d "${SOURCE_SKILL_DIR}" ]; then
    echo "錯誤: 找不到技能來源目錄: ${SOURCE_SKILL_DIR}" >&2
    exit 1
fi

# 3. 建立目標目錄並複製技能檔案
echo "==> 複製技能檔案至 ${TARGET_SKILL_DIR} ..."
mkdir -p "${TARGET_SKILL_DIR}/scripts" "${TARGET_SKILL_DIR}/references"

cp -r "${SOURCE_SKILL_DIR}/SKILL.md" "${TARGET_SKILL_DIR}/"
cp -r "${SOURCE_SKILL_DIR}/scripts/"* "${TARGET_SKILL_DIR}/scripts/"
cp -r "${SOURCE_SKILL_DIR}/references/"* "${TARGET_SKILL_DIR}/references/"

chmod +x "${TARGET_SKILL_DIR}/scripts/tomboy_note_writer.py"

# 4. 配置全域 AGENTS.md 規則
echo "==> 配置全域提示指令 (${AGENTS_MD}) ..."
mkdir -p "${TARGET_BASE_DIR}"

if [ ! -f "${AGENTS_MD}" ]; then
    touch "${AGENTS_MD}"
fi

if ! grep -q "${DIRECTIVE_MARKER}" "${AGENTS_MD}"; then
    echo "${DIRECTIVE_CONTENT}" >> "${AGENTS_MD}"
    echo "已成功將歸檔引導指令加入 ${AGENTS_MD}"
else
    echo "提示: ${AGENTS_MD} 中已存在 Tomboy 歸檔指令，略過追加。"
fi

echo ""
echo "============================================================"
echo "  安裝成功！"
echo "============================================================"
echo "技能路徑: ${TARGET_SKILL_DIR}"
echo "轉換工具: ${TARGET_SKILL_DIR}/scripts/tomboy_note_writer.py"
echo "全域規則: ${AGENTS_MD}"
echo ""
echo "在任何 antigravity-cli 對話串中，只需對 Agent 說："
echo "  「請將剛才討論的工作流程歸檔至 Tomboy 筆記，放在『共用 - 自由軟體』筆記本」"
echo "即可自動建立或更新合規的 Tomboy 筆記！"
echo "============================================================"
