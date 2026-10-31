#!/usr/bin/env python3
"""
tomboy_note_writer.py
---------------------
Utility to create or update Tomboy notes from Markdown format.
Ensures full compatibility with Tomboy's native XML specification:
  - Generates valid UUIDs and RFC 3339 timestamps with timezone.
  - Correctly maps Markdown (Headings, bold, italic, monospace, bullets/indents, links)
    to Tomboy XML elements (<size:large>, <bold>, <italic>, <monospace>, <list><list-item dir="ltr">).
  - Handles notebook tagging via <tag>system:notebook:... </tag>.
  - Safely creates new notes or updates existing notes by title.
"""

import sys
import os
import re
import uuid
import datetime
import xml.etree.ElementTree as ET
from pathlib import Path

TOMBOY_DIR = Path.home() / ".local" / "share" / "tomboy"

def get_current_timestamp():
    # Format: 2026-10-31T16:40:56.0240210+08:00
    now = datetime.datetime.now(datetime.timezone.utc).astimezone()
    # ISO 8601 with microseconds and offset
    return now.isoformat(timespec='microseconds')

def escape_xml(text):
    return (text
            .replace('&', '&amp;')
            .replace('<', '&lt;')
            .replace('>', '&gt;'))

# Common LaTeX math symbols/commands matching Tomboy's LatexNoteSession
COMMON_MATH_REGEX = re.compile(
    r'(\\[a-zA-Z]+|[_^=+\-*/<>|\\]|\\pm|\\times|\\div|\\cdot|\\sum|\\int|\\prod|\\sqrt|\\frac)'
)

def normalize_markdown_latex(md_body):
    """
    Normalizes multiline $$...$$ and \[...\] blocks by collapsing multiline whitespace
    so that internal lines are not misparsed by markdown line parsers.
    """
    def repl_dollar(m):
        inner = re.sub(r'\s+', ' ', m.group(1).strip())
        return f"\\[{inner}\\]"
    md_body = re.sub(r'\$\$(.*?)\$\$', repl_dollar, md_body, flags=re.DOTALL)

    def repl_bracket(m):
        inner = re.sub(r'\s+', ' ', m.group(1).strip())
        return f"\\[{inner}\\]"
    md_body = re.sub(r'\\\[(.*?)\\\]', repl_bracket, md_body, flags=re.DOTALL)

    return md_body

def parse_markdown_inlines(text):
    """
    Parses inline markdown like **bold**, *italic*, `code`, and [label](link) or URLs.
    Also handles and protects LaTeX math blocks (\[...\], $$...$$, $...$).
    Returns Tomboy XML formatted inline snippet.
    """
    # 1. Escape basic XML
    # We will use UUID-like placeholders without underscores to avoid italic regex collision
    placeholders = []
    def save_tag(content):
        idx = len(placeholders)
        placeholders.append(content)
        return f"\x00TMBPH{idx}\x00"

    # Pre-protect backtick code blocks `code` -> <monospace>code</monospace>
    def code_repl(m):
        code_text = escape_xml(m.group(1))
        return save_tag(f"<monospace>{code_text}</monospace>")
    text = re.sub(r'`([^`]+)`', code_repl, text)

    # Pre-protect and normalize LaTeX math expressions:
    # 1. Explicit \[ ... \] (Tomboy native LaTeX delimiter)
    def latex_bracket_repl(m):
        return save_tag(f"\\[{escape_xml(m.group(1))}\\]")
    text = re.sub(r'\\\[(.*?)\\\]', latex_bracket_repl, text, flags=re.DOTALL)

    # 2. Markdown display math $$ ... $$ -> convert to \[ ... \]
    def latex_display_dollar_repl(m):
        return save_tag(f"\\[{escape_xml(m.group(1))}\\]")
    text = re.sub(r'\$\$(.*?)\$\$', latex_display_dollar_repl, text, flags=re.DOTALL)

    # 3. Inline math $...$ -> convert to \[...\] only if it contains LaTeX math symbols
    def latex_inline_dollar_repl(m):
        inner = m.group(1)
        if COMMON_MATH_REGEX.search(inner):
            return save_tag(f"\\[{escape_xml(inner)}\\]")
        return m.group(0) # Keep currency or shell variables unchanged
    text = re.sub(r'(?<!\$)\$([^\$\n]+)\$(?!\$)', latex_inline_dollar_repl, text)

    # Pre-protect URLs [text](url) -> if text == url: <link:url>url</link:url> else: text <link:url>url</link:url>
    def link_repl(m):
        lbl = m.group(1)
        url = m.group(2)
        if lbl == url or not lbl:
            return save_tag(f"<link:url>{escape_xml(url)}</link:url>")
        else:
            return save_tag(f"{escape_xml(lbl)} (<link:url>{escape_xml(url)}</link:url>)")
    text = re.sub(r'\[([^\]]+)\]\((https?://[^\)]+)\)', link_repl, text)

    # Raw URLs (http:// or https://)
    def raw_url_repl(m):
        raw_url = m.group(0)
        return save_tag(f"<link:url>{escape_xml(raw_url)}</link:url>")
    text = re.sub(r'https?://[^\s<>"]+', raw_url_repl, text)

    # Bold **text** or __text__
    def bold_repl(m):
        b_text = escape_xml(m.group(1))
        return save_tag(f"<bold>{b_text}</bold>")
    text = re.sub(r'\*\*([^*]+)\*\*', bold_repl, text)
    text = re.sub(r'__([^_]+)__', bold_repl, text)

    # Italic *text* or _text_
    def italic_repl(m):
        i_text = escape_xml(m.group(1))
        return save_tag(f"<italic>{i_text}</italic>")
    text = re.sub(r'\*([^*]+)\*', italic_repl, text)
    text = re.sub(r'(?<![a-zA-Z0-9])_([^_]+)_(?![a-zA-Z0-9])', italic_repl, text)

    # Escape remaining text
    text = escape_xml(text)

    # Restore placeholders
    for idx, ph in enumerate(placeholders):
        text = text.replace(f"\x00TMBPH{idx}\x00", ph)

    return text

