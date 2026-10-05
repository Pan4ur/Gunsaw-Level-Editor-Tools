using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunsawLevelEditorTools;

internal sealed class DoorMotionOverlay
{
    private const float HitRadiusPixels = 48f;

    private static readonly Color[] TargetColors =
    {
        new Color(.9f, .3f, 1f, .9f), new Color(.2f, .85f, 1f, .9f), new Color(1f, .65f, .2f, .9f),
        new Color(.35f, 1f, .5f, .9f), new Color(1f, .35f, .45f, .9f), new Color(.7f, .55f, 1f, .9f)
    };

    private readonly Dictionary<GameObject, SpriteRenderer> _ghosts = new Dictionary<GameObject, SpriteRenderer>();

    private readonly Dictionary<GameObject, SpriteRenderer> _animatedGhosts =
        new Dictionary<GameObject, SpriteRenderer>();

    private readonly Dictionary<GameObject, TargetHandleInput> _targetInputs =
        new Dictionary<GameObject, TargetHandleInput>();

    private readonly Dictionary<GameObject, Vector3> _lastPositions = new Dictionary<GameObject, Vector3>();
    private readonly List<LineRenderer> _lines = new List<LineRenderer>();
    private readonly List<GameObject> _lineObjects = new List<GameObject>();
    private Material _material;
    private LevelPartGame _dragging;
    private LevelPartGame _draggingOrigin;
    private int _overlaySortingLayer;
    private bool _overlaySortingLayerResolved;

    internal void Update(LevelEditor editor, bool editingText)
    {
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!shift)
        {
            _dragging = null;
            SetLineCount(0);
            SetGhosts(false);
            SetInputs(false);
            return;
        }

        var props = new List<LevelPartGame>();
        var teleportZones = new List<LevelPartGame>();
        var targetParts = new List<LevelPartGame>();
        var changersByDoorId = new Dictionary<int, List<LevelPartGame>>();
        foreach (var part in editor.GetComponentsInChildren<LevelPartGame>(true))
        {
            if (IsMovingPart(part))
            {
                props.Add(part);
                targetParts.Add(part);
            }
            else if (IsTeleportZone(part))
            {
                teleportZones.Add(part);
                targetParts.Add(part);
            }
            else if (part != null && part.part != null && IsDoorTargetChanger(part))
            {
                List<LevelPartGame> changers;
                if (!changersByDoorId.TryGetValue(part.part.activId, out changers))
                {
                    changers = new List<LevelPartGame>();
                    changersByDoorId.Add(part.part.activId, changers);
                }

                changers.Add(part);
            }
        }

        TrackTargetOffsets(props);

        if (!editingText && Input.GetMouseButtonDown(0))
        {
            foreach (var part in props)
            {
                List<LevelPartGame> changers;
                if (!changersByDoorId.TryGetValue(part.part.id, out changers))
                    continue;
                foreach (var changer in changers)
                    if (Near(changer.part.force))
                    {
                        _dragging = changer;
                        _draggingOrigin = part;
                        break;
                    }

                if (_dragging != null)
                    break;
            }

            if (_dragging == null)
                foreach (var part in targetParts)
                    if (Near(part.part.force))
                    {
                        _dragging = part;
                        _draggingOrigin = part;
                        break;
                    }
        }

        if (_dragging != null)
        {
            if (Input.GetMouseButton(0))
                SetTarget(editor, _dragging, _draggingOrigin, CursorWorld());
            else
            {
                _dragging = null;
                _draggingOrigin = null;
            }
        }

        var lineCount = (props.Count + teleportZones.Count) * 2;
        foreach (var part in props)
        {
            List<LevelPartGame> changers;
            if (changersByDoorId.TryGetValue(part.part.id, out changers))
                lineCount += changers.Count * 3;
        }

