using System.Collections.Generic;
using UnityEngine;

namespace GunsawLevelEditorTools;

internal sealed class EditorHistory
{
    private readonly Stack<Entry> _undo = new Stack<Entry>();
    private readonly Stack<Entry> _redo = new Stack<Entry>();
    private string _last;
    private string _mouseStart;
    private readonly CollaborativeEditorHistory _collaborative = new CollaborativeEditorHistory();
    private bool _wasCollaborative;

    internal void Update(LevelEditor editor, bool editingText)
    {
        var collaborative = _collaborative.Update(editor, editingText);
        if (collaborative != _wasCollaborative)
        {
            ResetSnapshots();
            _wasCollaborative = collaborative;
        }
        if (collaborative)
            return;
        if (_last == null)
        {
            _last = Snapshot(editor);
            return;
        }

        var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!editingText && control && Input.GetKeyDown(KeyCode.Z))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                Redo(editor);
            else
                Undo(editor);
            return;
        }

        if (Input.GetMouseButtonDown(0))
            _mouseStart = _last;
        if (Input.GetMouseButtonUp(0))
        {
            var current = Snapshot(editor);
            if (_mouseStart != null && current != _mouseStart)
                Add(_mouseStart, current);
            _mouseStart = null;
            _last = current;
            return;
        }

        if (HasKeyboardEdit(control))
        {
            var current = Snapshot(editor);
            if (current != _last)
                Add(_last, current);
            _last = current;
        }
    }

    private static bool HasKeyboardEdit(bool control)
    {
        return Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.LeftArrow) ||
               Input.GetKeyDown(KeyCode.RightArrow) ||
               Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
               (control && Input.GetKeyDown(KeyCode.D));
    }

    private static string Snapshot(LevelEditor editor)
    {
        return editor.GetLevelCode();
    }

    private void Add(string before, string after)
    {
        if (before == after)
            return;
        _undo.Push(new Entry(before, after));
        _redo.Clear();
    }

    private void Undo(LevelEditor editor)
    {
        if (_undo.Count == 0)
            return;
        var entry = _undo.Pop();
        Restore(editor, entry.Before);
        _redo.Push(entry);
    }

    private void Redo(LevelEditor editor)
    {
        if (_redo.Count == 0)
            return;
        var entry = _redo.Pop();
        Restore(editor, entry.After);
        _undo.Push(entry);
    }

    private void Restore(LevelEditor editor, string code)
    {
        editor.loadString = code;
        editor.LoadLevel();
        _last = code;
        _mouseStart = null;
    }

    internal void Reset()
    {
        ResetSnapshots();
        _collaborative.Reset();
        _wasCollaborative = false;
    }

    internal void Dispose()
    {
        Reset();
        _collaborative.Dispose();
    }

    private void ResetSnapshots()
    {
        _undo.Clear();
        _redo.Clear();
        _last = null;
        _mouseStart = null;
    }

    private struct Entry
    {
        internal readonly string Before;
        internal readonly string After;

        internal Entry(string before, string after)
        {
            Before = before;
            After = after;
        }
    }
}
