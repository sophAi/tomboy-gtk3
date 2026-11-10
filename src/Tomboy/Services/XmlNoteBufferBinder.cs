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
                string color = Preferences.Current.NoteTitleColor;
                tag.Foreground = !string.IsNullOrWhiteSpace(color) ? color : "#005A9E";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "monospace", tag => {
                tag.Family = "Monospace";
            });
            RegisterTag(table, "link:internal", tag => {
                string linkColor = Preferences.Current.NoteLinkColor;
                tag.Foreground = !string.IsNullOrWhiteSpace(linkColor) ? linkColor : "#005A9E";
                tag.Underline = Pango.Underline.Single;
                tag.Weight = Weight.Bold;
            });
            RegisterTag(table, "link:url", tag => {
                string urlColor = Preferences.Current.UrlLinkColor;
                tag.Foreground = !string.IsNullOrWhiteSpace(urlColor) ? urlColor : "#0255B2";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "link:broken", tag => {
                tag.Foreground = "#888888";
                tag.Underline = Pango.Underline.Single;
            });
            RegisterTag(table, "find-match", tag => {
                string matchColor = Preferences.Current.EnableCustomSearchMatchColor && !string.IsNullOrWhiteSpace(Preferences.Current.CustomSearchMatchColor)
                    ? Preferences.Current.CustomSearchMatchColor
                    : "#FFF59D";
                tag.Background = matchColor;
                tag.Foreground = "#000000";
            });
            RegisterTag(table, "find-match-current", tag => {
                tag.Background = "#FF6D00";
                tag.Foreground = "#FFFFFF";
                tag.Weight = Weight.Bold;
            });
            RegisterTag(table, "latex_code", tag => {
                // Do NOT use Invisible = true due to upstream GTK3 bug in gtktextlayout/gtktextiter.
                // Size = 1 (1 Pango unit = 1/1024 pt) shrinks text to 0 width safely.
                tag.Size = 1;
                tag.Editable = false;
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
            // Normalize non-RTL directions to Ltr
            direction = (direction == Pango.Direction.Rtl) ? Pango.Direction.Rtl : Pango.Direction.Ltr;

            string tagName = $"depth:{depth}:{direction}";
            TextTag existing = buffer.TagTable.Lookup(tagName);
            if (existing != null) return existing;

            var depthTag = new TextTag(tagName)
            {
                Indent = -14,
                LeftMargin = direction == Pango.Direction.Rtl ? 0 : (depth + 1) * 25,
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

            string linkColor = Preferences.Current.NoteLinkColor;
            tag = new TextTag(tagName)
            {
                Foreground = !string.IsNullOrWhiteSpace(linkColor) ? linkColor : "#005A9E",
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

            var stack = new Stack<(TextMark StartMark, TextTag? Tag)>();
            int currentDepth = -1;
            var listStack = new Stack<bool>();

            while (xml.Read())
            {
                switch (xml.NodeType)
                {
                    case XmlNodeType.Element:
                        {
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
                                    TextMark mark = buffer.CreateMark(null, buffer.EndIter, true);
                                    stack.Push((mark, depthTag));
                                    listStack.Push(false);
                                }
                                break;
                            }

                            TextTag? tag = MapElementToTag(buffer, elementName);
                            TextMark startMark = buffer.CreateMark(null, buffer.EndIter, true);
                            stack.Push((startMark, tag));
                            break;
                        }

                    case XmlNodeType.Text:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        {
                            string textVal = xml.Value;
                            TextIter insertAt = buffer.EndIter;
                            buffer.Insert(ref insertAt, textVal);

                            if (listStack.Count > 0)
                            {
                                listStack.Pop();
                                listStack.Push(true);
                            }
                            break;
                        }

                    case XmlNodeType.EndElement:
                        {
                            string endElement = xml.Name.ToLowerInvariant();
                            if (endElement == "note-content") break;

                            if (endElement == "list")
                            {
                                currentDepth--;
                                break;
                            }

                            if (stack.Count > 0)
                            {
                                var (tagStartMark, tagInstance) = stack.Pop();
                                if (tagInstance != null && tagInstance.Name != null && tagInstance.Name.StartsWith("depth:") && listStack.Count > 0 && listStack.Pop())
                                {
                                    TextIter applyStart = buffer.GetIterAtMark(tagStartMark);
                                    var parts = tagInstance.Name.Split(':');
                                    int d = (parts.Length >= 2 && int.TryParse(parts[1], out int parsedD)) ? parsedD : 0;
                                    char bulletChar = IndentBullets[d % IndentBullets.Length];
                                    string bulletText = $"{bulletChar} ";

                                    // Insert bullet specifically with depthTag (tag ONLY on the bullet characters)
                                    buffer.InsertWithTags(ref applyStart, bulletText, tagInstance);
                                }
                                else if (tagInstance != null)
                                {
                                    TextIter applyStart = buffer.GetIterAtMark(tagStartMark);
                                    TextIter applyEnd = buffer.EndIter;
                                    buffer.ApplyTag(tagInstance, applyStart, applyEnd);
                                }

                                buffer.DeleteMark(tagStartMark);
                            }
                            break;
                        }
                }
            }

            while (stack.Count > 0)
            {
                var (m, _) = stack.Pop();
                buffer.DeleteMark(m);
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

        public static void HighlightExternalUrls(TextBuffer buffer, TextIter? rangeStart = null, TextIter? rangeEnd = null)
        {
            if (buffer == null || buffer.Handle == IntPtr.Zero) return;

            string urlColor = Preferences.Current.UrlLinkColor;
            TextTag urlTag = buffer.TagTable.Lookup("link:url") ?? new TextTag("link:url")
            {
                Foreground = !string.IsNullOrWhiteSpace(urlColor) ? urlColor : "#0255B2",
                Underline = Pango.Underline.Single
            };

            if (buffer.TagTable.Lookup("link:url") == null)
            {
                buffer.TagTable.Add(urlTag);
            }

            TextTag? latexCodeTag = buffer.TagTable.Lookup("latex_code");
            TextTag? latexImageTag = buffer.TagTable.Lookup("latex_image");

            TextIter scanStart = rangeStart ?? buffer.StartIter;
            TextIter scanEnd = rangeEnd ?? buffer.EndIter;
            if (scanStart.Offset >= scanEnd.Offset) return;

            buffer.RemoveTag(urlTag, scanStart, scanEnd);

            string text = buffer.GetText(scanStart, scanEnd, false);
            if (string.IsNullOrEmpty(text)) return;

            int baseOffset = scanStart.Offset;
            var matches = UrlRegex.Matches(text);
            TextIter searchIter = scanStart;

            foreach (Match m in matches)
            {
                if (!m.Success) continue;

                var (startOffsetInChunk, length, cleanUrl) = CleanUrlMatch(m);
                if (length <= 0) continue;

                int absStartOffset = baseOffset + startOffsetInChunk;
                int absEndOffset = absStartOffset + length;

                TextIter start;
                TextIter end;

                if (absStartOffset >= 0 && absEndOffset <= buffer.CharCount)
                {
                    start = buffer.GetIterAtOffset(absStartOffset);
                    end = buffer.GetIterAtOffset(absEndOffset);
                    if (buffer.GetText(start, end, false) != cleanUrl)
                    {
                        if (!searchIter.ForwardSearch(cleanUrl, TextSearchFlags.VisibleOnly, out start, out end, scanEnd))
                            continue;
                    }
                }
                else
                {
                    if (!searchIter.ForwardSearch(cleanUrl, TextSearchFlags.VisibleOnly, out start, out end, scanEnd))
                        continue;
                }

                searchIter = end;

                // Do not tag inside LaTeX formulas or images
                if (latexCodeTag != null && start.HasTag(latexCodeTag)) continue;
                if (latexImageTag != null && start.HasTag(latexImageTag)) continue;

                buffer.ApplyTag(urlTag, start, end);
            }
        }

        internal class TitleTrieNode
        {
            public readonly Dictionary<char, TitleTrieNode> Children = new();
            public string? Title;
        }

        internal class TitleTrie
        {
            public readonly TitleTrieNode Root = new();

            public void Insert(string title)
            {
                if (string.IsNullOrWhiteSpace(title)) return;
                var current = Root;
                foreach (char c in title.ToLowerInvariant())
                {
                    if (!current.Children.TryGetValue(c, out var next))
                    {
                        next = new TitleTrieNode();
                        current.Children[c] = next;
                    }
                    current = next;
                }
                current.Title = title;
            }

            public List<(int start, int length, string title)> SearchAll(string text, int offsetAdjustment = 0)
            {
                var matches = new List<(int start, int length, string title)>();
                if (string.IsNullOrEmpty(text)) return matches;

                for (int i = 0; i < text.Length; i++)
                {
                    var node = Root;
                    int longestMatchLen = -1;
                    string? longestTitle = null;

                    for (int j = i; j < text.Length; j++)
                    {
                        char c = char.ToLowerInvariant(text[j]);
                        if (!node.Children.TryGetValue(c, out node))
                            break;

                        if (node.Title != null)
                        {
                            longestMatchLen = j - i + 1;
                            longestTitle = node.Title;
                        }
                    }

                    if (longestTitle != null && longestMatchLen >= 2)
                    {
                        matches.Add((i + offsetAdjustment, longestMatchLen, longestTitle));
                    }
                }

                return matches;
            }
        }

        public static void HighlightNoteTitleLinks(TextBuffer buffer, List<string> noteTitles, string currentNoteTitle = "", TextIter? rangeStart = null, TextIter? rangeEnd = null)
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

            TextIter scanStart = rangeStart ?? line1End;
            if (scanStart.Offset < line1End.Offset)
            {
                scanStart = line1End;
            }
            TextIter scanEnd = rangeEnd ?? buffer.EndIter;
            if (scanStart.Offset >= scanEnd.Offset) return;

            // Remove existing note links from the target scan area before re-scanning
            if (internalLinkTag != null)
            {
                buffer.RemoveTag(internalLinkTag, scanStart, scanEnd);
            }
            foreach (string title in noteTitles)
            {
                string tagName = $"note_link_{title.GetHashCode()}";
                TextTag? tag = buffer.TagTable.Lookup(tagName);
                if (tag != null)
                {
                    buffer.RemoveTag(tag, scanStart, scanEnd);
                }
            }

            var validTitles = noteTitles
                .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length >= 2 && !string.Equals(t, currentNoteTitle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (validTitles.Count == 0) return;

            // Build Trie dictionary for rapid linear-time multi-pattern matching
            var trie = new TitleTrie();
            foreach (var t in validTitles)
            {
                trie.Insert(t);
            }

            // Extract the chunk to search
            int baseOffset = scanStart.Offset;
            string chunkText = buffer.GetText(scanStart, scanEnd, false);
            var rawMatches = trie.SearchAll(chunkText, baseOffset);

            // Sort by length descending, then start offset ascending to prioritize longest matches
            var sortedMatches = rawMatches
                .OrderByDescending(m => m.length)
                .ThenBy(m => m.start)
                .ToList();

            var taggedRanges = new List<(int start, int end)>();

            foreach (var (index, length, title) in sortedMatches)
            {
                int endIndex = index + length;

                // Word boundary check on full bufferText to accurately check boundaries
                if (!IsWordBoundary(bufferText, index, length))
                    continue;

                // Skip if overlapping with an already tagged longer match
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

                if (index < 0 || endIndex > buffer.CharCount) continue;

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
            DepthInfo? currentLineDepth = null;

            TextIter iter = buffer.StartIter;
            TextIter nextIter = buffer.StartIter;
            nextIter.ForwardChar();

            while (!iter.IsEnd)
            {
                if (iter.StartsLine())
                {
                    currentLineDepth = FindDepthTag(iter);
                    if (currentLineDepth != null)
                    {
                        lineHasDepth = true;

                        if (iter.Line == prevDepthLine + 1)
                        {
                            if (currentLineDepth.Depth == prevDepth)
                            {
                                xml.WriteEndElement(); // close previous <list-item>
                            }
                            else if (currentLineDepth.Depth > prevDepth)
                            {
                                xml.WriteStartElement(null, "list", null);
                                for (int i = prevDepth + 2; i <= currentLineDepth.Depth; i++)
                                {
                                    xml.WriteStartElement(null, "list-item", null);
                                    xml.WriteAttributeString("dir", currentLineDepth.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                                    xml.WriteStartElement(null, "list", null);
                                }
                            }
                            else
                            {
                                xml.WriteEndElement(); // close previous <list-item>
                                for (int i = prevDepth; i > currentLineDepth.Depth; i--)
                                {
                                    xml.WriteEndElement(); // close <list>
                                    xml.WriteEndElement(); // close <list-item>
                                }
                            }
                        }
                        else
                        {
                            xml.WriteStartElement(null, "list", null);
                            for (int i = 1; i <= currentLineDepth.Depth; i++)
                            {
                                xml.WriteStartElement(null, "list-item", null);
                                xml.WriteAttributeString("dir", currentLineDepth.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                                xml.WriteStartElement(null, "list", null);
                            }
                        }

                        prevDepth = currentLineDepth.Depth;
                        xml.WriteStartElement(null, "list-item", null);
                        xml.WriteAttributeString("dir", currentLineDepth.Direction == Pango.Direction.Rtl ? "rtl" : "ltr");
                    }
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
                    bool isBulletChar = currentLineDepth != null && (iter.LineOffset == 0 || iter.LineOffset == 1) && IsBulletPrefix(iter);
                    if (!isBulletChar && !string.IsNullOrEmpty(iter.Char))
                    {
                        xml.WriteString(iter.Char);
                    }
                }

                bool isLineEnd = nextIter.EndsLine();
                bool endOfDepthLine = lineHasDepth && isLineEnd;
                bool nextLineHasDepth = false;

                // Evaluate next line depth only at the line end
                if (isLineEnd && iter.Line < buffer.LineCount - 1)
                {
                    TextIter nextLine = buffer.GetIterAtLine(iter.Line + 1);
                    nextLineHasDepth = FindDepthTag(nextLine) != null;
                }

                if (endOfDepthLine || (nextLineHasDepth && isLineEnd))
                {
                    while (tagStack.Count > 0)
                    {
                        TextTag t = tagStack.Pop();
                        WriteTagEnd(xml, t);
                    }
                }
                else if (tagStack.Count > 0)
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
