using System;
using System.Collections.Generic;
using System.Linq;
using Gtk;

namespace Tomboy.Latex
{
    public class LatexNoteSession : IDisposable
    {
        private readonly TextView textView;
        private readonly System.Action? freezeUndo;
        private readonly System.Action? thawUndo;
        private readonly Func<string>? getHeader;
        private readonly Func<string>? getFooter;
        private readonly Func<bool>? isDollarEnabled;

        private readonly List<LatexImage> images = new();
        private TextTag? imageTag;
        private TextTag? codeTag;

        private bool checkLatexRunning = false;
        private bool isModifyingBuffer = false;
        private uint checkTimeoutId = 0;
        private bool isDisposed = false;

        public bool IsRenderingLatex => checkLatexRunning || isModifyingBuffer;

        public LatexNoteSession(
            TextView textView,
            System.Action? freezeUndo = null,
            System.Action? thawUndo = null,
            Func<string>? getHeader = null,
            Func<string>? getFooter = null,
            Func<bool>? isDollarEnabled = null)
        {
            this.textView = textView;
            this.freezeUndo = freezeUndo;
            this.thawUndo = thawUndo;
            this.getHeader = getHeader;
            this.getFooter = getFooter;
            this.isDollarEnabled = isDollarEnabled;

            EnsureTags();
            AttachEvents();
        }

        private void EnsureTags()
        {
            var table = textView.Buffer.TagTable;

            codeTag = table.Lookup("latex_code");
            if (codeTag == null)
            {
                codeTag = new TextTag("latex_code")
                {
                    Invisible = true
                };
                table.Add(codeTag);
            }

            imageTag = table.Lookup("latex_image");
            if (imageTag == null)
            {
                imageTag = new TextTag("latex_image");
                table.Add(imageTag);
            }
        }

        private void AttachEvents()
        {
            textView.Buffer.MarkSet += OnMarkSet;
            textView.Buffer.Changed += OnBufferChanged;
            textView.Buffer.DeleteRange += OnDeleteRange;
        }

        public void Initialize()
        {
            QueueCheckLaTeX(20);
        }

        public void QueueCheckLaTeX(uint delayMs = 50)
        {
            if (isDisposed) return;
            if (checkTimeoutId != 0)
            {
                GLib.Source.Remove(checkTimeoutId);
                checkTimeoutId = 0;
            }

            checkTimeoutId = GLib.Timeout.Add(delayMs, () =>
            {
                checkTimeoutId = 0;
                if (!isDisposed)
                {
                    CheckLaTeX();
                }
                return false;
            });
        }

        private void OnMarkSet(object o, MarkSetArgs args)
        {
            if (isDisposed || checkLatexRunning) return;
            if (args.Mark == textView.Buffer.InsertMark)
            {
                QueueCheckLaTeX(40);
            }
        }

        private void OnBufferChanged(object? sender, EventArgs e)
        {
            if (isDisposed || checkLatexRunning) return;
            QueueCheckLaTeX(100);
        }

        private void OnDeleteRange(object o, DeleteRangeArgs args)
        {
            if (isDisposed || checkLatexRunning) return;
            TextIter delStart = args.Start;
            TextIter delEnd = args.End;
            int startOff = Math.Min(delStart.Offset, delEnd.Offset);
            int endOff = Math.Max(delStart.Offset, delEnd.Offset);

            for (int i = images.Count - 1; i >= 0; i--)
            {
                var img = images[i];
                if (img.ImageMark == null || img.ImageMark.Handle == IntPtr.Zero || img.ImageMark.Deleted)
                {
                    images.RemoveAt(i);
                    continue;
                }
                int imgOff = img.ImagePosition.Offset;
                if (imgOff >= startOff && imgOff < endOff)
                {
                    images.RemoveAt(i);
                }
            }
            QueueCheckLaTeX(100);
        }

        public bool HandleCopy()
        {
            if (isDisposed) return false;
            TextBuffer buf = textView.Buffer;
            if (!buf.GetSelectionBounds(out TextIter start, out TextIter end))
                return false;

            string cleanText = GetCleanTextFromRange(buf, start, end);
            if (string.IsNullOrEmpty(cleanText)) return false;

            var clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            clipboard.Text = cleanText;

            var primary = Clipboard.Get(Gdk.Selection.Primary);
            primary.Text = cleanText;

            return true;
        }

        public bool HandleCut()
        {
            if (isDisposed) return false;
            TextBuffer buf = textView.Buffer;
            if (!buf.GetSelectionBounds(out TextIter start, out TextIter end))
                return false;

            string cleanText = GetCleanTextFromRange(buf, start, end);
            var clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            clipboard.Text = cleanText;

            var primary = Clipboard.Get(Gdk.Selection.Primary);
            primary.Text = cleanText;

            int startOff = Math.Min(start.Offset, end.Offset);
            int endOff = Math.Max(start.Offset, end.Offset);
            for (int i = images.Count - 1; i >= 0; i--)
            {
                var img = images[i];
                if (img.ImageMark == null || img.ImageMark.Handle == IntPtr.Zero || img.ImageMark.Deleted)
                {
                    images.RemoveAt(i);
                    continue;
                }
                int imgOff = img.ImagePosition.Offset;
                if (imgOff >= startOff && imgOff < endOff)
                {
                    images.RemoveAt(i);
                }
            }

            isModifyingBuffer = true;
            try
            {
                buf.Delete(ref start, ref end);
            }
            finally
            {
                isModifyingBuffer = false;
            }

            QueueCheckLaTeX(50);
            return true;
        }

