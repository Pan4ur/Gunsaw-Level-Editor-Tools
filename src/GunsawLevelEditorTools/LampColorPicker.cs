using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunsawLevelEditorTools;

internal sealed class LampColorPicker
{
    private GameObject _root;
    private Slider[] _sliders;
    private TMP_Text[] _labels;
    private LevelPartGame _lamp;
    private object _changerData;
    private TMP_InputField _colorField;
    private Color _color;
    private bool _syncing;

    internal void Update(LevelEditor editor)
    {
        var selected = GetSelected(editor);
        var lamp = selected == null ? null : selected.GetComponent<LevelPartGame>();
        _changerData = null;
        if (selected != null)
            MultiplayerLogic.TryGetData(selected.GetComponent("CustomPropMarker") as MonoBehaviour, out _changerData);
        _colorField = MultiplayerLogic.IsType(_changerData, "LampColorChangerData") ? editor.teamField : null;
        if (lamp == null || (_colorField == null && !string.Equals(lamp.fullName, "Colored Lamp", StringComparison.OrdinalIgnoreCase)))
        {
            if (_root)
                _root.SetActive(false);
            _lamp = null;
            return;
        }

        EnsureUi(editor);
        _root.SetActive(true);
        PositionUi(editor);
        var color = ReadColor(lamp);
        if (_lamp != lamp || _color != color)
        {
            _lamp = lamp;
            SetColor(color);
        }
    }

    private void EnsureUi(LevelEditor editor)
    {
        if (_root)
            return;
        var canvas = editor.infoText.canvas;
        _root = new GameObject("Gunsaw Lamp Color Picker", typeof(RectTransform));
        _root.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)_root.transform;
        rect.anchorMin = new Vector2(.5f, .5f);
        rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(390f, 225f);
        PositionUi(editor);
        if (editor.spawnButtonPrefab != null)
        {
            var background = UnityEngine.Object.Instantiate(editor.spawnButtonPrefab, _root.transform);
            background.name = "Background";
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var button = background.GetComponent<Button>();
            if (button != null)
                button.enabled = false;
            var spawnIcon = background.GetComponent<SpawnIcon>();
            if (spawnIcon != null)
                spawnIcon.enabled = false;
            foreach (var text in background.GetComponentsInChildren<TMP_Text>(true))
                text.enabled = false;
            foreach (var image in background.GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = false;
                if (image.transform != background.transform && image.sprite != null && image.sprite.name != "uioutline")
                    image.enabled = false;
            }

            background.transform.SetAsFirstSibling();
        }

