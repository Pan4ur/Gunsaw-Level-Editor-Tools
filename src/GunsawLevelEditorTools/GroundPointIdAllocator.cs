using System.Collections.Generic;
using UnityEngine;

namespace GunsawLevelEditorTools;

internal sealed class GroundPointIdAllocator
{
    private const int FirstId = 6000001;
    private const int LastId = 6000999;
    private readonly HashSet<int> _seen = new HashSet<int>();
    private readonly HashSet<int> _used = new HashSet<int>();
    private Level _level;
    private bool _enabled = true;
    private GameObject _placementSource;
    private Vector2 _placementStart;

    internal void Toggle(LevelEditor editor)
    {
        _enabled = !_enabled;
    }

    internal void Update(LevelEditor editor, bool duplicated)
    {
        if (!ReferenceEquals(_level, editor.level))
            Reset(editor);

        var selected = editor.currentlySelected;
        var created = false;
        var parts = editor.GetComponentsInChildren<LevelPartGame>(true);
        foreach (var candidate in parts)
            if (IsGroundPoint(candidate) && _seen.Add(candidate.gameObject.GetInstanceID()) &&
                candidate.gameObject == selected)
                created = true;

        if (!selected || !created)
            return;
        var part = selected.GetComponent<LevelPartGame>();
        if (!_enabled || duplicated)
            return;

        _used.Clear();
        foreach (var candidate in parts)
            if (candidate != null && IsGroundPoint(candidate))
                _used.Add(candidate.part.id);

        for (var id = FirstId; id <= LastId; id++)
        {
            if (_used.Contains(id))
                continue;
            part.part.id = id;
            editor.idField.SetTextWithoutNotify(id.ToString());
            return;
        }

        editor.SetInfoText("Ground point ID pool is full.");
    }

    internal bool TryPlaceFromSelected(LevelEditor editor)
    {
        if (_placementSource)
        {
            if (!Input.GetMouseButtonUp(1))
                return false;
            var sourceObject = _placementSource;
            _placementSource = null;
            var delta = (Vector2)Input.mousePosition - _placementStart;
            if (delta.sqrMagnitude > 64f)
                return false;
            var source = sourceObject.GetComponent<LevelPartGame>();
            if (!IsGroundPoint(source) || Camera.main == null)
                return false;
            var copy = Object.Instantiate(sourceObject, editor.transform);
            var position = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            position.z = sourceObject.transform.position.z;
            copy.transform.position = position;
            var part = copy.GetComponent<LevelPartGame>();
            part.part.id = source.part.id;
            var groundPoint = copy.GetComponent<GroundPoint>();
            if (groundPoint != null)
                groundPoint.id = source.part.id;
            _seen.Add(copy.GetInstanceID());
            return true;
        }

        if (!Input.GetMouseButtonDown(1) ||
            !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ||
            (UnityEngine.EventSystems.EventSystem.current != null &&
             UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            return false;
        var selected = editor.currentlySelected;
        var sourcePart = selected ? selected.GetComponent<LevelPartGame>() : null;
        if (!IsGroundPoint(sourcePart))
            return false;
        _placementSource = selected;
        _placementStart = Input.mousePosition;
        return false;
    }

    internal void Reset(LevelEditor editor)
    {
        _seen.Clear();
        _used.Clear();
        _placementSource = null;
        _level = editor != null ? editor.level : null;
        if (editor == null)
            return;
        foreach (var part in editor.GetComponentsInChildren<LevelPartGame>(true))
            if (IsGroundPoint(part))
                _seen.Add(part.gameObject.GetInstanceID());
    }

    private static bool IsGroundPoint(LevelPartGame part)
    {
        if (part == null || part.part == null)
            return false;
        return part.part.path == "Building/GroundPoint" || part.part.path == "Editor/Building/GroundPoint" ||
               part.fullName == "Ground Point";
    }
}