        public bool HandlePaste()
        {
            if (isDisposed) return false;
            TextBuffer buf = textView.Buffer;
            var clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            string? text = clipboard.WaitForText();
            if (string.IsNullOrEmpty(text)) return false;

            // Strip any \uFFFC object replacement characters
            text = text.Replace("\uFFFC", "");
            if (text.Length == 0) return true;

            isModifyingBuffer = true;
            try
            {
                if (buf.GetSelectionBounds(out TextIter start, out TextIter end))
                {
                    int startOff = Math.Min(start.Offset, end.Offset);
                    int endOff = Math.Max(start.Offset, end.Offset);
                    for (int i = images.Count - 1; i >= 0; i--)
                    {
                        var img = images[i];
                        if (img.ImageMark == null || img.ImageMark.Handle == IntPtr.Zero || img.ImageMark.Deleted)
                        {
                            images.RemoveAt(i);
                            continue;
                        }
                        int imgOff = img.ImagePosition.Offset;
                        if (imgOff >= startOff && imgOff < endOff)
                        {
                            images.RemoveAt(i);
                        }
                    }

                    buf.Delete(ref start, ref end);
                }
                TextIter cursor = buf.GetIterAtMark(buf.InsertMark);
                buf.Insert(ref cursor, text);
            }
            finally
            {
                isModifyingBuffer = false;
            }

            QueueCheckLaTeX(50);
            return true;
        }

        private static string GetCleanTextFromRange(TextBuffer buf, TextIter start, TextIter end)
        {
            // Include hidden characters so formula code is copied, and remove \uFFFC pixbuf markers
            string raw = buf.GetText(start, end, true);
            return raw.Replace("\uFFFC", "");
        }

        public bool HandleClick(TextIter iter)
        {
            if (isDisposed) return false;

            TextBuffer buf = textView.Buffer;
            LatexImage? clickedImg = null;

            foreach (var img in images)
            {
                if (img.IsImagePosition(iter))
                {
                    clickedImg = img;
                    break;
                }
            }

            if (clickedImg != null)
            {
                isModifyingBuffer = true;
                try
                {
                    int offset = clickedImg.Open(freezeUndo, thawUndo);
                    images.Remove(clickedImg);

                    if (offset >= 0 && offset <= buf.CharCount)
                    {
                        TextIter pos = buf.GetIterAtOffset(offset);
                        buf.PlaceCursor(pos);
                    }
                    textView.GrabFocus();
                    return true;
                }
                finally
                {
                    isModifyingBuffer = false;
                }
            }

            return false;
        }