def markdown_to_tomboy_content(title, md_body):
    """
    Converts markdown body lines into Tomboy <note-content> body with nested lists and size tags.
    """
    # Normalize multiline LaTeX expressions first
    md_body = normalize_markdown_latex(md_body)

    lines = md_body.replace('\r\n', '\n').replace('\r', '\n').split('\n')
    
    output = []
    output.append(f"{escape_xml(title.strip())}\n")

    current_list_depth = 0 # 0 means not in list, 1 means <list>, 2 means nested <list>, etc.

    def close_lists_to_depth(target_depth):
        nonlocal current_list_depth
        res = []
        while current_list_depth > target_depth:
            res.append("</list-item></list>")
            current_list_depth -= 1
        return "".join(res)

    for line in lines:
        stripped = line.strip()
        if not stripped:
            if current_list_depth > 0:
                output.append(close_lists_to_depth(0))
            output.append("\n")
            continue

        # Check Headings (#, ##, ###)
        heading_match = re.match(r'^(#{1,3})\s+(.*)$', line)
        if heading_match:
            if current_list_depth > 0:
                output.append(close_lists_to_depth(0))
            level = len(heading_match.group(1))
            h_text = parse_markdown_inlines(heading_match.group(2))
            if level == 1:
                output.append(f"\n<size:huge>{h_text}</size:huge>\n")
            elif level == 2:
                output.append(f"\n<size:large>{h_text}</size:large>\n")
            else:
                output.append(f"\n<bold>{h_text}</bold>\n")
            continue

        # Check Bullet Lists (•, ◦, ‣, -, *, + or indented)
        list_match = re.match(r'^([ \t]*)([•◦‣\-\*\+])\s+(.*)$', line)
        if list_match:
            indent_str = list_match.group(1)
            bullet = list_match.group(2)
            item_text = parse_markdown_inlines(list_match.group(3))

            # Determine depth (1-based for XML <list>)
            depth = 1
            if bullet == '•':
                depth = 1
            elif bullet == '◦':
                depth = 2
            elif bullet == '‣':
                depth = 3
            else:
                spaces = len(indent_str.replace('\t', '    '))
                depth = 1 + (spaces // 2)

            if depth > current_list_depth:
                # Open nested lists
                while current_list_depth < depth:
                    output.append("<list><list-item dir=\"ltr\">")
                    current_list_depth += 1
                output.append(item_text)
            elif depth == current_list_depth:
                # Same depth: close previous item and start next
                output.append("</list-item><list-item dir=\"ltr\">")
                output.append(item_text)
            else:
                # Lesser depth: close down
                while current_list_depth > depth:
                    output.append("</list-item></list>")
                    current_list_depth -= 1
                output.append("</list-item><list-item dir=\"ltr\">")
                output.append(item_text)
            continue

        # Normal text line
        if current_list_depth > 0:
            output.append(close_lists_to_depth(0))

        output.append(parse_markdown_inlines(line) + "\n")

    if current_list_depth > 0:
        output.append(close_lists_to_depth(0))

    return "".join(output)

def find_existing_note_by_title(title):
    if not TOMBOY_DIR.exists():
        return None, None
    for note_path in TOMBOY_DIR.glob("*.note"):
        try:
            tree = ET.parse(note_path)
            root = tree.getroot()
            title_el = root.find('{http://beatniksoftware.com/tomboy}title')
            if title_el is not None and title_el.text and title_el.text.strip().lower() == title.strip().lower():
                return note_path, tree
        except:
            continue
    return None, None

def write_tomboy_note(title, markdown_content, notebook=None, note_path=None):
    TOMBOY_DIR.mkdir(parents=True, exist_ok=True)

    existing_path, existing_tree = (note_path, ET.parse(note_path)) if note_path else find_existing_note_by_title(title)

    now_ts = get_current_timestamp()
    if existing_path and existing_tree:
        note_id = existing_path.stem
        root = existing_tree.getroot()
        created_el = root.find('{http://beatniksoftware.com/tomboy}create-date')
        create_ts = created_el.text if created_el is not None and created_el.text else now_ts
        target_file = existing_path
        action = "Updated"
    else:
        note_id = str(uuid.uuid4())
        create_ts = now_ts
        target_file = TOMBOY_DIR / f"{note_id}.note"
        action = "Created"

    xml_body = markdown_to_tomboy_content(title, markdown_content)

    tags_xml = ""
    if notebook:
        tags_xml = f"""  <tags>
    <tag>system:notebook:{escape_xml(notebook.strip())}</tag>
  </tags>
"""

    note_xml = f"""<?xml version="1.0" encoding="utf-8"?>
<note version="0.3" xmlns:link="http://beatniksoftware.com/tomboy/link" xmlns:size="http://beatniksoftware.com/tomboy/size" xmlns="http://beatniksoftware.com/tomboy">
  <title>{escape_xml(title.strip())}</title>
  <text xml:space="preserve"><note-content version="0.1">{xml_body}</note-content></text>
  <last-change-date>{now_ts}</last-change-date>
  <last-metadata-change-date>{now_ts}</last-metadata-change-date>
  <create-date>{create_ts}</create-date>
  <cursor-position>0</cursor-position>
  <selection-bound-position>-1</selection-bound-position>
  <width>650</width>
  <height>500</height>
  <x>-1</x>
  <y>-1</y>
{tags_xml}  <open-on-startup>False</open-on-startup>
</note>
"""

    with open(target_file, "w", encoding="utf-8") as f:
        f.write(note_xml)

    print(f"[{action}] Tomboy note saved: {target_file}")
    print(f"Title: {title}")
    if notebook:
        print(f"Notebook: {notebook}")
    return target_file

def main():
    import argparse
    parser = argparse.ArgumentParser(description="Create or update Tomboy notes from Markdown.")
    parser.add_argument("--title", "-t", required=True, help="Note title")
    parser.add_argument("--notebook", "-n", default=None, help="Notebook name (optional)")
    parser.add_argument("--file", "-f", default=None, help="Markdown file to read content from")
    parser.add_argument("--content", "-c", default=None, help="Markdown content string")

    args = parser.parse_args()

    if args.file:
        with open(args.file, "r", encoding="utf-8") as f:
            content = f.read()
    elif args.content:
        content = args.content
    else:
        # Read from stdin
        if not sys.stdin.isatty():
            content = sys.stdin.read()
        else:
            print("Error: No content provided via --file, --content, or stdin.", file=sys.stderr)
            sys.exit(1)

    write_tomboy_note(args.title, content, args.notebook)

if __name__ == "__main__":
    main()
