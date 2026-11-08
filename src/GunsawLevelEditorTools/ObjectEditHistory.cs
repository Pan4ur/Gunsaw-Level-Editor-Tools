using System;
using System.Collections.Generic;

namespace GunsawLevelEditorTools;

internal sealed class ObjectEditHistory
{
    private readonly Stack<Entry> _undo = new Stack<Entry>();
    private readonly Stack<Entry> _redo = new Stack<Entry>();

    internal void Record(Dictionary<Guid, string> before, Dictionary<Guid, string> after)
    {
        var entry = new Entry();
        foreach (var pair in after)
        {
            before.TryGetValue(pair.Key, out var previous);
            if (previous == pair.Value)
                continue;
            entry.Before.Add(pair.Key, previous);
            entry.After.Add(pair.Key, pair.Value);
        }
        if (entry.After.Count == 0)
            return;
        _undo.Push(entry);
        _redo.Clear();
    }

    internal void Invalidate(Guid id)
    {
        foreach (var entries in new[] { _undo, _redo })
            foreach (var entry in entries)
            {
                entry.Before.Remove(id);
                entry.After.Remove(id);
            }
    }

    internal Dictionary<Guid, string> Take(bool redo, Func<Guid, string> current)
    {
        var source = redo ? _redo : _undo;
        var destination = redo ? _undo : _redo;
        while (source.Count > 0)
        {
            var entry = source.Pop();
            var expected = redo ? entry.Before : entry.After;
            var target = redo ? entry.After : entry.Before;
            var applied = new Entry();
            var result = new Dictionary<Guid, string>();
            foreach (var pair in expected)
            {
                if (current(pair.Key) != pair.Value)
                    continue;
                result.Add(pair.Key, target[pair.Key]);
                applied.Before.Add(pair.Key, entry.Before[pair.Key]);
                applied.After.Add(pair.Key, entry.After[pair.Key]);
            }
            if (result.Count == 0)
                continue;
            destination.Push(applied);
            return result;
        }
        return new Dictionary<Guid, string>();
    }

    internal void Reset()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private sealed class Entry
    {
        internal readonly Dictionary<Guid, string> Before = new Dictionary<Guid, string>();
        internal readonly Dictionary<Guid, string> After = new Dictionary<Guid, string>();
    }
}
