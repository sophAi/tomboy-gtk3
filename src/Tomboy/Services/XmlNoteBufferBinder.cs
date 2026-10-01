using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Gtk;
using Pango;

namespace Tomboy.Services
{
    public class DepthInfo
    {
        public int Depth { get; }
        public Pango.Direction Direction { get; }
        public TextTag Tag { get; }

        public DepthInfo(int depth, Pango.Direction direction, TextTag tag)
        {
            Depth = depth;
            Direction = direction;
            Tag = tag;
        }
    }

    public class XmlNoteBufferBinder
    {
        public static readonly char[] IndentBullets = { '•', '◦', '‣' };

        /// <summary>
        /// Registers all common Tomboy tags in a TextBuffer's tag table.
        /// </summary>
        public static void EnsureCommonTags(TextBuffer buffer)
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero) return;

            var table = buffer.TagTable;

            RegisterTag(table, "bold", tag => tag.Weight = Weight.Bold);
            RegisterTag(table, "italic", tag => tag.Style = Pango.Style.Italic);
            RegisterTag(table, "underline", tag => tag.Underline = Pango.Underline.Single);
            RegisterTag(table, "strikethrough", tag => tag.Strikethrough = true);
            RegisterTag(table, "highlight", tag => {
                tag.Background = "#FFF066";
                tag.Foreground = "#000000";
            });
            RegisterTag(table, "datetime", tag => {
                tag.Scale = 0.85;
                tag.Style = Pango.Style.Italic;
                tag.Foreground = "#777777";
            });
            RegisterTag(table, "huge", tag => {
                tag.Scale = 2.0;
            });
            RegisterTag(table, "size:huge", tag => {
                tag.Scale = 2.0;
            });
            RegisterTag(table, "large", tag => {
                tag.Scale = 1.4;
            });
            RegisterTag(table, "size:large", tag => {
                tag.Scale = 1.4;
            });
            RegisterTag(table, "small", tag => {
                tag.Scale = 0.85;
            });
            RegisterTag(table, "size:small", tag => {
                tag.Scale = 0.85;
            });
            RegisterTag(table, "note-title", tag => {
                tag.Scale = 2.0;
                tag.Foreground = "#005A9E";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "monospace", tag => {
                tag.Family = "Monospace";
            });
            RegisterTag(table, "link:internal", tag => {
                tag.Foreground = "#005A9E";
                tag.Underline = Pango.Underline.Single;
                tag.Weight = Weight.Bold;
            });
            RegisterTag(table, "link:url", tag => {
                tag.Foreground = "#0255B2";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "link:broken", tag => {
                tag.Foreground = "#888888";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "find-match", tag => {
                tag.Background = "#FFF59D";
                tag.Foreground = "#000000";
            });
            RegisterTag(table, "find-match-current", tag => {
                tag.Background = "#FF6D00";
                tag.Foreground = "#FFFFFF";
                tag.Weight = Weight.Bold;
            });
            RegisterTag(table, "latex_code", tag => {
                tag.Invisible = true;
            });
            RegisterTag(table, "latex_image", tag => {
            });
        }

        private static void RegisterTag(TextTagTable table, string tagName, Action<TextTag> init)
        {
            if (table.Lookup(tagName) == null)
            {
                var tag = new TextTag(tagName);
                init(tag);
                table.Add(tag);
            }
        }

        public static TextTag GetOrCreateDepthTag(TextBuffer buffer, int depth, Pango.Direction direction = Pango.Direction.Ltr)
        {
            string tagName = $"depth:{depth}:{direction}";
            TextTag existing = buffer.TagTable.Lookup(tagName);
            if (existing != null) return existing;

            var depthTag = new TextTag(tagName)
            {
                Indent = -14,
                LeftMargin = direction == Pango.Direction.Ltr ? (depth + 1) * 25 : 0,
                RightMargin = direction == Pango.Direction.Rtl ? (depth + 1) * 25 : 0,
                PixelsBelowLines = 4,
                Scale = Pango.Scale.Medium
            };
            buffer.TagTable.Add(depthTag);
            return depthTag;
        }

        public static TextTag GetOrCreateLinkTag(TextBuffer buffer, string linkTarget)
        {
            string tagName = $"note_link_{linkTarget.GetHashCode()}";
            TextTag tag = buffer.TagTable.Lookup(tagName);
            if (tag != null) return tag;

            tag = new TextTag(tagName)
            {
                Foreground = "#005A9E",
                Underline = Pango.Underline.Single,
                Weight = Weight.Bold
            };

            buffer.TagTable.Add(tag);
            return tag;
        }

        /// <summary>
        /// Deserializes Tomboy XML format into a GTK TextBuffer with newline normalization and soft break support.
        /// </summary>
        public static void LoadXmlToBuffer(TextBuffer buffer, string xmlContent, List<string>? existingNoteTitles = null, string currentNoteTitle = "")
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero)
                return;

            EnsureCommonTags(buffer);
            buffer.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(xmlContent))
                return;