        public void CheckLaTeX()
        {
            if (checkLatexRunning || isDisposed) return;
            if (textView == null || textView.Handle == IntPtr.Zero) return;
            TextBuffer buf = textView.Buffer;
            if (buf == null || buf.Handle == IntPtr.Zero) return;

            checkLatexRunning = true;
            try
            {
                int currentOffset = 0;
                TextIter startPos = buf.StartIter;
                // If the buffer has multiple lines, skip the first line (note title)
                if (startPos.ForwardLine())
                {
                    currentOffset = startPos.Offset;
                }

                string? rawHeader = getHeader?.Invoke();
                string header = string.IsNullOrWhiteSpace(rawHeader) ? LatexManager.DEFAULT_HEADER : rawHeader;
                string? rawFooter = getFooter?.Invoke();
                string footer = string.IsNullOrWhiteSpace(rawFooter) ? LatexManager.DEFAULT_FOOTER : rawFooter;
                bool dollar = isDollarEnabled?.Invoke() ?? false;

                while (currentOffset < buf.CharCount)
                {
                    TextIter searchPos = buf.GetIterAtOffset(currentOffset);
                    if (searchPos.IsEnd) break;

                    if (!FindMathCode(searchPos, dollar, out TextIter matchStart, out TextIter matchEnd))
                        break;

                    int startOff = matchStart.Offset;
                    int endOff = matchEnd.Offset;

                    // Clean up any orphaned / duplicate pixbufs immediately preceding startOff
                    while (startOff > 0)
                    {
                        TextIter prev = buf.GetIterAtOffset(startOff - 1);
                        if (prev.Pixbuf != null || prev.Char == "\uFFFC")
                        {
                            int pOff = prev.Offset;
                            bool isTracked = images.Any(img =>
                                img.ImageMark != null &&
                                !img.ImageMark.Deleted &&
                                img.ImageMark.Handle != IntPtr.Zero &&
                                img.ImagePosition.Offset == pOff);

                            if (!isTracked)
                            {
                                TextIter delStart = buf.GetIterAtOffset(startOff - 1);
                                TextIter delEnd = buf.GetIterAtOffset(startOff);
                                buf.Delete(ref delStart, ref delEnd);
                                startOff--;
                                endOff--;
                                continue;
                            }
                        }
                        break;
                    }

                    LatexImage? existingImage = null;
                    if (startOff > 0)
                    {
                        int targetOffset = startOff - 1;
                        existingImage = images.FirstOrDefault(img =>
                            img.ImageMark != null &&
                            !img.ImageMark.Deleted &&
                            img.ImageMark.Handle != IntPtr.Zero &&
                            img.ImagePosition.Offset == targetOffset);
                    }

                    // Fetch fresh cursor iterator from buffer
                    TextIter cursor = buf.GetIterAtMark(buf.InsertMark);
                    int cursorOff = cursor.Offset;

                    // Check if cursor is strictly inside formula code (from '[' to before ']')
                    // When cursor is at endOff (right after formula), cursorInCode is false (formula stays rendered)
                    bool cursorInCode = cursorOff >= startOff && cursorOff < endOff;

                    if (existingImage != null)
                    {
                        if (cursorInCode)
                        {
                            existingImage.Open(freezeUndo, thawUndo);
                            images.Remove(existingImage);
                            startOff--;
                            endOff--;
                        }
                    }
                    else
                    {
                        if (!cursorInCode)
                        {
                            TextIter codeStart = buf.GetIterAtOffset(startOff);
                            TextIter codeEnd = buf.GetIterAtOffset(endOff);
                            string code = buf.GetText(codeStart, codeEnd, true);
                            Gdk.Pixbuf? pixbuf = LatexManager.Instance.GetImage(code, header, footer, (c, pb) =>
                            {
                                if (!isDisposed)
                                {
                                    QueueCheckLaTeX(10);
                                }
                            });

                            if (pixbuf != null && imageTag != null && codeTag != null)
                            {
                                TextIter cStart = buf.GetIterAtOffset(startOff);
                                TextIter cEnd = buf.GetIterAtOffset(endOff);
                                var img = new LatexImage(cStart, cEnd, pixbuf, imageTag, codeTag, freezeUndo, thawUndo);
                                images.Add(img);
                                endOff++;
                            }
                        }
                    }

                    currentOffset = Math.Max(endOff, currentOffset + 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LatexNoteSession] CheckLaTeX error: {ex.Message}");
            }
            finally
            {
                checkLatexRunning = false;
            }
        }

        private bool FindMathCode(TextIter pos, bool dollarEnabled, out TextIter matchStart, out TextIter matchEnd)
        {
            TextBuffer buf = textView.Buffer;
            matchStart = buf.EndIter;
            matchEnd = buf.EndIter;
            bool found = false;

            // 1. Search for display math: \[ ... \]
            if (pos.ForwardSearch("\\[", 0, out TextIter dStart, out TextIter dAfterStart, buf.EndIter) &&
                dAfterStart.ForwardSearch("\\]", 0, out TextIter dBeforeEnd, out TextIter dEnd, buf.EndIter))
            {
                matchStart = dStart;
                matchEnd = dEnd;
                found = true;
            }

            // 2. Search for inline math: \( ... \)
            if (pos.ForwardSearch("\\(", 0, out TextIter iStart, out TextIter iAfterStart, buf.EndIter) &&
                iAfterStart.ForwardSearch("\\)", 0, out TextIter iBeforeEnd, out TextIter iEnd, buf.EndIter))
            {
                if (!found || iStart.Offset < matchStart.Offset)
                {
                    matchStart = iStart;
                    matchEnd = iEnd;
                    found = true;
                }
            }

            // 3. Search for dollar math: $ ... $ (if enabled)
            if (dollarEnabled &&
                pos.ForwardSearch("$", 0, out TextIter dlStart, out TextIter dlAfterStart, buf.EndIter) &&
                dlAfterStart.ForwardSearch("$", 0, out TextIter dlBeforeEnd, out TextIter dlEnd, buf.EndIter))
            {
                if (!found || dlStart.Offset < matchStart.Offset)
                {
                    matchStart = dlStart;
                    matchEnd = dlEnd;
                    found = true;
                }
            }

            return found;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            if (checkTimeoutId != 0)
            {
                GLib.Source.Remove(checkTimeoutId);
                checkTimeoutId = 0;
            }

            try
            {
                if (textView != null && textView.Handle != IntPtr.Zero && textView.Buffer != null && textView.Buffer.Handle != IntPtr.Zero)
                {
                    textView.Buffer.MarkSet -= OnMarkSet;
                    textView.Buffer.Changed -= OnBufferChanged;
                    textView.Buffer.DeleteRange -= OnDeleteRange;
                }
            }
            catch { }
            images.Clear();
        }
    }
}
