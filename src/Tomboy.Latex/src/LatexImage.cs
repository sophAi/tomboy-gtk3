using System;
using Gtk;

namespace Tomboy.Latex
{
    public class LatexImage
    {
        public string LatexCode { get; }
        public TextMark ImageMark { get; }
        public TextTag ImageTag { get; }
        public TextTag CodeTag { get; }
        public TextBuffer Buffer { get; }

        public TextIter ImagePosition => Buffer.GetIterAtMark(ImageMark);

        public LatexImage(TextIter codeStart, TextIter codeEnd, Gdk.Pixbuf pixbuf, TextTag imageTag, TextTag codeTag, System.Action? freezeUndo = null, System.Action? thawUndo = null)
        {
            LatexCode = codeStart.GetText(codeEnd);
            Buffer = codeStart.Buffer;
            ImageTag = imageTag;
            CodeTag = codeTag;

            freezeUndo?.Invoke();
            try
            {
                int insertOffset = codeStart.Offset;
                TextIter imageStart = Buffer.GetIterAtOffset(insertOffset);
                Buffer.InsertPixbuf(ref imageStart, pixbuf);

                TextIter markPos = Buffer.GetIterAtOffset(insertOffset);
                ImageMark = Buffer.CreateMark(null, markPos, false);

                TextIter imgStart = Buffer.GetIterAtOffset(insertOffset);
                TextIter imgEnd = Buffer.GetIterAtOffset(insertOffset + 1);
                Buffer.ApplyTag(ImageTag, imgStart, imgEnd);

                TextIter cStart = Buffer.GetIterAtOffset(insertOffset + 1);
                TextIter cEnd = Buffer.GetIterAtOffset(insertOffset + 1 + LatexCode.Length);
                Buffer.ApplyTag(CodeTag, cStart, cEnd);
            }
            finally
            {
                thawUndo?.Invoke();
            }
        }

        public bool IsImagePosition(TextIter pos)
        {
            if (ImageMark == null || ImageMark.Handle == IntPtr.Zero || ImageMark.Deleted) return false;
            TextIter imgIter = Buffer.GetIterAtMark(ImageMark);
            return imgIter.Offset == pos.Offset || pos.HasTag(ImageTag);
        }

        public int Open(System.Action? freezeUndo = null, System.Action? thawUndo = null)
        {
            if (ImageMark == null || ImageMark.Handle == IntPtr.Zero || ImageMark.Deleted) return -1;

            freezeUndo?.Invoke();
            try
            {
                TextIter start = Buffer.GetIterAtMark(ImageMark);
                int offset = start.Offset;
                TextIter end = Buffer.GetIterAtOffset(offset + 1);
                Buffer.RemoveTag(ImageTag, start, end);
                Buffer.Delete(ref start, ref end);

                TextIter cStart = Buffer.GetIterAtOffset(offset);
                TextIter cEnd = Buffer.GetIterAtOffset(offset + LatexCode.Length);
                Buffer.RemoveTag(CodeTag, cStart, cEnd);

                if (ImageMark != null && ImageMark.Handle != IntPtr.Zero && !ImageMark.Deleted)
                {
                    Buffer.DeleteMark(ImageMark);
                }
                return offset;
            }
            finally
            {
                thawUndo?.Invoke();
            }
        }
    }
}