            // Normalize line endings to standard Unix newline '\n'
            xmlContent = xmlContent.Replace("\r\n", "\n").Replace("\r", "\n");

            try
            {
                DeserializeXml(buffer, xmlContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deserializing Tomboy XML: {ex.Message}");
                if (buffer.Handle != IntPtr.Zero)
                {
                    buffer.Text = xmlContent;
                }
            }

            // Ensure first line has Title formatting
            ApplyTitleTagToFirstLine(buffer);

            // Highlight External URLs
            HighlightExternalUrls(buffer);

            // Interlink Known Note Titles
            if (existingNoteTitles != null && existingNoteTitles.Count > 0)
            {
                HighlightNoteTitleLinks(buffer, existingNoteTitles, currentNoteTitle);
            }
        }

        private static void DeserializeXml(TextBuffer buffer, string xmlContent)
        {
            var stringReader = new StringReader(xmlContent);
            using var xml = new XmlTextReader(stringReader) { Namespaces = false, WhitespaceHandling = WhitespaceHandling.All };

            int offset = 0;
            var stack = new Stack<(int Start, TextTag? Tag)>();
            int currentDepth = -1;
            var listStack = new Stack<bool>();

            while (xml.Read())
            {
                switch (xml.NodeType)
                {
                    case XmlNodeType.Element:
                        string elementName = xml.Name.ToLowerInvariant();
                        if (elementName == "note-content") break;

                        if (elementName == "list")
                        {
                            currentDepth++;
                            break;
                        }
                        else if (elementName == "list-item")
                        {
                            if (currentDepth >= 0)
                            {
                                Pango.Direction dir = xml.GetAttribute("dir") == "rtl" ? Pango.Direction.Rtl : Pango.Direction.Ltr;
                                TextTag depthTag = GetOrCreateDepthTag(buffer, currentDepth, dir);
                                stack.Push((offset, depthTag));
                                listStack.Push(false);
                            }
                            break;
                        }

                        TextTag? tag = MapElementToTag(buffer, elementName);
                        stack.Push((offset, tag));
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        string textVal = xml.Value;
                        TextIter insertAt = buffer.GetIterAtOffset(offset);
                        buffer.Insert(ref insertAt, textVal);
                        offset += textVal.Length;

                        if (listStack.Count > 0)
                        {
                            listStack.Pop();
                            listStack.Push(true);
                        }
                        break;

                    case XmlNodeType.EndElement:
                        string endElement = xml.Name.ToLowerInvariant();
                        if (endElement == "note-content") break;

                        if (endElement == "list")
                        {
                            currentDepth--;
                            break;
                        }

                        if (stack.Count > 0)
                        {
                            var (tagStart, tagInstance) = stack.Pop();
                            if (tagInstance != null && tagInstance.Name != null && tagInstance.Name.StartsWith("depth:") && listStack.Count > 0 && listStack.Pop())
                            {
                                TextIter applyStart = buffer.GetIterAtOffset(tagStart);
                                var parts = tagInstance.Name.Split(':');
                                int d = (parts.Length >= 2 && int.TryParse(parts[1], out int parsedD)) ? parsedD : 0;
                                char bulletChar = IndentBullets[d % IndentBullets.Length];
                                string bulletText = $"{bulletChar} ";

                                // Insert bullet specifically with depthTag (tag ONLY on the bullet characters)
                                buffer.InsertWithTags(ref applyStart, bulletText, tagInstance);
                                offset += bulletText.Length;
                            }
                            else if (tagInstance != null)
                            {
                                TextIter applyStart = buffer.GetIterAtOffset(tagStart);
                                TextIter applyEnd = buffer.GetIterAtOffset(offset);
                                buffer.ApplyTag(tagInstance, applyStart, applyEnd);
                            }
                        }
                        break;
                }
            }
        }