        SetLineCount(lineCount);
        var index = 0;
        foreach (var part in props)
        {
            index = DrawTarget(_lines, index, part, part, TargetColors[0]);
            UpdateInput(editor, part, part);
            List<LevelPartGame> changers;
            if (!changersByDoorId.TryGetValue(part.part.id, out changers))
                changers = null;
            for (var i = 0; changers != null && i < changers.Count; i++)
            {
                var changer = changers[i];
                var color = TargetColors[(i + 1) % TargetColors.Length];
                index = DrawTarget(_lines, index, part, changer, color);
                SetSegment(_lines[index++], changer.transform.position, changer.part.force,
                    color);
                UpdateInput(editor, changer, part);
                UpdateGhost(part, changer);
                targetParts.Add(changer);
            }

            UpdateGhost(part, part);
        }

        foreach (var part in teleportZones)
        {
            index = DrawTarget(_lines, index, part, part, TargetColors[1]);
            UpdateInput(editor, part, part);
        }

        RemoveUnusedGhosts(targetParts);
        RemoveUnusedInputs(targetParts);
    }

    private void TrackTargetOffsets(List<LevelPartGame> parts)
    {
        var valid = new HashSet<GameObject>();
        foreach (var part in parts)
        {
            valid.Add(part.gameObject);
            Vector3 previous;
            if (_lastPositions.TryGetValue(part.gameObject, out previous))
            {
                var delta = part.transform.position - previous;
                if (delta.sqrMagnitude > .0000001f)
                    part.part.force += (Vector2)delta;
            }

            _lastPositions[part.gameObject] = part.transform.position;
        }

        var remove = new List<GameObject>();
        foreach (var pair in _lastPositions)
            if (!pair.Key || !valid.Contains(pair.Key))
                remove.Add(pair.Key);
        foreach (var item in remove)
            _lastPositions.Remove(item);
    }

    private static bool IsMovingPart(LevelPartGame part)
    {
        if (part == null || part.part == null)
            return false;
        var name = part.fullName;
        return name == "Door" || name == "Striped Door" || name == "Secret Door" || name == "Moving Background";
    }

    private static bool IsDoorTargetChanger(LevelPartGame part)
    {
        var path = part.part.path;
        return path == "Building/Triggers/DoorPosChanger" || path == "Editor/Building/Triggers/DoorPosChanger" ||
               part.fullName == "Door Target Changer";
    }

    private static bool IsTeleportZone(LevelPartGame part)
    {
        if (part == null || part.part == null)
            return false;
            
        return string.Equals(part.fullName, "Teleport Zone", System.StringComparison.OrdinalIgnoreCase) ||
               (part.part.path != null && part.part.path.EndsWith("/TeleportZone", System.StringComparison.OrdinalIgnoreCase));
    }

    private static int DrawTarget(List<LineRenderer> lines, int index, LevelPartGame door, LevelPartGame targetPart,
        Color color)
    {
        var target = (Vector3)targetPart.part.force;
        SetSegment(lines[index++], door.transform.position, target, color);
        SetCircle(lines[index++], target, color);
        return index;
    }

    private int OverlaySortingLayer
    {
        get
        {
            if (_overlaySortingLayerResolved)
                return _overlaySortingLayer;
            var layers = SortingLayer.layers;
            _overlaySortingLayer = layers.Length == 0 ? 0 : layers[layers.Length - 1].id;
            _overlaySortingLayerResolved = true;
            return _overlaySortingLayer;
        }
    }

    private void UpdateGhost(LevelPartGame door, LevelPartGame targetPart)
    {
        var source = door.GetComponent<SpriteRenderer>();
        var ghost = GetGhost(_ghosts, targetPart.gameObject, "Gunsaw Motion Endpoint Preview");
        if (source == null)
        {
            ghost.gameObject.SetActive(false);
            return;
        }

        CopyAppearance(source, ghost, door.transform);
        var color = source.color;
        color.a = .28f;
        ghost.color = color;
        ghost.transform.position = targetPart.part.force;
        ghost.gameObject.SetActive(true);

        var animated = GetGhost(_animatedGhosts, targetPart.gameObject, "Gunsaw Motion Animation Preview");
        CopyAppearance(source, animated, door.transform);
        animated.color = color;
        var distance = Vector2.Distance(door.transform.position, targetPart.part.force);
        var speed = Mathf.Max(.1f, Mathf.Abs(door.part.activId));
        var progress = distance < .001f ? 0f : Mathf.PingPong(Time.unscaledTime * speed / distance, 1f);
        animated.transform.position = Vector3.Lerp(door.transform.position, targetPart.part.force, progress);
        animated.gameObject.SetActive(true);
    }

    private static SpriteRenderer GetGhost(Dictionary<GameObject, SpriteRenderer> ghosts, GameObject key, string name)
    {
        SpriteRenderer ghost;
        if (ghosts.TryGetValue(key, out ghost) && ghost)
            return ghost;
        var go = new GameObject(name);
        ghost = go.AddComponent<SpriteRenderer>();
        ghosts[key] = ghost;
        return ghost;
    }

    private static void CopyAppearance(SpriteRenderer source, SpriteRenderer target, Transform transform)
    {
        target.sprite = source.sprite;
        target.drawMode = source.drawMode;
        target.size = source.size;
        target.transform.rotation = transform.rotation;
        target.transform.localScale = transform.localScale;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder + 1;
    }

    private void RemoveUnusedGhosts(List<LevelPartGame> props)
    {
        var valid = new HashSet<GameObject>();
        foreach (var part in props)
            valid.Add(part.gameObject);
        var remove = new List<GameObject>();
        foreach (var pair in _ghosts)
            if (!pair.Key || !valid.Contains(pair.Key))
            {
                if (pair.Value)
                    Object.Destroy(pair.Value.gameObject);
                remove.Add(pair.Key);
            }

        foreach (var key in remove)
        {
            _ghosts.Remove(key);
        }

        remove.Clear();
        foreach (var pair in _animatedGhosts)
            if (!pair.Key || !valid.Contains(pair.Key))
            {
                if (pair.Value)
                    Object.Destroy(pair.Value.gameObject);
                remove.Add(pair.Key);
            }

        foreach (var key in remove)
            _animatedGhosts.Remove(key);
    }

    private void UpdateInput(LevelEditor editor, LevelPartGame part, LevelPartGame origin)
    {
        TargetHandleInput input;
        if (!_targetInputs.TryGetValue(part.gameObject, out input) || !input)
        {
            var go = new GameObject("Gunsaw Motion Target Input", typeof(RectTransform), typeof(Image),
                typeof(TargetHandleInput));
            var image = go.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            input = go.GetComponent<TargetHandleInput>();
            input.Owner = this;
            input.Part = part;
            _targetInputs[part.gameObject] = input;
        }

        input.Origin = origin;
        input.Editor = editor;

        var canvas = editor.infoText.canvas;
        var rect = input.GetComponent<RectTransform>();
        input.transform.SetParent(canvas.transform, false);
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,
            Camera.main.WorldToScreenPoint(part.part.force),
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = point;
        rect.sizeDelta = new Vector2(96f, 96f);
        input.gameObject.SetActive(true);
        input.transform.SetAsLastSibling();
    }

    private void RemoveUnusedInputs(List<LevelPartGame> props)
    {
        var valid = new HashSet<GameObject>();
        foreach (var part in props)
            valid.Add(part.gameObject);
        var remove = new List<GameObject>();
        foreach (var pair in _targetInputs)
            if (!pair.Key || !valid.Contains(pair.Key))
            {
                if (pair.Value)
                    Object.Destroy(pair.Value.gameObject);
                remove.Add(pair.Key);
            }

        foreach (var key in remove)
            _targetInputs.Remove(key);
    }

    private void SetGhosts(bool active)
    {
        foreach (var ghost in _ghosts.Values)
            if (ghost)
                ghost.gameObject.SetActive(active);
        foreach (var ghost in _animatedGhosts.Values)
            if (ghost)
                ghost.gameObject.SetActive(active);
    }

    private void SetInputs(bool active)
    {
        foreach (var input in _targetInputs.Values)
            if (input)
                input.gameObject.SetActive(active);
    }

    private void SetLineCount(int count)
    {
        while (_lines.Count < count)
        {
            var go = new GameObject("Gunsaw Motion Target");
            var line = go.AddComponent<LineRenderer>();
            line.material = Material;
            line.useWorldSpace = true;
            line.widthMultiplier = .045f;
            line.numCapVertices = 3;
            line.sortingLayerID = OverlaySortingLayer;
            line.sortingOrder = short.MaxValue;
            _lines.Add(line);
            _lineObjects.Add(go);
        }

        for (var i = 0; i < _lines.Count; i++)
            _lines[i].gameObject.SetActive(i < count);
    }

    private static void SetSegment(LineRenderer line, Vector3 from, Vector3 to, Color color)
    {
        line.positionCount = 2;
        line.loop = false;
        line.startColor = color;
        line.endColor = color;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    private static void SetCircle(LineRenderer line, Vector3 center, Color color)
    {
        const int count = 18;
        line.positionCount = count;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        for (var i = 0; i < count; i++)
        {
            var angle = i * Mathf.PI * 2f / count;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * .16f, Mathf.Sin(angle) * .16f, 0f));
        }
    }

    private static bool Near(Vector2 point)
    {
        if (Camera.main == null)
            return false;
        var screen = Camera.main.WorldToScreenPoint(point);
        var delta = new Vector2(screen.x, screen.y) - new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        return delta.sqrMagnitude <= HitRadiusPixels * HitRadiusPixels;
    }

    private static Vector2 CursorWorld()
    {
        if (Camera.main == null)
            return Vector2.zero;
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }

    private static Vector2 SnapTarget(LevelPartGame origin, Vector2 point)
    {
        var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (alt || origin == null)
            return point;
        var vector = point - (Vector2)origin.transform.position;
        if (vector.sqrMagnitude < .000001f)
            return point;
        var angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
        angle = Mathf.Round(angle / 15f) * 15f * Mathf.Deg2Rad;
        return (Vector2)origin.transform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * vector.magnitude;
    }

    private static void SetTarget(LevelEditor editor, LevelPartGame part, LevelPartGame origin, Vector2 point)
    {
        part.part.force = SnapTarget(origin, point);
        if (editor != null && editor.currentlySelected == part.gameObject)
        {
            editor.forceXField.SetTextWithoutNotify(part.part.force.x.ToString());
            editor.forceYField.SetTextWithoutNotify(part.part.force.y.ToString());
        }
    }

    private static Vector2 WorldFromScreen(Vector2 screen)
    {
        if (Camera.main == null)
            return Vector2.zero;
        return Camera.main.ScreenToWorldPoint(screen);
    }

    private Material Material
    {
        get
        {
            if (_material == null)
                _material = new Material(Shader.Find("Sprites/Default"));
            return _material;
        }
    }

    internal void Dispose()
    {
        foreach (var ghost in _ghosts.Values)
            if (ghost)
                Object.Destroy(ghost.gameObject);
        foreach (var ghost in _animatedGhosts.Values)
            if (ghost)
                Object.Destroy(ghost.gameObject);
        foreach (var input in _targetInputs.Values)
            if (input)
                Object.Destroy(input.gameObject);
        _ghosts.Clear();
        _animatedGhosts.Clear();
        _targetInputs.Clear();
        _lastPositions.Clear();
        foreach (var line in _lineObjects)
            if (line)
                Object.Destroy(line);
        _lineObjects.Clear();
        _lines.Clear();
        if (_material)
            Object.Destroy(_material);
        _material = null;
        _dragging = null;
        _draggingOrigin = null;
    }

    private sealed class TargetHandleInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        internal DoorMotionOverlay Owner;
        internal LevelEditor Editor;
        internal LevelPartGame Part;
        internal LevelPartGame Origin;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Part)
                SetTarget(Editor, Part, Origin, WorldFromScreen(eventData.position));
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Part)
                SetTarget(Editor, Part, Origin, WorldFromScreen(eventData.position));
        }
    }
}