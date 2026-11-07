using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunsawLevelEditorTools;

internal sealed class EditorHotbar
{
    private readonly Slot[] _slots =
        { new Slot(), new Slot(), new Slot(), new Slot(), new Slot(), new Slot(), new Slot() };

    private readonly HashSet<int> _boundIcons = new HashSet<int>();
    private GameObject _root;
    private string _activePath;
    private string _placementPath;
    private Vector2 _placementStart;
    private bool _visible = true;
    private Color _baseColor;
    private Color _selectedColor;
    private Color _dragColor;
    private Sprite _frameSprite;
    private Image.Type _frameType;
    private Material _frameMaterial;
    private LevelEditor _editor;
    private MonoBehaviour _customController;

    internal bool Visible => _visible;

    internal void Toggle(LevelEditor editor)
    {
        _visible = !_visible;
        EnsureUi(editor);
        if (_root)
            _root.SetActive(_visible);
        editor.SetInfoText(_visible ? "Hotbar: ON" : "Hotbar: OFF");
    }

    internal void Update(LevelEditor editor, bool editingText)
    {
        _editor = editor;
        _customController = MultiplayerLogic.Controller(editor);
        EnsureUi(editor);
        BindSpawnIcons(editor);
        if (!string.IsNullOrEmpty(editor.curSpawnPath) && MultiplayerLogic.Placement(_customController) != null)
        {
            MultiplayerLogic.EndPlacement(_customController);
            _activePath = null;
        }
        if ((editor.miniHeldObj == null || editor.miniHeldObj.sprite == null) &&
            MultiplayerLogic.Placement(_customController) == null)
            _activePath = null;
        if (_placementPath != null)
        {
            if (!Input.GetMouseButtonUp(1))
                return;
            var path = _placementPath;
            _placementPath = null;
            if (((Vector2)Input.mousePosition - _placementStart).sqrMagnitude <= 64f)
                Place(editor, path);
            return;
        }

        if (editingText)
            return;
        var slot = PressedSlot();
        if (slot >= 0 && !string.IsNullOrEmpty(_slots[slot].Path))
        {
            SelectSlot(slot);
        }

        if (!Input.GetMouseButtonDown(1) || string.IsNullOrEmpty(_activePath) ||
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
            (UnityEngine.EventSystems.EventSystem.current != null &&
             UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            return;
        _placementPath = _activePath;
        _placementStart = Input.mousePosition;
    }

    private void Place(LevelEditor editor, string path)
    {
        foreach (var slot in _slots)
            if (slot.Path == path && slot.Definition != null)
            {
                if (_customController != null && Camera.main != null)
                    MultiplayerLogic.Place(_customController, slot.Definition,
                        editor.alignToGrid(Camera.main.ScreenToWorldPoint(Input.mousePosition)));
                return;
            }
        var prefab = Resources.Load<GameObject>(path);
        if (!prefab || Camera.main == null)
            return;
        var position = editor.alignToGrid(Camera.main.ScreenToWorldPoint(Input.mousePosition));
        var placed = Object.Instantiate(prefab, position, Quaternion.identity);
        placed.transform.SetParent(editor.transform);
        editor.UpdateLighting();
    }

    internal void Dispose()
    {
        if (_root)
            Object.Destroy(_root);
        _root = null;
        _boundIcons.Clear();
        _activePath = null;
        _placementPath = null;
        _customController = null;
    }

    private void BindSpawnIcons(LevelEditor editor)
    {
        var canvas = editor.infoText == null ? null : editor.infoText.canvas;
        if (canvas != null)
            BindSpawnIcons(canvas.GetComponentsInChildren<SpawnIcon>(true));
        BindSpawnIcons(Object.FindObjectsOfType<SpawnIcon>());
    }

    private void BindSpawnIcons(SpawnIcon[] icons)
    {
        foreach (var icon in icons)
        {
            if (icon == null || !_boundIcons.Add(icon.GetInstanceID()))
                continue;
            var captured = icon;
            var button = icon.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(() => AssignHeldSlot(captured));
        }
    }

    private void AssignHeldSlot(SpawnIcon icon)
    {
        var slot = HeldSlot();
        if (slot < 0 || icon == null)
            return;
        var item = _slots[slot];
        var definition = string.IsNullOrEmpty(_editor.curSpawnPath)
            ? MultiplayerLogic.Placement(_customController) : null;
        if (definition == null && string.IsNullOrEmpty(icon.spawnString))
            return;
        item.Definition = definition;
        item.Path = definition == null ? icon.spawnString : MultiplayerLogic.ReadString(definition, "TypeId");
        var prefab = definition == null ? Resources.Load<GameObject>(item.Path) : null;
        var spriteRenderer = prefab ? prefab.GetComponent<SpriteRenderer>() : null;
        item.Icon.sprite = definition != null ? MultiplayerLogic.Icon(definition) : spriteRenderer ? spriteRenderer.sprite : null;
        item.Icon.enabled = item.Icon.sprite != null;
        _activePath = item.Path;
        UpdateUi();
    }

    private void EnsureUi(LevelEditor editor)
    {
        if (_root || editor.infoText == null)
            return;
        var canvas = editor.infoText.canvas;
        var nativeImage = FindNativeFrame(editor);
        var nativeColor = nativeImage == null ? new Color(.2f, .2f, .2f, .9f) : nativeImage.color;
        _baseColor = nativeColor;
        _selectedColor = nativeColor;
        _selectedColor.r = Mathf.Min(1f, _selectedColor.r + .12f);
        _selectedColor.g = Mathf.Min(1f, _selectedColor.g + .12f);
        _selectedColor.b = Mathf.Min(1f, _selectedColor.b + .12f);
        _dragColor = nativeColor;
        _frameSprite = nativeImage == null ? null : nativeImage.sprite;
        _frameType = nativeImage == null ? Image.Type.Simple : nativeImage.type;
        _frameMaterial = nativeImage == null ? null : nativeImage.material;
        _root = new GameObject("Gunsaw Editor Hotbar", typeof(RectTransform));
        _root.transform.SetParent(canvas.transform, false);
        var root = (RectTransform)_root.transform;
        root.anchorMin = new Vector2(0f, .5f);
        root.anchorMax = new Vector2(0f, .5f);
        root.pivot = new Vector2(0f, .5f);
        root.anchoredPosition = new Vector2(16f, 0f);
        root.sizeDelta = new Vector2(428f, 74f);
        var dragZone = new GameObject("Hotbar Drag Zone", typeof(RectTransform), typeof(Image),
            typeof(HotbarDragInput));
        dragZone.transform.SetParent(_root.transform, false);
        var dragRect = (RectTransform)dragZone.transform;
        dragRect.anchorMin = new Vector2(0f, 1f);
        dragRect.anchorMax = new Vector2(0f, 1f);
        dragRect.pivot = new Vector2(0f, 1f);
        dragRect.anchoredPosition = Vector2.zero;
        dragRect.sizeDelta = new Vector2(428f, 14f);
        dragZone.GetComponent<Image>().color = _dragColor;
        dragZone.GetComponent<HotbarDragInput>().Owner = this;
        for (var i = 0; i < _slots.Length; i++)
        {
            var box = new GameObject("Hotbar " + SlotName(i), typeof(RectTransform), typeof(Image));
            box.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)box.transform;
            rect.anchorMin = new Vector2(0f, .5f);
            rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(i * 62f, -8f);
            rect.sizeDelta = new Vector2(56f, 56f);
            var image = box.GetComponent<Image>();
            if (image == null)
                image = box.AddComponent<Image>();
            image.color = _baseColor;
            image.sprite = _frameSprite;
            image.type = _frameType;
            image.material = _frameMaterial;
            image.raycastTarget = true;
            var outline = box.AddComponent<Outline>();
            outline.effectColor = new Color(.9f, .9f, .9f, .85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.enabled = false;
            var input = box.AddComponent<HotbarSlotInput>();
            input.Owner = this;
            input.Index = i;
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(box.transform, false);
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(5f, 5f);
            iconRect.offsetMax = new Vector2(-5f, -5f);
            var iconImage = icon.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.enabled = false;
            var label = Object.Instantiate(editor.infoText, box.transform);
            label.text = SlotName(i);
            label.alignment = TextAlignmentOptions.BottomRight;
            label.fontSize = 17f;
            label.raycastTarget = false;
            label.color = Color.white;
            label.rectTransform.anchorMin = new Vector2(1f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(1f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(-4f, 2f);
            label.rectTransform.sizeDelta = new Vector2(22f, 22f);
            label.transform.SetAsLastSibling();
            _slots[i].Box = image;
            _slots[i].Icon = iconImage;
            _slots[i].Outline = outline;
        }

        _root.transform.SetAsLastSibling();
        _root.SetActive(_visible);
        UpdateUi();
    }

    private void UpdateUi()
    {
        foreach (var slot in _slots)
        {
            if (slot == null || slot.Box == null)
                continue;
            var selected = slot.Path == _activePath && !string.IsNullOrEmpty(slot.Path);
            slot.Box.color = selected ? _selectedColor : _baseColor;
            if (slot.Outline != null)
                slot.Outline.enabled = selected;
        }
    }

    private void SelectSlot(int index)
    {
        if (_editor == null || index < 0 || index >= _slots.Length || string.IsNullOrEmpty(_slots[index].Path))
            return;
        _activePath = _slots[index].Path;
        if (_slots[index].Definition != null)
        {
            if (_customController != null)
                MultiplayerLogic.BeginPlacement(_customController, _slots[index].Definition);
        }
        else
        {
            MultiplayerLogic.EndPlacement(_customController);
            _editor.SelectObj(_activePath);
        }
        UpdateUi();
    }

    private static Image FindNativeFrame(LevelEditor editor)
    {
        if (editor.objMenu != null)
        {
            var image = editor.objMenu.GetComponent<Image>();
            if (image != null)
                return image;
        }

        if (editor.spawnButtonPrefab != null)
        {
            var image = editor.spawnButtonPrefab.GetComponent<Image>();
            if (image != null)
                return image;
        }

        var canvas = editor.infoText == null ? null : editor.infoText.canvas;
        if (canvas == null)
            return null;
        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
        {
            var image = button.GetComponent<Image>();
            if (image != null)
                return image;
        }

        return null;
    }


    private static int PressedSlot()
    {
        for (var i = 0; i < 6; i++)
            if (Input.GetKeyDown(KeyCode.Alpha4 + i))
                return i;
        return Input.GetKeyDown(KeyCode.Alpha0) ? 6 : -1;
    }

    private static int HeldSlot()
    {
        for (var i = 0; i < 6; i++)
            if (Input.GetKey(KeyCode.Alpha4 + i))
                return i;
        return Input.GetKey(KeyCode.Alpha0) ? 6 : -1;
    }

    private static string SlotName(int index)
    {
        return index == 6 ? "0" : (index + 4).ToString();
    }

    private sealed class Slot
    {
        internal string Path;
        internal object Definition;
        internal Image Box;
        internal Image Icon;
        internal Outline Outline;
    }

    private sealed class HotbarSlotInput : MonoBehaviour, IPointerClickHandler
    {
        internal EditorHotbar Owner;
        internal int Index;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null)
                Owner.SelectSlot(Index);
        }
    }

    private sealed class HotbarDragInput : MonoBehaviour, IDragHandler
    {
        internal EditorHotbar Owner;

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner == null || Owner._root == null)
                return;
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas == null ? 1f : canvas.scaleFactor;
            ((RectTransform)Owner._root.transform).anchoredPosition += eventData.delta / scale;
        }
    }
}