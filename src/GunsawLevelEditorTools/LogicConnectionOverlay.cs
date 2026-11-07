using System;
using System.Collections.Generic;
using UnityEngine;

namespace GunsawLevelEditorTools;

internal sealed class LogicConnectionOverlay
{
    private const float LineWidth = 0.055f;
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly List<GameObject> lineObjects = new List<GameObject>();
    private readonly Dictionary<int, List<Endpoint>> outputs = new Dictionary<int, List<Endpoint>>();
    private readonly Dictionary<int, List<Endpoint>> inputs = new Dictionary<int, List<Endpoint>>();
    private readonly List<LevelPartGame> parts = new List<LevelPartGame>();
    private readonly List<MonoBehaviour> components = new List<MonoBehaviour>();
    private readonly List<CustomEndpoint> customEndpoints = new List<CustomEndpoint>();
    private Material material;
    private bool visible;
    private float nextRefresh;

    internal void Toggle(LevelEditor editor)
    {
        visible = !visible;
        if (visible)
        {
            nextRefresh = 0f;
            Refresh(editor);
        }
        else
            ClearLines();

        if (editor != null)
            editor.SetInfoText(visible ? "Logic connections: ON" : "Logic connections: OFF");
    }

    internal void Update(LevelEditor editor)
    {
        if (!visible || editor == null)
            return;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.20f;
            Refresh(editor);
        }
    }

    internal void Dispose()
    {
        visible = false;
        ClearLines();
        if (material != null)
            UnityEngine.Object.Destroy(material);
        material = null;
    }

    private void Refresh(LevelEditor editor)
    {
        ClearMap(outputs);
        ClearMap(inputs);
        parts.Clear();
        editor.GetComponentsInChildren(true, parts);
        foreach (var partGame in parts)
        {
            if (partGame == null || partGame.part == null || IsExcluded(partGame))
                continue;
            var position = partGame.transform.position;
            if (IsGroundPoint(partGame))
                continue;
            if (IsTargetIdOutput(partGame) && partGame.part.activId > 0)
                Add(outputs, partGame.part.activId, position, partGame.gameObject);
            if (IsDirectOutput(partGame) && partGame.part.id > 0)
                Add(outputs, partGame.part.id, position, partGame.gameObject);
            if (partGame.part.id > 0 && IsLogicInput(partGame))
                Add(inputs, partGame.part.id, position, partGame.gameObject);
        }

        RefreshCustomEndpoints(editor);
        foreach (var endpoint in customEndpoints)
            Add(endpoint.Output ? outputs : inputs, endpoint.Id, endpoint.Owner.transform.position, endpoint.Owner);

        var required = 0;
        foreach (var pair in outputs)
        {
            List<Endpoint> targets;
            if (!inputs.TryGetValue(pair.Key, out targets))
                continue;
            foreach (var from in pair.Value)
            foreach (var to in targets)
                if ((from.Position - to.Position).sqrMagnitude > 0.0001f)
                    required++;
        }

        EnsureLineCount(required);
        var selected = GetSelectedObject(editor);
        var index = 0;
        foreach (var pair in outputs)
        {
            List<Endpoint> targets;
            if (!inputs.TryGetValue(pair.Key, out targets))
                continue;
            var color = IdColor(pair.Key);
            foreach (var from in pair.Value)
            {
                foreach (var to in targets)
                {
                    if ((from.Position - to.Position).sqrMagnitude <= 0.0001f)
                        continue;
                    var line = lines[index++];
                    color.a = selected == null || IsSelected(selected, from.Owner) || IsSelected(selected, to.Owner)
                        ? 0.9f
                        : 0.25f;
                    line.startColor = color;
                    line.endColor = color;
                    line.SetPosition(0, new Vector3(from.Position.x, from.Position.y, -0.5f));
                    line.SetPosition(1, new Vector3(to.Position.x, to.Position.y, -0.5f));
                }
            }
        }
    }

    private void RefreshCustomEndpoints(LevelEditor editor)
    {
        customEndpoints.Clear();
        components.Clear();
        editor.GetComponentsInChildren(true, components);
        foreach (var marker in components)
        {
            if (marker == null || marker.GetType().Name != "CustomPropMarker")
                continue;
            ReadCustomProp(marker);
        }
    }

    private void ReadCustomProp(MonoBehaviour marker)
    {
        if (MultiplayerLogic.TryGetData(marker, out var data))
            MultiplayerLogic.VisitSignals(data, (name, id, output) => AddCustom(id, output, marker.gameObject));
    }

    private void AddCustom(int id, bool output, GameObject owner)
    {
        foreach (var endpoint in customEndpoints)
            if (endpoint.Id == id && endpoint.Output == output && endpoint.Owner == owner)
                return;
        customEndpoints.Add(new CustomEndpoint(id, output, owner));
    }

    private static void Add(Dictionary<int, List<Endpoint>> map, int id, Vector3 position, GameObject owner)
    {
        if (id < 0)
            return;
        List<Endpoint> points;
        if (!map.TryGetValue(id, out points))
        {
            points = new List<Endpoint>();
            map.Add(id, points);
        }

        points.Add(new Endpoint(position, owner));
    }

    private static void ClearMap(Dictionary<int, List<Endpoint>> map)
    {
        foreach (var points in map.Values)
            points.Clear();
    }

    private static bool IsExcluded(LevelPartGame partGame)
    {
        var path = partGame.part.path ?? string.Empty;
        return path.Equals("Building/PlayerSpawn", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("Enemies/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsButton(LevelPartGame partGame)
    {
        return string.Equals(partGame.fullName, "Button", StringComparison.OrdinalIgnoreCase) ||
               (partGame.part.path ?? string.Empty).EndsWith("/Button", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGroundPoint(LevelPartGame partGame)
    {
        var path = partGame.part.path;
        return path == "Building/GroundPoint" || path == "Editor/Building/GroundPoint" ||
               partGame.fullName == "Ground Point";
    }

    private static bool IsDoor(LevelPartGame partGame)
    {
        var name = partGame.fullName;
        return string.Equals(name, "Door", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Striped Door", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Secret Door", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Moving Background", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLogicInput(LevelPartGame partGame)
    {
        var path = partGame.part.path;
        var name = partGame.fullName;
        return IsDoor(partGame) || EqualsName(name, "Door Target Changer") || EqualsName(name, "Timed Trigger") ||
               EqualsName(name, "Delayed Trigger") || EqualsName(name, "Fog Setter") ||
               EqualsName(name, "Rain Setter") || EqualsName(name, "Snow Setter") ||
               EqualsName(name, "Weather Changer") || EqualsName(name, "Special Trigger") ||
               EqualsName(name, "Gravity Trigger") || EqualsName(name, "Lamp") ||
               EqualsName(name, "Colored Lamp") ||
               IsPath(path, "DoorPosChanger") || IsPath(path, "TimedTrigger") || IsPath(path, "DelayedTrigger") ||
               IsPath(path, "WeatherChanger") || IsPath(path, "SpecialTrigger") || IsPath(path, "GravityTrigger");
    }

    private static bool IsTargetIdOutput(LevelPartGame partGame)
    {
        var path = partGame.part.path;
        var name = partGame.fullName;
        return EqualsName(name, "Timed Trigger") || EqualsName(name, "Delayed Trigger") ||
               IsPath(path, "TimedTrigger") || IsPath(path, "DelayedTrigger");
    }

    private static bool IsDirectOutput(LevelPartGame partGame)
    {
        return IsButton(partGame) || IsPath(partGame.part.path, "ActivateZone") ||
               EqualsName(partGame.fullName, "Activation Zone");
    }

    private static bool IsPath(string path, string name)
    {
        return path == "Building/Triggers/" + name || path == "Editor/Building/Triggers/" + name;
    }

    private static bool EqualsName(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static GameObject GetSelectedObject(LevelEditor editor)
    {
        return editor.currentlySelected;
    }

    private static bool IsSelected(GameObject selected, GameObject owner)
    {
        return selected != null && owner != null &&
               (selected == owner || selected.transform.IsChildOf(owner.transform) ||
                owner.transform.IsChildOf(selected.transform));
    }

    private void EnsureLineCount(int count)
    {
        while (lines.Count < count)
        {
            var go = new GameObject("Logic Connection Line");
            var line = go.AddComponent<LineRenderer>();
            line.material = GetMaterial();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = LineWidth;
            line.endWidth = LineWidth;
            line.numCapVertices = 3;
            line.sortingOrder = short.MaxValue;
            lines.Add(line);
            lineObjects.Add(go);
        }

        for (var i = 0; i < lines.Count; i++)
            lines[i].gameObject.SetActive(i < count);
    }

    private Material GetMaterial()
    {
        if (material != null)
            return material;
        material = new Material(Shader.Find("Sprites/Default"));
        return material;
    }

    private void ClearLines()
    {
        foreach (var go in lineObjects)
            if (go != null)
                UnityEngine.Object.Destroy(go);

        lines.Clear();
        lineObjects.Clear();
    }

    private static Color IdColor(int id)
    {
        var hue = Mathf.Repeat(id * 0.6180339887f, 1f);
        var color = Color.HSVToRGB(hue, 0.82f, 1f);
        color.a = 0.9f;
        return color;
    }

    private struct Endpoint
    {
        internal readonly Vector3 Position;
        internal readonly GameObject Owner;

        internal Endpoint(Vector3 position, GameObject owner)
        {
            Position = position;
            Owner = owner;
        }
    }

    private struct CustomEndpoint
    {
        internal readonly int Id;
        internal readonly bool Output;
        internal readonly GameObject Owner;

        internal CustomEndpoint(int id, bool output, GameObject owner)
        {
            Id = id;
            Output = output;
            Owner = owner;
        }
    }
}