        _sliders = new Slider[6];
        _labels = new TMP_Text[6];
        var names = new[] { "R", "G", "B", "H", "S", "L" };
        var colors = new[]
        {
            new Color(.95f, .2f, .2f), new Color(.2f, .9f, .3f), new Color(.25f, .45f, 1f), new Color(.9f, .25f, .9f),
            new Color(.15f, .8f, .9f), new Color(.95f, .85f, .25f)
        };
        for (var i = 0; i < 6; i++)
        {
            var index = i;
            var label = UnityEngine.Object.Instantiate(editor.infoText, _root.transform);
            label.text = names[i];
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 20f;
            label.raycastTarget = false;
            label.color = colors[i];
            label.rectTransform.anchorMin = new Vector2(0f, 1f);
            label.rectTransform.anchorMax = new Vector2(0f, 1f);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(12f, -12f - i * 34f);
            label.rectTransform.sizeDelta = new Vector2(125f, 26f);
            _labels[i] = label;
            var slider = UnityEngine.Object.Instantiate(editor.RSlid.gameObject, _root.transform)
                .GetComponent<Slider>();
            slider.name = "Lamp " + names[i] + " Slider";
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            var sliderRect = slider.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 1f);
            sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(110f, -11f - i * 34f);
            sliderRect.sizeDelta = new Vector2(260f, 26f);
            slider.onValueChanged = new Slider.SliderEvent();
            slider.onValueChanged.AddListener(value => Changed(index));
            foreach (var image in slider.GetComponentsInChildren<Image>(true))
                image.color = colors[i];
            _sliders[i] = slider;
        }

        _root.transform.SetAsLastSibling();
    }

    private void PositionUi(LevelEditor editor)
    {
        var rect = (RectTransform)_root.transform;
        var canvas = editor.infoText.canvas;
        var canvasRect = (RectTransform)canvas.transform;
        Canvas.ForceUpdateCanvases();
        var panel = editor.objMenu == null ? null : editor.objMenu.GetComponent<RectTransform>();
        Vector2 position;
        if (panel == null)
            position = new Vector2(-18f, -18f);
        else
        {
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            var screen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out position);
            position += new Vector2(-18f, 0f);
        }

        var bounds = canvasRect.rect;
        position.x = Mathf.Clamp(position.x, bounds.xMin + rect.rect.width, bounds.xMax);
        position.y = Mathf.Clamp(position.y, bounds.yMin + rect.rect.height, bounds.yMax);
        rect.anchoredPosition = position;
    }

    private void Changed(int index)
    {
        if (_syncing || _lamp == null)
            return;
        Color color;
        if (index < 3)
            color = new Color(_sliders[0].value, _sliders[1].value, _sliders[2].value);
        else
            color = HslToRgb(_sliders[3].value, _sliders[4].value, _sliders[5].value);
        if (_colorField != null)
        {
            _colorField.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(color));
            _colorField.onEndEdit.Invoke(_colorField.text);
            SetColor(ReadColor(_lamp));
            return;
        }
        _lamp.part.team = "#" + ColorUtility.ToHtmlStringRGB(color);
        LevelLoader.UpdateLampColor(_lamp.gameObject, _lamp.part.force.x, color);
        SetColor(color);
    }

    private void SetColor(Color color)
    {
        if (_sliders == null)
            return;
        _syncing = true;
        _color = color;
        var hsl = RgbToHsl(color);
        var values = new[] { color.r, color.g, color.b, hsl.x, hsl.y, hsl.z };
        for (var i = 0; i < 6; i++)
        {
            _sliders[i].SetValueWithoutNotify(values[i]);
            _labels[i].text = LabelText(i, values[i]);
        }

        foreach (var image in _sliders[3].GetComponentsInChildren<Image>(true))
            image.color = Color.HSVToRGB(hsl.x, 1f, 1f);

        _syncing = false;
    }

    private Color ReadColor(LevelPartGame lamp)
    {
        Color color;
        var value = _colorField == null ? lamp.part.team : MultiplayerLogic.ReadString(_changerData, "color");
        return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.white;
    }

    private static string LabelText(int index, float value)
    {
        if (index < 3)
            return new[] { "R", "G", "B" }[index] + " " + Mathf.RoundToInt(value * 255f);
        if (index == 3)
            return "H " + Mathf.RoundToInt(value * 360f) + "°";
        return (index == 4 ? "S " : "L ") + Mathf.RoundToInt(value * 100f) + "%";
    }

    private static GameObject GetSelected(LevelEditor editor)
    {
        return editor.currentlySelected;
    }

    private static Vector3 RgbToHsl(Color color)
    {
        var max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        var min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        var light = (max + min) * .5f;
        if (Mathf.Approximately(max, min))
            return new Vector3(0f, 0f, light);
        var delta = max - min;
        var saturation = light > .5f ? delta / (2f - max - min) : delta / (max + min);
        var hue = max == color.r ? (color.g - color.b) / delta + (color.g < color.b ? 6f : 0f) :
            max == color.g ? (color.b - color.r) / delta + 2f : (color.r - color.g) / delta + 4f;
        return new Vector3(hue / 6f, saturation, light);
    }

    private static Color HslToRgb(float hue, float saturation, float light)
    {
        if (saturation <= 0f)
            return new Color(light, light, light);
        var q = light < .5f ? light * (1f + saturation) : light + saturation - light * saturation;
        var p = 2f * light - q;
        return new Color(Hue(p, q, hue + 1f / 3f), Hue(p, q, hue), Hue(p, q, hue - 1f / 3f));
    }

    private static float Hue(float p, float q, float value)
    {
        if (value < 0f)
            value += 1f;
        if (value > 1f)
            value -= 1f;
        if (value < 1f / 6f)
            return p + (q - p) * 6f * value;
        if (value < .5f)
            return q;
        return value < 2f / 3f ? p + (q - p) * (2f / 3f - value) * 6f : p;
    }

    internal void Dispose()
    {
        if (_root)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _sliders = null;
        _labels = null;
        _lamp = null;
        _changerData = null;
        _colorField = null;
    }
}