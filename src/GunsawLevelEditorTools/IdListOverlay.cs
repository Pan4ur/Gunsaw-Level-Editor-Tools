using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunsawLevelEditorTools;

internal sealed class IdListOverlay
{
    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Row> _rows = new List<Row>();
    private GameObject _root;
    private RectTransform _content;
    private TMP_Text _title;
    private TMP_Text _groundLabel;
    private bool _visible;
    private float _nextUpdate;
    private int _entriesHash = int.MinValue;
    private Color _rowColor;
    private bool _showGround;

    internal void Toggle(LevelEditor editor)
    {
        _visible = !_visible;
        if (_visible)
        {
            EnsureUi(editor);
            _nextUpdate = 0f;
            _entriesHash = int.MinValue;
        }

        if (_root)
            _root.SetActive(_visible);
    }

    internal void Update(LevelEditor editor)
    {
        if (!_visible)
            return;
        EnsureUi(editor);
        if (Time.unscaledTime < _nextUpdate)
            return;
        _nextUpdate = Time.unscaledTime + .35f;
        BuildEntries(editor);
        var hash = EntriesHash();
        if (hash == _entriesHash)
            return;
        _entriesHash = hash;
        RebuildRows(editor);
    }

    private void EnsureUi(LevelEditor editor)
    {
        if (_root)
            return;
        var canvas = editor.infoText.canvas;
        _root = new GameObject("Gunsaw IDs List", typeof(RectTransform), typeof(Image));
        _root.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)_root.transform;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-18f, -118f);
        rect.sizeDelta = new Vector2(440f, 645f);
        var uiColor = new Color(.32f, .32f, .32f, .96f);
        _root.GetComponent<Image>().color = new Color(.12f, .12f, .12f, .96f);
        _rowColor = new Color(.22f, .22f, .22f, .96f);
        _title = UnityEngine.Object.Instantiate(editor.infoText, _root.transform);
        _title.alignment = TextAlignmentOptions.Center;
        _title.fontSize = 20f;
        _title.color = Color.white;
        _title.raycastTarget = false;
        _title.rectTransform.anchorMin = new Vector2(0f, 1f);
        _title.rectTransform.anchorMax = new Vector2(1f, 1f);
        _title.rectTransform.pivot = new Vector2(.5f, 1f);
        _title.rectTransform.anchoredPosition = new Vector2(14f, -10f);
        _title.rectTransform.sizeDelta = new Vector2(-28f, 30f);
        var groundButton = new GameObject("Ground Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
        groundButton.transform.SetParent(_root.transform, false);
        var groundRect = groundButton.GetComponent<RectTransform>();
        groundRect.anchorMin = new Vector2(1f, 1f);
        groundRect.anchorMax = new Vector2(1f, 1f);
        groundRect.pivot = new Vector2(1f, 1f);
        groundRect.anchoredPosition = new Vector2(-12f, -9f);
        groundRect.sizeDelta = new Vector2(88f, 27f);
        groundButton.GetComponent<Image>().color = _rowColor;
        var groundAction = groundButton.GetComponent<Button>();
        groundAction.targetGraphic = groundButton.GetComponent<Image>();
        groundAction.onClick.AddListener(ToggleGround);
        _groundLabel = UnityEngine.Object.Instantiate(_title, groundButton.transform);
        _groundLabel.alignment = TextAlignmentOptions.Center;
        _groundLabel.fontSize = 12f;
        _groundLabel.raycastTarget = false;
        _groundLabel.rectTransform.anchorMin = Vector2.zero;
        _groundLabel.rectTransform.anchorMax = Vector2.one;
        _groundLabel.rectTransform.offsetMin = Vector2.zero;
        _groundLabel.rectTransform.offsetMax = Vector2.zero;
        UpdateGroundLabel();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(_root.transform, false);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(10f, 10f);
        viewportRect.offsetMax = new Vector2(-28f, -48f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, .16f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        _content = content.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(0f, 0f);
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 2f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = _root.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 18f;
        var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        bar.transform.SetParent(_root.transform, false);
        var barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, .5f);
        barRect.anchoredPosition = new Vector2(-8f, -19f);
        barRect.sizeDelta = new Vector2(12f, -58f);
        bar.GetComponent<Image>().color = Color.Lerp(uiColor, Color.black, .58f);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(bar.transform, false);
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        handle.GetComponent<Image>().color = uiColor;
        var scrollbar = bar.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        _root.SetActive(_visible);
        _root.transform.SetAsLastSibling();
    }

    private void BuildEntries(LevelEditor editor)
    {
        _entries.Clear();
        foreach (var part in editor.GetComponentsInChildren<LevelPartGame>(true))
        {
            if (part == null || part.part == null)
                continue;
            if (IsPlayerSpawn(part) || IsExcluded(part))
                continue;
            if (part.part.activId > 0 && !IsMovingPart(part))
                Add(part.gameObject, part.fullName, part.transform.position, "Activation ID", part.part.activId);
            if (part.part.id > 0)
                Add(part.gameObject, part.fullName, part.transform.position, IsButton(part) ? "Output" : "Input",
                    part.part.id);
        }

        if (_showGround)
        {
            foreach (var point in editor.GetComponentsInChildren<GroundPoint>(true))
                if (point != null)
                    Add(point.gameObject, "Ground Point", point.transform.position, "Ground", point.id);
        }

        foreach (var marker in editor.GetComponentsInChildren<MonoBehaviour>(true))
        {
            object data;
            if (!MultiplayerLogic.TryGetData(marker, out data))
                continue;
            AddCustomEntries(marker, data);
        }

        _entries.Sort((left, right) => right.Id.CompareTo(left.Id));
    }

    private void Add(GameObject item, string name, Vector3 position, string channel, int id)
    {
        _entries.Add(new Entry { Item = item, Name = name, Position = position, Channel = channel, Id = id });
    }

    private void AddCustomEntries(MonoBehaviour marker, object data)
    {
        MultiplayerLogic.VisitSignals(data, (name, id, output) => AddCustom(marker, name, id));
    }

    private void AddCustom(MonoBehaviour marker, string channel, int id)
    {
        if (id > 0)
            Add(marker.gameObject, marker.name, marker.transform.position, channel, id);
    }

    private void ToggleGround()
    {
        _showGround = !_showGround;
        _entriesHash = int.MinValue;
        _nextUpdate = 0f;
        UpdateGroundLabel();
    }

    private void UpdateGroundLabel()
    {
        if (_groundLabel)
            _groundLabel.text = _showGround ? "Ground: ON" : "Ground";
    }

    private void RebuildRows(LevelEditor editor)
    {
        _title.text = "IDs";
        while (_rows.Count < _entries.Count)
            _rows.Add(CreateRow());
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= _entries.Count)
            {
                row.Root.SetActive(false);
                continue;
            }

            var entry = _entries[i];
            row.Root.SetActive(true);
            row.Editor = editor;
            row.Item = entry.Item;
            var renderer = entry.Item ? entry.Item.GetComponentInChildren<SpriteRenderer>(true) : null;
            row.Icon.sprite = renderer ? renderer.sprite : null;
            row.Icon.color = renderer ? renderer.color : Color.clear;
            row.Text.text = entry.Name + "  |  (" + entry.Position.x.ToString("0.##") + ", " +
                            entry.Position.y.ToString("0.##") + ")  |  " + entry.Channel + ": " + entry.Id;
        }
    }

    private Row CreateRow()
    {
        var rowObject = new GameObject("ID Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement),
            typeof(Button));
        rowObject.transform.SetParent(_content, false);
        rowObject.GetComponent<Image>().color = _rowColor;
        var layoutElement = rowObject.GetComponent<LayoutElement>();
        layoutElement.minHeight = 29f;
        layoutElement.preferredHeight = 29f;
        layoutElement.flexibleHeight = 0f;
        var row = new Row { Root = rowObject, Editor = null, Item = null };
        var button = rowObject.GetComponent<Button>();
        button.targetGraphic = rowObject.GetComponent<Image>();
        button.onClick.AddListener(() => Focus(row.Editor, row.Item));
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(rowObject.transform, false);
        row.Icon = icon.GetComponent<Image>();
        var iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, .5f);
        iconRect.anchorMax = new Vector2(0f, .5f);
        iconRect.pivot = new Vector2(0f, .5f);
        iconRect.anchoredPosition = new Vector2(5f, 0f);
        iconRect.sizeDelta = new Vector2(22f, 22f);
        row.Text = UnityEngine.Object.Instantiate(_title, rowObject.transform);
        row.Text.alignment = TextAlignmentOptions.Left;
        row.Text.enableWordWrapping = false;
        row.Text.fontSize = 11f;
        row.Text.color = Color.white;
        row.Text.raycastTarget = false;
        var textRect = row.Text.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(32f, 0f);
        textRect.offsetMax = new Vector2(-4f, 0f);
        return row;
    }

    private int EntriesHash()
    {
        unchecked
        {
            var hash = _entries.Count;
            foreach (var entry in _entries)
            {
                hash = hash * 31 + (entry.Item ? entry.Item.GetInstanceID() : 0);
                hash = hash * 31 + entry.Id;
                hash = hash * 31 + (entry.Channel == null ? 0 : entry.Channel.GetHashCode());
                hash = hash * 31 + Mathf.RoundToInt(entry.Position.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(entry.Position.y * 100f);
            }

            return hash;
        }
    }

    private static bool IsButton(LevelPartGame part)
    {
        return Normalize(part.fullName) == "button" || Normalize(part.part.path).EndsWith("/button");
    }

    private static void Focus(LevelEditor editor, GameObject item)
    {
        if (!item)
            return;
        if (Camera.main)
            Camera.main.transform.position = new Vector3(item.transform.position.x, item.transform.position.y,
                Camera.main.transform.position.z);
        var collider = item.GetComponent<Collider2D>();
        if (collider != null)
            editor.SelectPart(collider);
        else
        {
            editor.currentlySelected = item;
        }
    }

    private static bool IsPlayerSpawn(LevelPartGame part)
    {
        return Normalize(part.fullName) == "playerspawnpoint" || Normalize(part.fullName) == "playerspawn" ||
               (part.part.path ?? string.Empty).EndsWith("/PlayerSpawn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcluded(LevelPartGame part)
    {
        var name = Normalize(part.fullName);
        var path = Normalize(part.part.path);
        return name == "acid" || name == "water" || path.EndsWith("/acid") || path.EndsWith("/water");
    }

    private static bool IsMovingPart(LevelPartGame part)
    {
        var name = Normalize(part.fullName);
        return name == "door" || name == "stripeddoor" || name == "secretdoor" || name == "movingbackground";
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    internal void Dispose()
    {
        if (_root)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _content = null;
        _title = null;
        _groundLabel = null;
        _visible = false;
        _entries.Clear();
        _rows.Clear();
        _entriesHash = int.MinValue;
    }

    private struct Entry
    {
        internal GameObject Item;
        internal string Name;
        internal Vector3 Position;
        internal string Channel;
        internal int Id;
    }

    private sealed class Row
    {
        internal GameObject Root;
        internal Image Icon;
        internal TMP_Text Text;
        internal LevelEditor Editor;
        internal GameObject Item;
    }
}