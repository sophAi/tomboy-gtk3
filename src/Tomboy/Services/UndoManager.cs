using System;
using System.Collections.Generic;
using Gtk;

namespace Tomboy.Services
{
    public interface IUndoAction
    {
        void Undo(TextBuffer buffer);
        void Redo(TextBuffer buffer);
        bool CanMerge(IUndoAction action);
        void Merge(IUndoAction action);
        void Destroy();
    }

    public class TextInsertAction : IUndoAction
    {
        public int Offset { get; set; }
        public string Text { get; set; }
        public bool IsPaste { get; set; }

        public TextInsertAction(int offset, string text)
        {
            Offset = offset;
            Text = text ?? string.Empty;
            IsPaste = Text.Length > 1;
        }

        public void Undo(TextBuffer buffer)
        {
            var startIter = buffer.GetIterAtOffset(Offset);
            var endIter = buffer.GetIterAtOffset(Offset + Text.Length);
            buffer.Delete(ref startIter, ref endIter);
            buffer.PlaceCursor(buffer.GetIterAtOffset(Offset));
        }

        public void Redo(TextBuffer buffer)
        {
            var iter = buffer.GetIterAtOffset(Offset);
            buffer.Insert(ref iter, Text);
            buffer.PlaceCursor(buffer.GetIterAtOffset(Offset + Text.Length));
        }

        public bool CanMerge(IUndoAction action)
        {
            if (action is TextInsertAction ins)
            {
                if (IsPaste || ins.IsPaste) return false;
                if (ins.Offset != Offset + Text.Length) return false;
                if (Text.Length > 0 && Text[Text.Length - 1] == '\n') return false;
                if (ins.Text.Length > 0 && (ins.Text[0] == ' ' || ins.Text[0] == '\t' || ins.Text[0] == '\n')) return false;
                return true;
            }
            return false;
        }

        public void Merge(IUndoAction action)
        {
            if (action is TextInsertAction ins)
            {
                Text += ins.Text;
            }
        }

        public void Destroy() { }
    }

    public class TextDeleteAction : IUndoAction
    {
        public int Offset { get; set; }
        public string DeletedText { get; set; }
        public bool IsCut { get; set; }

        public TextDeleteAction(int offset, string text)
        {
            Offset = offset;
            DeletedText = text ?? string.Empty;
            IsCut = DeletedText.Length > 1;
        }

        public void Undo(TextBuffer buffer)
        {
            var iter = buffer.GetIterAtOffset(Offset);
            buffer.Insert(ref iter, DeletedText);
            buffer.PlaceCursor(buffer.GetIterAtOffset(Offset + DeletedText.Length));
        }

        public void Redo(TextBuffer buffer)
        {
            var startIter = buffer.GetIterAtOffset(Offset);
            var endIter = buffer.GetIterAtOffset(Offset + DeletedText.Length);
            buffer.Delete(ref startIter, ref endIter);
            buffer.PlaceCursor(buffer.GetIterAtOffset(Offset));
        }

        public bool CanMerge(IUndoAction action)
        {
            if (action is TextDeleteAction del)
            {
                if (IsCut || del.IsCut) return false;
                if (DeletedText.Contains('\n') || del.DeletedText.Contains('\n')) return false;

                // Backspace merge: deletion happens right before
                if (del.Offset + del.DeletedText.Length == Offset) return true;

                // Forward Delete merge: deletion happens at same offset
                if (del.Offset == Offset) return true;
            }
            return false;
        }

        public void Merge(IUndoAction action)
        {
            if (action is TextDeleteAction del)
            {
                if (del.Offset + del.DeletedText.Length == Offset)
                {
                    DeletedText = del.DeletedText + DeletedText;
                    Offset = del.Offset;
                }
                else if (del.Offset == Offset)
                {
                    DeletedText += del.DeletedText;
                }
            }
        }

        public void Destroy() { }
    }

    public class UndoManager
    {
        private readonly TextBuffer buffer;
        private readonly Stack<IUndoAction> undoStack = new();
        private readonly Stack<IUndoAction> redoStack = new();
        private int freezeCount = 0;
        private bool tryMerge = false;

        public event EventHandler? UndoChanged;

        public bool CanUndo => undoStack.Count > 0;
        public bool CanRedo => redoStack.Count > 0;

        public UndoManager(TextBuffer buffer)
        {
            this.buffer = buffer;
            buffer.InsertText += OnInsertText;
            buffer.DeleteRange += OnDeleteRange;
        }

        public void FreezeUndo()
        {
            freezeCount++;
        }

        public void ThawUndo()
        {
            if (freezeCount > 0)
                freezeCount--;
        }

        public void ClearUndoHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
            tryMerge = false;
            UndoChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddUndoAction(IUndoAction action)
        {
            if (freezeCount > 0) return;

            if (tryMerge && undoStack.Count > 0)
            {
                var top = undoStack.Peek();
                if (top.CanMerge(action))
                {
                    top.Merge(action);
                    return;
                }
            }

            undoStack.Push(action);
            redoStack.Clear();
            tryMerge = true;
            UndoChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;

            var action = undoStack.Pop();
            FreezeUndo();
            try
            {
                action.Undo(buffer);
            }
            finally
            {
                ThawUndo();
            }
            redoStack.Push(action);
            tryMerge = false;
            UndoChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;

            var action = redoStack.Pop();
            FreezeUndo();
            try
            {
                action.Redo(buffer);
            }
            finally
            {
                ThawUndo();
            }
            undoStack.Push(action);
            tryMerge = false;
            UndoChanged?.Invoke(this, EventArgs.Empty);
        }

        [GLib.ConnectBefore]
        private void OnInsertText(object sender, InsertTextArgs args)
        {
            if (freezeCount > 0) return;
            string cleanText = (args.NewText ?? string.Empty).Replace("\uFFFC", "");
            if (cleanText.Length == 0) return;
            var action = new TextInsertAction(args.Pos.Offset, cleanText);
            AddUndoAction(action);
        }

        [GLib.ConnectBefore]
        private void OnDeleteRange(object sender, DeleteRangeArgs args)
        {
            if (freezeCount > 0) return;
            string deleted = buffer.GetText(args.Start, args.End, true).Replace("\uFFFC", "");
            if (!string.IsNullOrEmpty(deleted))
            {
                var action = new TextDeleteAction(args.Start.Offset, deleted);
                AddUndoAction(action);
            }
        }
    }
}
