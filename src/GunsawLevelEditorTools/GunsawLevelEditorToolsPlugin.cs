using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using GunsawImageCode;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunsawLevelEditorTools;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class GunsawLevelEditorToolsPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "pan4ur.gunsaw.leveleditortools";
    public const string PluginName = "Gunsaw Level Editor Tools";
    public const string PluginVersion = "0.0.1";
    private const int MarkerId = -7319841;

    private readonly Dictionary<ArtObjectKind, GameObject> _prefabs = new Dictionary<ArtObjectKind, GameObject>();
    private LevelEditor _lastEditor;
    private GameObject _uiRoot;
    private GameObject _placementOverlay;
    private GameObject _placementGroup;
    private Button _pasteButton;
    private Button _idsButton;
    private Button _hotbarButton;
    private TMP_Text _pasteLabel;
    private TMP_Text _idsLabel;
    private TMP_Text _hotbarLabel;
    private TMP_Text _helpLabel;
    private bool _importing;
    private bool _cancelImport;
    private bool _placementActive;
    private float _placementScale = 1f;
    private float _nextRestore;
    private readonly LogicConnectionOverlay _logicConnections = new();
    private readonly LevelEditingTools _levelTools = new();
    private readonly DoorMotionOverlay _doorMotion = new();
    private readonly EditorHistory _history = new();
    private readonly LampColorPicker _lampColorPicker = new();
    private readonly IdListOverlay _idList = new();
    private readonly GroundPreview _groundPreview = new();
    private readonly GroundPointIdAllocator _groundPointIds = new();
    private readonly EditorHotbar _hotbar = new();
    private bool _duplicatingThisFrame;

    private void Awake()
    {
        Logger.LogInfo(PluginName + " loaded.");
    }

    private void Update()
    {
        var editor = LevelEditor.main;
        if (!editor)
        {
            if (_lastEditor)
                LeaveEditor();
            return;
        }

        if (editor.editorWarning)
            editor.editorWarning.SetActive(false);

        if (_lastEditor != editor)
        {
            LeaveEditor();
            _lastEditor = editor;
            ResolvePrefabs();
            StartCoroutine(CreateNativeUiAfterStart(editor));
            _nextRestore = Time.unscaledTime + 0.5f;
            _groundPointIds.Reset(editor);
        }

        var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (control && Input.GetKeyDown(KeyCode.U) && !EditingTextField())
        {
            _groundPointIds.Toggle(editor);
            return;
        }

        if (!_placementActive && !_importing && _groundPointIds.TryPlaceFromSelected(editor))
        {
            _groundPreview.RefreshNow(editor);
            return;
        }

        _duplicatingThisFrame = control && Input.GetKeyDown(KeyCode.D);

        if (control && Input.GetKeyDown(KeyCode.G) && !EditingTextField())
        {
            _groundPreview.Toggle(editor);
            return;
        }

        _levelTools.Prepare(editor);
        if (!_placementActive && !_importing && !EditingTextField() && _levelTools.TryDuplicate(editor))
            return;

        if (Input.GetKeyDown(KeyCode.L) && !EditingTextField())
        {
            _logicConnections.Toggle(editor);
            return;
        }

        _logicConnections.Update(editor);

        if (Time.unscaledTime >= _nextRestore)
        {
            _nextRestore = Time.unscaledTime + 1f;
            RestoreGeneratedSizes(editor);
            UpdateButtons();
        }

        if (_importing && Input.GetKeyDown(KeyCode.Escape))
        {
            _cancelImport = true;
            UpdateButtons();
            return;
        }

        if (_placementActive)
        {
            UpdatePlacement();
            return;
        }

        if (!_importing && control && Input.GetKeyDown(KeyCode.V) && !EditingTextField())
            PasteFromClipboard();
    }

    private void LateUpdate()
    {
        var editor = LevelEditor.main;
        if (editor && !_placementActive && !_importing)
        {
            _levelTools.Update(editor, EditingTextField());
            _groundPointIds.Update(editor, _duplicatingThisFrame);
            _doorMotion.Update(editor, EditingTextField());
            _history.Update(editor, EditingTextField());
            _lampColorPicker.Update(editor);
            _idList.Update(editor);
            _groundPreview.Update(editor);
            _hotbar.Update(editor, EditingTextField());
            UpdatePluginUiVisibility(editor);
        }
    }

    private void OnDestroy()
    {
        _logicConnections.Dispose();
        _levelTools.Dispose();
        _doorMotion.Dispose();
        _history.Dispose();
        _lampColorPicker.Dispose();
        _idList.Dispose();
        _groundPreview.Dispose();
        _hotbar.Dispose();
    }

    private IEnumerator CreateNativeUiAfterStart(LevelEditor editor)
    {
        yield return null;
        if (editor && editor == LevelEditor.main)
            CreateNativeUi(editor);
    }

    private void CreateNativeUi(LevelEditor editor)
    {
        DestroyNativeUi();
        var canvas = editor.infoText != null ? editor.infoText.canvas : FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Notify("Image Paste: LevelEditor canvas was not found.", true);
            return;
        }

        var template = FindButtonTemplate(canvas, editor);
        if (template == null)
        {
            Notify("Image Paste: a native button template was not found.", true);
            return;
        }

        _placementOverlay =
            new GameObject("Image Placement Input", typeof(RectTransform), typeof(Image), typeof(Button));
        _placementOverlay.transform.SetParent(canvas.transform, false);
        var overlayRect = (RectTransform)_placementOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _placementOverlay.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0f);
        overlayImage.raycastTarget = true;
        var overlayButton = _placementOverlay.GetComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        overlayButton.onClick.AddListener(ConfirmPlacement);
        _placementOverlay.SetActive(false);

        _uiRoot = new GameObject("Gunsaw Image Paste UI", typeof(RectTransform));
        _uiRoot.transform.SetParent(canvas.transform, false);
        var rootRect = (RectTransform)_uiRoot.transform;
        rootRect.anchorMin = Vector2.one;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = Vector2.one;
        rootRect.anchoredPosition = new Vector2(-18f, -18f);
        rootRect.sizeDelta = new Vector2(210f, 200f);

        _pasteButton = CloneNativeButton(template, _uiRoot.transform, "Paste Image", 0f, PasteFromClipboard,
            out _pasteLabel);
        _idsButton = CloneNativeButton(template, _uiRoot.transform, "IDs", -50f, ToggleIdList, out _idsLabel);
        _hotbarButton = CloneNativeButton(template, _uiRoot.transform, "Hotbar", -100f, ToggleHotbar, out _hotbarLabel);
        if (editor.infoText != null)
        {
            _helpLabel = Instantiate(editor.infoText, _uiRoot.transform);
            _helpLabel.name = "Image Placement Help";
            _helpLabel.text = string.Empty;
            _helpLabel.alignment = TextAlignmentOptions.TopRight;
            _helpLabel.enableWordWrapping = true;
            _helpLabel.raycastTarget = false;
            var helpRect = _helpLabel.rectTransform;
            helpRect.anchorMin = Vector2.one;
            helpRect.anchorMax = Vector2.one;
            helpRect.pivot = Vector2.one;
            helpRect.anchoredPosition = new Vector2(0f, -154f);
            helpRect.sizeDelta = new Vector2(520f, 76f);
            _helpLabel.gameObject.SetActive(false);
        }

        _placementOverlay.transform.SetAsLastSibling();
        _uiRoot.transform.SetAsLastSibling();
        UpdateButtons();
        Logger.LogInfo("Native LevelEditor controls created from '" + template.name + "'.");
    }

    private static Button FindButtonTemplate(Canvas canvas, LevelEditor editor)
    {
        var buttons = canvas.GetComponentsInChildren<Button>(true);
        var template = buttons
            .Where(button => button.GetComponentInChildren<TMP_Text>(true) != null)
            .OrderByDescending(button =>
            {
                var rect = button.transform as RectTransform;
                if (rect == null)
                    return 0f;
                var size = rect.rect.size;
                return size.x > size.y * 1.4f ? size.x * size.y + 100000f : size.x * size.y;
            })
            .FirstOrDefault();
        if (template != null)
            return template;
        return editor.spawnButtonPrefab != null ? editor.spawnButtonPrefab.GetComponent<Button>() : null;
    }

    private static Button CloneNativeButton(Button template, Transform parent, string text, float y,
        UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        var clone = Instantiate(template.gameObject, parent, false);
        clone.name = text;
        clone.SetActive(true);
        var rect = clone.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(200f, 43f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        var button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        label = clone.GetComponentInChildren<TMP_Text>(true);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 24f;
        if (label.transform != clone.transform)
        {
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        return button;
    }

    private void PasteFromClipboard()
    {
        if (_importing || _placementActive)
            return;
        try
        {
            var document = ArtCode.Decode(GUIUtility.systemCopyBuffer);
            ResolvePrefabs();
            var missing = document.Objects.Select(item => item.Kind).Distinct()
                .Where(kind => !_prefabs.ContainsKey(kind)).ToArray();
            if (missing.Length > 0)
            {
                Notify(
                    "Editor prefabs were not found for: " +
                    string.Join(", ", missing.Select(kind => kind.ToString()).ToArray()) + ".", true);
                return;
            }

            StartCoroutine(SpawnPreview(document));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Paste failed: " + ex);
            Notify("Clipboard does not contain a valid GSI1 image code.", true);
        }
    }

    private void ToggleIdList()
    {
        if (LevelEditor.main)
            _idList.Toggle(LevelEditor.main);
    }

    private void ToggleHotbar()
    {
        if (!LevelEditor.main)
            return;
        _hotbar.Toggle(LevelEditor.main);
        UpdateButtons();
    }

    private void UpdatePluginUiVisibility(LevelEditor editor)
    {
        if (_uiRoot == null)
            return;
        _uiRoot.SetActive(_placementActive || _importing || editor.objMenu == null ||
                          !editor.objMenu.activeInHierarchy);
    }

    private IEnumerator SpawnPreview(ArtDocument document)
    {
        _importing = true;
        _cancelImport = false;
        UpdateButtons();
        if (_pasteLabel != null)
            _pasteLabel.text = "Creating Image...";

        var editor = LevelEditor.main;
        _placementGroup = new GameObject("Gunsaw Image Preview");
        _placementGroup.transform.position = CursorWorldPosition();
        var created = 0;

        foreach (var item in document.Objects)
        {
            if (_cancelImport)
            {
                if (_placementGroup)
                    Destroy(_placementGroup);
                _placementGroup = null;
                _importing = false;
                _cancelImport = false;
                UpdateButtons();
                Notify("Image import cancelled.", false);
                yield break;
            }

            if (!editor)
            {
                CancelPlacement();
                yield break;
            }

            var prefab = _prefabs[item.Kind];
            var instance = Instantiate(prefab);
            instance.transform.SetParent(_placementGroup.transform, false);
            instance.transform.localPosition = new Vector3(item.X, item.Y, 0f);
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, item.Rotation);
            instance.transform.localScale = Vector3.one;
            var partGame = instance.GetComponent<LevelPartGame>();
            if (partGame != null)
            {
                partGame.part.size = new Vector2(item.Width, item.Height);
                partGame.part.activId = MarkerId;
                if (item.Kind == ArtObjectKind.InfoScreen)
                    partGame.part.team = item.Text ?? string.Empty;
            }

            ApplySize(instance, new Vector2(item.Width, item.Height));
            if (item.Kind == ArtObjectKind.InfoScreen)
                ApplyInfoScreenText(instance, item.Text ?? string.Empty, new Vector2(item.Width, item.Height));
            foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            created++;
            if (created % 150 == 0)
                yield return null;
        }

        _importing = false;
        _cancelImport = false;
        if (created == 0)
        {
            CancelPlacement();
            Notify("The image code contains no objects.", true);
            yield break;
        }

        _placementScale = 1f;
        _placementActive = true;
        _placementOverlay.SetActive(true);
        _placementOverlay.transform.SetAsLastSibling();
        _uiRoot.transform.SetAsLastSibling();
        if (_helpLabel != null)
            _helpLabel.gameObject.SetActive(true);
        UpdatePlacementHelp();
        UpdateButtons();
        editor.UpdateLighting();
        editor.SetInfoText("Move the image, resize it, then click to place.");
    }

    private void UpdatePlacement()
    {
        if (!_placementGroup)
        {
            CancelPlacement();
            return;
        }

        _placementGroup.transform.position = CursorWorldPosition();
        var scaleDelta = Input.mouseScrollDelta.y;
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            scaleDelta += 1f;
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            scaleDelta -= 1f;
        if (Math.Abs(scaleDelta) > 0.001f)
        {
            _placementScale = Mathf.Clamp(_placementScale * Mathf.Pow(1.1f, scaleDelta), 0.05f, 20f);
            _placementGroup.transform.localScale = Vector3.one * _placementScale;
            UpdatePlacementHelp();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            CancelPlacement();
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            ConfirmPlacement();
    }

    private void ConfirmPlacement()
    {
        if (!_placementActive || !_placementGroup)
            return;
        var editor = LevelEditor.main;
        if (!editor)
        {
            CancelPlacement();
            return;
        }

        var placed = new List<GameObject>();
        while (_placementGroup.transform.childCount > 0)
        {
            var child = _placementGroup.transform.GetChild(0);
            var instance = child.gameObject;
            var worldPosition = child.position;
            var worldRotation = child.rotation;
            var part = instance.GetComponent<LevelPartGame>();
            var requested = part != null && part.part != null
                ? part.part.size * _placementScale
                : Vector2.one * _placementScale;

            child.SetParent(editor.transform, true);
            child.position = worldPosition;
            child.rotation = worldRotation;
            child.localScale = Vector3.one;
            if (part != null && part.part != null)
                part.part.size = requested;
            ApplySize(instance, requested);
            if (part != null && Normalize(part.part.path).EndsWith("/infoscreen"))
                ApplyInfoScreenText(instance, part.part.team ?? string.Empty, requested);
            foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = true;
            placed.Add(instance);
        }

        Destroy(_placementGroup);
        _placementGroup = null;
        _placementActive = false;
        _placementOverlay.SetActive(false);
        if (_helpLabel != null)
            _helpLabel.gameObject.SetActive(false);
        UpdateButtons();
        editor.UpdateLighting();
        editor.SetInfoText("Image placed: " + placed.Count + " objects. Ctrl+Z removes the whole image.");
        Logger.LogInfo("Placed " + placed.Count + " image objects at scale " + _placementScale.ToString("0.00") + ".");
    }

    private void CancelPlacement()
    {
        if (_placementGroup)
            Destroy(_placementGroup);
        _placementGroup = null;
        _placementActive = false;
        _importing = false;
        _cancelImport = false;
        if (_placementOverlay)
            _placementOverlay.SetActive(false);
        if (_helpLabel)
            _helpLabel.gameObject.SetActive(false);
        UpdateButtons();
        if (LevelEditor.main)
            LevelEditor.main.SetInfoText("Image placement cancelled.");
    }

    private void UpdatePlacementHelp()
    {
        if (_helpLabel == null)
            return;
        _helpLabel.text = "Move mouse to position\nMouse wheel or +/-: resize (" +
                          (_placementScale * 100f).ToString("0") + "%)  •  Click/Enter: place  •  Esc: cancel";
    }

    private void UpdateButtons()
    {
        if (_pasteButton != null)
            _pasteButton.interactable = !_importing && !_placementActive;
        if (_idsButton != null)
            _idsButton.interactable = !_importing && !_placementActive;
        if (_hotbarButton != null)
            _hotbarButton.interactable = !_importing && !_placementActive;
        if (_pasteLabel != null)
            _pasteLabel.text = _importing ? "Creating Image..." : "Paste Image";
        if (_hotbarLabel != null)
            _hotbarLabel.text = _hotbar.Visible ? "Hide Hotbar" : "Show Hotbar";
    }

    private static bool EditingTextField()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
            return false;
        var selected = EventSystem.current.currentSelectedGameObject;
        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
    }

    private static Vector3 CursorWorldPosition()
    {
        if (!Camera.main)
            return Vector3.zero;
        var position = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        position.z = 0f;
        return position;
    }

    private void ResolvePrefabs()
    {
        if (_prefabs.Count == 4)
            return;
        var all = Resources.LoadAll<GameObject>("Editor");
        var names = new List<string>();
        foreach (var prefab in all)
        {
            var part = prefab.GetComponent<LevelPartGame>();
            if (part == null)
                continue;
            names.Add(part.fullName + " [" + part.part.path + "]");
            var normalizedName = Normalize(part.fullName);
            var normalizedPath = Normalize(part.part.path);
            if (!_prefabs.ContainsKey(ArtObjectKind.Tile) &&
                (normalizedName == "tile" || normalizedPath.EndsWith("/tile") || normalizedPath.EndsWith("/whitetile")))
                _prefabs[ArtObjectKind.Tile] = prefab;
            if (!_prefabs.ContainsKey(ArtObjectKind.InfoScreen) &&
                (normalizedName == "infoscreen" || normalizedPath.EndsWith("/infoscreen")))
                _prefabs[ArtObjectKind.InfoScreen] = prefab;
            if (!_prefabs.ContainsKey(ArtObjectKind.Background) &&
                (normalizedName == "background" || normalizedPath.EndsWith("/whitetilebackground")))
                _prefabs[ArtObjectKind.Background] = prefab;
            if (!_prefabs.ContainsKey(ArtObjectKind.Ice) &&
                (normalizedName == "ice" || normalizedPath.EndsWith("/ice")))
                _prefabs[ArtObjectKind.Ice] = prefab;
        }

        if (_prefabs.Count != 4)
            Logger.LogWarning("Could not resolve required prefabs. Available editor objects: " +
                              string.Join(", ", names.ToArray()));
        else
            Logger.LogInfo("Resolved image prefabs: Tile='" +
                           _prefabs[ArtObjectKind.Tile].GetComponent<LevelPartGame>().part.path +
                           "', Background='" +
                           _prefabs[ArtObjectKind.Background].GetComponent<LevelPartGame>().part.path +
                           "', Info Screen='" +
                           _prefabs[ArtObjectKind.InfoScreen].GetComponent<LevelPartGame>().part.path +
                           "', Ice='" + _prefabs[ArtObjectKind.Ice].GetComponent<LevelPartGame>().part.path + "'.");
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void RestoreGeneratedSizes(LevelEditor editor)
    {
        for (var i = 0; i < editor.transform.childCount; i++)
        {
            var child = editor.transform.GetChild(i).gameObject;
            var part = child.GetComponent<LevelPartGame>();
            if (part != null && part.part != null && part.part.activId == MarkerId && part.part.size.x > 0f &&
                part.part.size.y > 0f)
            {
                ApplySize(child, part.part.size);
                if (Normalize(part.part.path).EndsWith("/infoscreen"))
                    ApplyInfoScreenText(child, part.part.team ?? string.Empty, part.part.size);
            }
        }
    }

    private static void ApplyInfoScreenText(GameObject instance, string text, Vector2 requested)
    {
        var label = instance.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
            return;
        label.text = text;
        var rect = label.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = requested;
    }

    private static void ApplySize(GameObject instance, Vector2 requested)
    {
        var renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer == null)
            return;
        if (renderer.drawMode == SpriteDrawMode.Tiled || renderer.drawMode == SpriteDrawMode.Sliced)
        {
            renderer.size = requested;
            return;
        }

        var sprite = renderer.sprite;
        if (sprite == null)
            return;
        var natural = sprite.bounds.size;
        if (natural.x > 0.0001f && natural.y > 0.0001f)
            instance.transform.localScale = new Vector3(requested.x / natural.x, requested.y / natural.y, 1f);
    }

    private void Notify(string message, bool error)
    {
        if (LevelEditor.main)
            LevelEditor.main.SetInfoText(message);
        if (error)
            Logger.LogWarning(message);
        else
            Logger.LogInfo(message);
    }

    private void LeaveEditor()
    {
        _logicConnections.Dispose();
        _levelTools.Dispose();
        _doorMotion.Dispose();
        _history.Reset();
        _lampColorPicker.Dispose();
        _idList.Dispose();
        _groundPreview.Dispose();
        if (_placementGroup)
            Destroy(_placementGroup);
        _placementGroup = null;
        _placementActive = false;
        _importing = false;
        _cancelImport = false;
        _prefabs.Clear();
        DestroyNativeUi();
        _lastEditor = null;
    }

    private void DestroyNativeUi()
    {
        if (_uiRoot)
            Destroy(_uiRoot);
        if (_placementOverlay)
            Destroy(_placementOverlay);
        _uiRoot = null;
        _placementOverlay = null;
        _pasteButton = null;
        _idsButton = null;
        _hotbarButton = null;
        _pasteLabel = null;
        _idsLabel = null;
        _hotbarLabel = null;
        _helpLabel = null;
    }
}