        private static TextTag? MapElementToTag(TextBuffer buffer, string elementName)
        {
            EnsureCommonTags(buffer);

            return elementName switch
            {
                "bold" or "b" => buffer.TagTable.Lookup("bold"),
                "italic" or "i" => buffer.TagTable.Lookup("italic"),
                "underline" or "u" => buffer.TagTable.Lookup("underline"),
                "strikethrough" or "s" => buffer.TagTable.Lookup("strikethrough"),
                "highlight" => buffer.TagTable.Lookup("highlight"),
                "datetime" => buffer.TagTable.Lookup("datetime"),
                "size:huge" or "huge" => buffer.TagTable.Lookup("size:huge"),
                "size:large" or "large" => buffer.TagTable.Lookup("size:large"),
                "size:small" or "small" => buffer.TagTable.Lookup("size:small"),
                "size:title" or "title" or "note-title" => buffer.TagTable.Lookup("note-title"),
                "monospace" or "fixed-width" => buffer.TagTable.Lookup("monospace"),
                "link:internal" or "internal" => buffer.TagTable.Lookup("link:internal"),
                "link:url" or "url" => buffer.TagTable.Lookup("link:url"),
                "link:broken" => buffer.TagTable.Lookup("link:broken"),
                _ => buffer.TagTable.Lookup(elementName)
            };
        }

