using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GunsawLevelEditorTools;

internal sealed class CollaborativeEditorHistory
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly ObjectEditHistory _history = new ();
    private readonly Harmony _harmony = new ("gunsaw.leveleditortools.history");
    private static CollaborativeEditorHistory _active;
    private Type _replication;
    private Type _session;
    private Type _change;
    private bool _searched;
    private object _document;
    private Dictionary<Guid, string> _replay = new Dictionary<Guid, string>();

    internal bool Update(LevelEditor editor, bool editingText)
    {
        if (_replication == null)
        {
            if (_searched)
                return false;
            _searched = true;
            _replication = AccessTools.TypeByName("LevelEditorReplication");
            if (_replication == null)
                return false;
            _session = _replication.Assembly.GetType("MultiplayerSession");
            _change = _replication.Assembly.GetType("EditorObjectChange");
            var document = _replication.Assembly.GetType("EditorDocument");
            _active = this;
            _harmony.Patch(AccessTools.Method(document, "Apply"),
                prefix: new HarmonyMethod(typeof(CollaborativeEditorHistory), nameof(BeforeCommit)));
            _harmony.Patch(AccessTools.Method(document, "Restore"),
                prefix: new HarmonyMethod(typeof(CollaborativeEditorHistory), nameof(BeforeSnapshot)));
        }

        var replication = _replication.GetField("instance", StaticFlags).GetValue(null);
        if (!(bool)_session.GetProperty("IsActive", StaticFlags).GetValue(null, null) ||
            (bool)_replication.GetProperty("IsLocalEditor", StaticFlags).GetValue(null, null) ||
            replication == null || !ReferenceEquals(Read(replication, "editor"), editor))
        {
            Reset();
            return false;
        }

        var documentValue = Read(replication, "document");
        if (!ReferenceEquals(_document, documentValue))
        {
            Reset();
            _document = documentValue;
        }
        
        if (editingText || !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ||
            !Input.GetKeyDown(KeyCode.Z))
            return true;
        if (!(bool)_replication.GetProperty("CanEdit", StaticFlags).GetValue(null, null))
            return true;

        var draft = _replication.GetMethod("CaptureChanges", InstanceFlags).Invoke(replication, new object[] { 0 });
        if (Read(replication, "pending") != null || Read(replication, "deferredDraft") != null || ((Array)Read(draft, "Changes")).Length != 0 || Read(draft, "Environment") != null)
        {
            editor.SetInfoText("Undo: waiting for changes to sync. Press Ctrl+Z again.");
            return true;
        }

        var redo = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        var changes = _history.Take(redo, id => Current(_document, id));
        if (changes.Count == 0)
        {
            editor.SetInfoText("No changes to " + (redo ? "redo." : "undo."));
            return true;
        }

        var values = new List<object>();
        Level environment = null;
        foreach (var pair in changes)
        {
            if (pair.Key == Guid.Empty)
            {
                environment = JsonUtility.FromJson<Level>(pair.Value);
                continue;
            }
            var state = pair.Value == null ? null : JsonUtility.FromJson<PartState>(pair.Value);
            values.Add(Activator.CreateInstance(_change, InstanceFlags, null, [pair.Key, 0, state == null ? null : state.Part, (Vector3?)(state == null ? Vector3.one : state.Scale) ], null));
        }
        var array = Array.CreateInstance(_change, values.Count);
        for (var i = 0; i < values.Count; i++)
            array.SetValue(values[i], i);
        _replay = changes;
        _replication.GetMethod("ApplyWorld", InstanceFlags).Invoke(replication, [array, environment, false]);
        editor.SetInfoText(redo ? "Changes redone." : "Changes undone.");
        return true;
    }

    private static void BeforeCommit(object __instance, object[] __args)
    {
        if (_active != null && ReferenceEquals(_active._document, __instance))
            _active.Record(__args[0]);
    }

    private static void BeforeSnapshot(object __instance)
    {
        if (_active != null && ReferenceEquals(_active._document, __instance))
            _active.Reset();
    }

    private void Record(object commit)
    {
        var own = (ushort)Read(commit, "Author") == (ushort)_session.GetProperty("LocalPeerId", StaticFlags).GetValue(null, null);
        var before = new Dictionary<Guid, string>();
        var after = new Dictionary<Guid, string>();
        foreach (var change in (IEnumerable)Read(commit, "Changes"))
            after[(Guid)Read(change, "Id")] = Serialize(change);
        var environment = Read(commit, "Environment");
        if (environment != null)
            after[Guid.Empty] = JsonUtility.ToJson(environment);
        foreach (var pair in after)
        {
            if (!own)
            {
                _history.Invalidate(pair.Key);
                _replay.Remove(pair.Key);
                continue;
            }
            before[pair.Key] = _replay.TryGetValue(pair.Key, out var replay) ? replay : Current(_document, pair.Key);
            _replay.Remove(pair.Key);
        }
        if (own)
            _history.Record(before, after);
    }

    private static string Current(object document, Guid id)
    {
        if (id == Guid.Empty)
            return JsonUtility.ToJson(Read(document, "Environment"));
        var objects = (IDictionary)Read(document, "Objects");
        return objects.Contains(id) ? Serialize(objects[id]) : null;
    }

    private static string Serialize(object change)
    {
        var part = Read(change, "Part") as LevelPart;
        return part == null ? null : JsonUtility.ToJson(new PartState { Part = part, Scale = (Vector3)Read(change, "Scale") });
    }

    private static object Read(object source, string name) =>
        source.GetType().GetField(name, InstanceFlags).GetValue(source);

    internal void Reset()
    {
        _history.Reset();
        _replay.Clear();
        _document = null;
    }

    internal void Dispose()
    {
        _harmony.UnpatchSelf();
        if (_active == this)
            _active = null;
        Reset();
    }

    [Serializable]
    private sealed class PartState
    {
        public LevelPart Part;
        public Vector3 Scale;
    }
}
