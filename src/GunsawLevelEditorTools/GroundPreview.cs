using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace GunsawLevelEditorTools;

internal sealed class GroundPreview
{
    private readonly Dictionary<int, SpriteShapeController> _shapes = new Dictionary<int, SpriteShapeController>();
    private readonly Dictionary<int, int> _hashes = new Dictionary<int, int>();
    private GameObject _root;
    private LevelLoader _sorter;
    private bool _visible;

    internal void Toggle(LevelEditor editor)
    {
        _visible = !_visible;
        if (_visible)
            Refresh(editor);
        else
            Clear();
        editor.SetInfoText(_visible ? "Ground preview: ON" : "Ground preview: OFF");
    }

    internal void Update(LevelEditor editor)
    {
        if (_visible && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(1) ||
                         Input.GetMouseButtonUp(1)))
            Refresh(editor);
    }

    internal void RefreshNow(LevelEditor editor)
    {
        if (_visible)
            Refresh(editor);
    }

    internal void Dispose()
    {
        _visible = false;
        Clear();
    }

    private void Refresh(LevelEditor editor)
    {
        if (!_root)
        {
            _root = new GameObject("Gunsaw Ground Preview");
            _sorter = _root.AddComponent<LevelLoader>();
            _sorter.enabled = false;
        }

        var groups = new Dictionary<int, List<Point>>();
        foreach (var part in editor.GetComponentsInChildren<LevelPartGame>(true))
        {
            if (part == null || part.part == null)
                continue;
            var path = part.part.path;
            if (path != "Building/GroundPoint" && path != "Editor/Building/GroundPoint" &&
                part.fullName != "Ground Point")
                continue;
            if (!groups.TryGetValue(part.part.id, out var list))
                groups[part.part.id] = list = new List<Point>();
            list.Add(new Point(part.transform.position));
        }

        var valid = new HashSet<int>();
        foreach (var pair in groups)
        {
            if (pair.Value.Count < 2)
                continue;
            valid.Add(pair.Key);
            var hash = Hash(pair.Value);
            if (_shapes.TryGetValue(pair.Key, out var existing) && existing &&
                _hashes.TryGetValue(pair.Key, out var saved) && saved == hash)
                continue;
            if (existing)
                Object.Destroy(existing.gameObject);
            var ordered = Order(pair.Value);
            var distinct = new List<Vector2>();
            foreach (var point in ordered)
            {
                var duplicate = false;
                foreach (var existingPoint in distinct)
                    if ((existingPoint - point).sqrMagnitude < .0001f)
                    {
                        duplicate = true;
                        break;
                    }

                if (!duplicate)
                    distinct.Add(point);
            }

            if (distinct.Count < 2)
                continue;
            var prefab = Resources.Load<GameObject>("Building/GroundShape");
            if (!prefab)
                continue;
            var shape = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity)
                .GetComponent<SpriteShapeController>();
            if (!shape)
                continue;
            foreach (var collider in shape.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
            shape.spline.Clear();
            for (var i = 0; i < distinct.Count; i++)
            {
                shape.spline.InsertPointAt(i, distinct[i]);
                shape.spline.SetTangentMode(i, ShapeTangentMode.Linear);
            }

            shape.RefreshSpriteShape();
            shape.BakeMesh().Complete();
            using (var commands = new CommandBuffer())
            {
                var target = Shader.PropertyToID("_GunsawGroundPreview");
                commands.GetTemporaryRT(target, 1, 1, 0);
                commands.SetRenderTarget(target);
                var renderer = shape.GetComponent<Renderer>();
                commands.DrawRenderer(renderer, renderer.sharedMaterial);
                commands.ReleaseTemporaryRT(target);
                Graphics.ExecuteCommandBuffer(commands);
            }
            _shapes[pair.Key] = shape;
            _hashes[pair.Key] = hash;
        }

        var stale = new List<int>();
        foreach (var pair in _shapes)
            if (!valid.Contains(pair.Key))
            {
                if (pair.Value)
                    Object.Destroy(pair.Value.gameObject);
                stale.Add(pair.Key);
            }

        foreach (var id in stale)
        {
            _shapes.Remove(id);
            _hashes.Remove(id);
        }
    }

    private List<Vector2> Order(List<Point> points)
    {
        var temporary = new List<GroundPoint>();
        foreach (var point in points)
        {
            var item = new GameObject("Gunsaw Ground Sort Point");
            item.transform.SetParent(_root.transform, false);
            item.transform.position = point.Position;
            temporary.Add(item.AddComponent<GroundPoint>());
        }

        var sourcePoints = new List<GroundPoint>();
        foreach (var point in Object.FindObjectsOfType<GroundPoint>())
            if (point && point.transform.IsChildOf(_root.transform))
                sourcePoints.Add(point);
        var sourceOrder = _sorter.GetPointcurve(sourcePoints[0], sourcePoints);
        var result = new List<Vector2>();
        foreach (var point in sourceOrder)
            result.Add(point.transform.position);
        foreach (var point in temporary)
            if (point)
                Object.Destroy(point.gameObject);
        return result;
    }

    private static int Hash(List<Point> points)
    {
        unchecked
        {
            var hash = 17;
            foreach (var point in points)
            {
                var position = point.Position;
                hash = hash * 31 + Mathf.RoundToInt(position.x * 1000f);
                hash = hash * 31 + Mathf.RoundToInt(position.y * 1000f);
            }

            return hash;
        }
    }

    private void Clear()
    {
        foreach (var shape in _shapes.Values)
            if (shape)
                Object.Destroy(shape.gameObject);
        if (_root)
            Object.Destroy(_root);
        _root = null;
        _sorter = null;
        _shapes.Clear();
        _hashes.Clear();
    }

    private readonly struct Point
    {
        internal readonly Vector2 Position;

        internal Point(Vector2 position)
        {
            Position = position;
        }
    }
}