        public static void ApplyTitleTagToFirstLine(TextBuffer buffer)
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero) return;

            TextIter start = buffer.StartIter;
            TextIter end = start;
            end.ForwardToLineEnd();

            EnsureCommonTags(buffer);
            TextTag? titleTag = buffer.TagTable.Lookup("note-title");

            if (titleTag != null)
            {
                if (start.Offset != end.Offset)
                {
                    buffer.ApplyTag(titleTag, start, end);
                }

                if (!end.IsEnd)
                {
                    TextIter rest = end;
                    if (rest.ForwardChar())
                    {
                        buffer.RemoveTag(titleTag, rest, buffer.EndIter);
                    }
                }
            }
        }

        public static readonly Regex UrlRegex = new Regex(
            @"(?<url>" +
                @"(?<scheme>(https?|ftp|sftp|file|irc|ircs|news|ssh|git|magnet|tel)://|mailto:)[^\s<>""'，。！？（）《》【】]+" +
                @"|\b(?<www>(www|ftp)\.[a-zA-Z0-9\-]+(\.[a-zA-Z0-9\-]+)+(/[^\s<>""'，。！？（）《》【】]*)?)" +
                @"|\b(?<email>[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})\b" +
                @"|(?<=^|\s)(?<path>(~/[a-zA-Z0-9_\-./]+|/[a-zA-Z0-9_\-.]+/[a-zA-Z0-9_\-./]*))" +
            @")",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly char[] TrailingUrlPunctuation = new char[]
        {
            '.', ',', ';', ':', '!', '?', '"', '\'', ')', ']', '}', '>', '»',
            '。', '，', '、', '；', '：', '！', '？', '）', '」', '』', '》', '】'
        };

        public static (int start, int length, string cleanUrl) CleanUrlMatch(Match m)
        {
            string val = m.Value;
            while (val.Length > 0 && Array.IndexOf(TrailingUrlPunctuation, val[^1]) >= 0)
            {
                if (val[^1] == ')' && val.Contains('(') && CountChar(val, '(') >= CountChar(val, ')'))
                    break;
                if (val[^1] == ']' && val.Contains('[') && CountChar(val, '[') >= CountChar(val, ']'))
                    break;
                val = val.Substring(0, val.Length - 1);
            }
            return (m.Index, val.Length, val);
        }

        private static int CountChar(string s, char c)
        {
            int count = 0;
            foreach (char ch in s) if (ch == c) count++;
            return count;
        }

        public static string NormalizeUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string url = raw.Trim();

            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }
            else if (url.StartsWith("ftp.", StringComparison.OrdinalIgnoreCase))
            {
                url = "ftp://" + url;
            }
            else if (url.StartsWith("~/"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                url = "file://" + Path.Combine(home, url.Substring(2));
            }
            else if (url.StartsWith("/") && url.IndexOf('/', 1) > 0)
            {
                url = "file://" + url;
            }
            else if (!url.Contains("://") && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && url.Contains('@'))
            {
                url = "mailto:" + url;
            }

            return url;
        }

        public static void OpenUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return;
            string url = NormalizeUrl(rawUrl);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    System.Diagnostics.Process.Start("xdg-open", url);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening URL '{url}': {ex.Message}");
                }
            }
        }

        public static void HighlightExternalUrls(TextBuffer buffer)
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero) return;

            string text = buffer.Text;
            if (string.IsNullOrEmpty(text)) return;

            TextTag urlTag = buffer.TagTable.Lookup("link:url") ?? new TextTag("link:url")
            {
                Foreground = "#0255B2",
                Underline = Pango.Underline.Single
            };

            if (buffer.TagTable.Lookup("link:url") == null)
            {
                buffer.TagTable.Add(urlTag);
            }

            TextTag? latexCodeTag = buffer.TagTable.Lookup("latex_code");
            TextTag? latexImageTag = buffer.TagTable.Lookup("latex_image");

            buffer.RemoveTag(urlTag, buffer.StartIter, buffer.EndIter);

            var matches = UrlRegex.Matches(text);
            TextIter searchIter = buffer.StartIter;

            foreach (Match m in matches)
            {
                if (!m.Success) continue;

                var (startOffset, length, cleanUrl) = CleanUrlMatch(m);
                if (length <= 0) continue;

                TextIter start;
                TextIter end;

                if (startOffset >= 0 && startOffset + length <= buffer.CharCount)
                {
                    start = buffer.GetIterAtOffset(startOffset);
                    end = buffer.GetIterAtOffset(startOffset + length);
                    if (buffer.GetText(start, end, false) != cleanUrl)
                    {
                        if (!searchIter.ForwardSearch(cleanUrl, TextSearchFlags.VisibleOnly, out start, out end, buffer.EndIter))
                            continue;
                    }
                }
                else
                {
                    if (!searchIter.ForwardSearch(cleanUrl, TextSearchFlags.VisibleOnly, out start, out end, buffer.EndIter))
                        continue;
                }

                searchIter = end;

                // Do not tag inside LaTeX formulas or images
                if (latexCodeTag != null && start.HasTag(latexCodeTag)) continue;
                if (latexImageTag != null && start.HasTag(latexImageTag)) continue;

                buffer.ApplyTag(urlTag, start, end);
            }
        }

        public static void HighlightNoteTitleLinks(TextBuffer buffer, List<string> noteTitles, string currentNoteTitle = "")
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero) return;

            string bufferText = buffer.Text;
            if (string.IsNullOrEmpty(bufferText)) return;

            TextIter line1End = buffer.StartIter;
            line1End.ForwardToLineEnd();
            int contentStartOffset = line1End.Offset;
            if (contentStartOffset >= bufferText.Length) return;

            TextTag? internalLinkTag = buffer.TagTable.Lookup("link:internal");
            TextTag? urlTag = buffer.TagTable.Lookup("link:url");
            TextTag? latexCodeTag = buffer.TagTable.Lookup("latex_code");
            TextTag? latexImageTag = buffer.TagTable.Lookup("latex_image");

            TextIter contentStart = line1End;
            TextIter contentEnd = buffer.EndIter;

            // Remove existing note links from content area before re-scanning
            if (internalLinkTag != null)
            {
                buffer.RemoveTag(internalLinkTag, contentStart, contentEnd);
            }
            foreach (string title in noteTitles)
            {
                string tagName = $"note_link_{title.GetHashCode()}";
                TextTag? tag = buffer.TagTable.Lookup(tagName);
                if (tag != null)
                {
                    buffer.RemoveTag(tag, contentStart, contentEnd);
                }
            }

            var sortedTitles = noteTitles
                .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length >= 2 && !string.Equals(t, currentNoteTitle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Length)
                .ToList();

            var taggedRanges = new List<(int start, int end)>();

            foreach (string title in sortedTitles)
            {
                int searchStart = contentStartOffset;
                while (searchStart < bufferText.Length)
                {
                    int index = bufferText.IndexOf(title, searchStart, StringComparison.OrdinalIgnoreCase);
                    if (index == -1) break;

                    int endIndex = index + title.Length;
                    searchStart = endIndex;

                    // Word boundary check (supports ASCII words and CJK ideographs)
                    if (!IsWordBoundary(bufferText, index, title.Length))
                        continue;

                    // Skip if overlapping with a longer title match
                    bool overlaps = false;
                    foreach (var (rStart, rEnd) in taggedRanges)
                    {
                        if (index < rEnd && endIndex > rStart)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;

                    TextIter startIter = buffer.GetIterAtOffset(index);
                    TextIter endIter = buffer.GetIterAtOffset(endIndex);

                    // Do not create links inside URLs or LaTeX formulas
                    if (urlTag != null && (startIter.HasTag(urlTag) || endIter.HasTag(urlTag))) continue;
                    if (latexCodeTag != null && startIter.HasTag(latexCodeTag)) continue;
                    if (latexImageTag != null && startIter.HasTag(latexImageTag)) continue;

                    TextTag linkTag = GetOrCreateLinkTag(buffer, title);
                    buffer.ApplyTag(linkTag, startIter, endIter);
                    taggedRanges.Add((index, endIndex));
                }
            }
        }

        private static bool IsWordBoundary(string text, int startIdx, int length)
        {
            int endIdx = startIdx + length;

            if (startIdx > 0)
            {
                char prev = text[startIdx - 1];
                char first = text[startIdx];
                if (IsAlphaNumericAscii(prev) && IsAlphaNumericAscii(first))
                    return false;
            }

            if (endIdx < text.Length)
            {
                char last = text[endIdx - 1];
                char next = text[endIdx];
                if (IsAlphaNumericAscii(last) && IsAlphaNumericAscii(next))
                    return false;
            }

            return true;
        }

        private static bool IsAlphaNumericAscii(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        /// <summary>
        /// Serializes a GTK TextBuffer into compliant Tomboy note-content XML preserving exact newlines and soft line breaks.
        /// </summary>
        public static string ExportBufferToXml(TextBuffer buffer, string title)
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero)
                return string.Empty;

            var stream = new StringWriter();
            using var xml = new XmlTextWriter(stream) { Formatting = Formatting.None };

            xml.WriteStartElement(null, "note-content", null);
            xml.WriteAttributeString("version", "0.1");
            xml.WriteAttributeString("xmlns:link", "http://beatniksoftware.com/tomboy/link");
            xml.WriteAttributeString("xmlns:size", "http://beatniksoftware.com/tomboy/size");
            xml.WriteAttributeString("xml:space", "preserve");

            var tagStack = new Stack<TextTag>();
            var replayStack = new Stack<TextTag>();

            int prevDepth = -1;
            int prevDepthLine = -1;
            bool lineHasDepth = false;

            TextIter iter = buffer.StartIter;
            TextIter nextIter = buffer.StartIter;
            nextIter.ForwardChar();

            while (!iter.IsEnd)
            {
                DepthInfo? depthInfo = FindDepthTag(iter);

                if (depthInfo != null && iter.StartsLine())
                {
                    lineHasDepth = true;

                    if (iter.Line == prevDepthLine + 1)
                    {
                        if (depthInfo.Depth == prevDepth)
                        {
                            xml.WriteEndElement(); // close previous <list-item>
                        }
                        else if (depthInfo.Depth > prevDepth)
                        {
                            xml.WriteStartElement(null, "list", null);
                            for (int i = prevDepth + 2; i <= depthInfo.Depth; i++)
                            {
                                xml.WriteStartElement(null, "list-item", null);
                                xml.WriteAttributeString("dir", depthInfo.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                                xml.WriteStartElement(null, "list", null);
                            }
                        }
                        else
                        {
                            xml.WriteEndElement(); // close previous <list-item>
                            for (int i = prevDepth; i > depthInfo.Depth; i--)
                            {
                                xml.WriteEndElement(); // close <list>
                                xml.WriteEndElement(); // close <list-item>
                            }
                        }
                    }
                    else
                    {
                        xml.WriteStartElement(null, "list", null);
                        for (int i = 1; i <= depthInfo.Depth; i++)
                        {
                            xml.WriteStartElement(null, "list-item", null);
                            xml.WriteAttributeString("dir", depthInfo.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                            xml.WriteStartElement(null, "list", null);
                        }
                    }

                    prevDepth = depthInfo.Depth;
                    xml.WriteStartElement(null, "list-item", null);
                    xml.WriteAttributeString("dir", depthInfo.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                }

                // Output any tags beginning at current position
                foreach (TextTag tag in iter.Tags)
                {
                    if (iter.BeginsTag(tag) && TagIsSerializable(tag))
                    {
                        WriteTagStart(xml, tag);
                        tagStack.Push(tag);
                    }
                }

                // Handle soft line breaks, bullet characters, and omit pixbuf / \uFFFC characters
                if (iter.Char == "\u2028")
                {
                    xml.WriteCharEntity('\u2028');
                }
                else if (iter.Pixbuf == null && iter.Char != "\uFFFC")
                {
                    bool isBulletChar = depthInfo != null && (iter.LineOffset == 0 || iter.LineOffset == 1) && IsBulletPrefix(iter);
                    if (!isBulletChar && !string.IsNullOrEmpty(iter.Char))
                    {
                        xml.WriteString(iter.Char);
                    }
                }

                bool endOfDepthLine = lineHasDepth && nextIter.EndsLine();
                bool nextLineHasDepth = false;

                if (iter.Line < buffer.LineCount - 1)
                {
                    TextIter nextLine = buffer.GetIterAtLine(iter.Line + 1);
                    nextLineHasDepth = FindDepthTag(nextLine) != null;
                }

                if (endOfDepthLine || (nextLineHasDepth && nextIter.EndsLine()))
                {
                    while (tagStack.Count > 0)
                    {
                        TextTag t = tagStack.Pop();
                        WriteTagEnd(xml, t);
                    }
                }
                else
                {
                    foreach (TextTag tag in iter.Tags)
                    {
                        if (TagEndsHere(tag, iter, nextIter) && TagIsSerializable(tag))
                        {
                            while (tagStack.Count > 0)
                            {
                                TextTag existing = tagStack.Pop();
                                if (!TagEndsHere(existing, iter, nextIter))
                                {
                                    replayStack.Push(existing);
                                }
                                WriteTagEnd(xml, existing);
                            }

                            while (replayStack.Count > 0)
                            {
                                TextTag replayTag = replayStack.Pop();
                                tagStack.Push(replayTag);
                                WriteTagStart(xml, replayTag);
                            }
                        }
                    }
                }

                if (endOfDepthLine)
                {
                    lineHasDepth = false;
                    prevDepthLine = iter.Line;
                }

                if (endOfDepthLine && !nextLineHasDepth)
                {
                    for (int i = prevDepth; i > -1; i--)
                    {
                        xml.WriteEndElement(); // </list-item>
                        xml.WriteEndElement(); // </list>
                    }
                    prevDepth = -1;
                }

                iter.ForwardChar();
                nextIter.ForwardChar();
            }

            while (tagStack.Count > 0)
            {
                TextTag tailTag = tagStack.Pop();
                WriteTagEnd(xml, tailTag);
            }

            if (prevDepth > -1)
            {
                for (int i = prevDepth; i > -1; i--)
                {
                    xml.WriteEndElement(); // </list-item>
                    xml.WriteEndElement(); // </list>
                }
            }

            xml.WriteEndElement(); // </note-content>
            xml.Close();

            string serialized = stream.ToString();
            if (Environment.NewLine != "\n")
            {
                serialized = serialized.Replace(Environment.NewLine, "\n");
            }
            return serialized;
        }

        private static bool IsBulletPrefix(TextIter iter)
        {
            string ch = iter.Char;
            return ch == "•" || ch == "◦" || ch == "‣" || ch == " " || ch == "\t";
        }

        private static bool TagIsSerializable(TextTag tag)
        {
            string name = tag.Name ?? string.Empty;
            if (name.StartsWith("depth:") || name.StartsWith("indent_") || name == "ext_url" || name.StartsWith("find-match") || name == "note-title" || name.StartsWith("latex_"))
                return false;

            return true;
        }

        private static void WriteTagStart(XmlTextWriter xml, TextTag tag)
        {
            string name = tag.Name ?? string.Empty;
            if (name.StartsWith("note_link_"))
            {
                xml.WriteStartElement(null, "link:internal", null);
            }
            else
            {
                xml.WriteStartElement(null, name, null);
            }
        }

        private static void WriteTagEnd(XmlTextWriter xml, TextTag tag)
        {
            xml.WriteEndElement();
        }

        private static bool TagEndsHere(TextTag tag, TextIter iter, TextIter nextIter)
        {
            return (iter.HasTag(tag) && !nextIter.HasTag(tag)) || nextIter.IsEnd;
        }

        public static DepthInfo? FindDepthTag(TextIter iter)
        {
            foreach (TextTag t in iter.Tags)
            {
                if (t.Name != null && t.Name.StartsWith("depth:"))
                {
                    var parts = t.Name.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int d))
                    {
                        Pango.Direction dir = (parts.Length >= 3 && parts[2] == "Rtl") ? Pango.Direction.Rtl : Pango.Direction.Ltr;
                        return new DepthInfo(d, dir, t);
                    }
                }
            }
            return null;
        }
    }
}
