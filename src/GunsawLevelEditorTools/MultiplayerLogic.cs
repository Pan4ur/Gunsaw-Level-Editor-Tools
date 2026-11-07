using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GunsawLevelEditorTools;

internal static class MultiplayerLogic
{
    private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> Members =
        new Dictionary<Type, Dictionary<string, MemberInfo>>();

    internal static bool TryGetData(MonoBehaviour marker, out object data)
    {
        data = null;
        if (marker == null || marker.GetType().Name != "CustomPropMarker")
            return false;
        var instance = Read(marker, "Instance");
        if (instance == null)
            return false;
        data = Read(instance, "Data");
        return data != null;
    }

    internal static void VisitSignals(object data, Action<string, int, bool> visit)
    {
        foreach (var name in new[] { "activationId", "input", "inputA", "inputB", "d", "clock", "j", "k", "set", "reset", "t" })
        {
            var value = Read(data, name);
            if (value is int id && id >= 0 && !(IsType(data, "NoPvpZoneData") && id == 0))
                visit(name, id, false);
        }
        foreach (var name in new[] { "output", "q", "notQ", "outAdd", "outRemove", "lampId" })
        {
            var value = Read(data, name);
            if (value is int id && id >= 0)
                visit(name, id, true);
        }
        foreach (var value in (ReadString(data, "outputIds") ?? string.Empty).Split(
                     new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(value, out var id) && id >= 0)
                visit("outputIds", id, true);
    }

    internal static MonoBehaviour Controller(LevelEditor editor)
    {
        return editor.GetComponent("CustomPropEditorController") as MonoBehaviour;
    }

    internal static object Placement(MonoBehaviour controller)
    {
        return Read(controller, "placingDefinition");
    }

    internal static void BeginPlacement(MonoBehaviour controller, object definition)
    {
        controller.GetType().GetMethod("BeginPlacement", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new[] { definition });
    }

    internal static void EndPlacement(MonoBehaviour controller)
    {
        if (controller != null)
            controller.GetType().GetMethod("EndPlacement", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
    }

    internal static void Place(MonoBehaviour controller, object definition, Vector2 position)
    {
        EndPlacement(controller);
        BeginPlacement(controller, definition);
        if (Placement(controller) != definition)
            return;
        var type = controller.GetType();
        var instanceType = type.Assembly.GetType("CustomPropInstance");
        var instance = Activator.CreateInstance(instanceType, true);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var contract = type.Assembly.GetType("ICustomPropDefinition");
        instanceType.GetField("Uid", flags).SetValue(instance, Guid.NewGuid().ToString("N"));
        instanceType.GetField("TypeId", flags).SetValue(instance, contract.GetProperty("TypeId").GetValue(definition, null));
        instanceType.GetField("Data", flags).SetValue(instance, contract.GetMethod("CreateDefaultData").Invoke(definition, null));
        instanceType.GetField("Position", flags).SetValue(instance, position);
        type.GetMethod("CreateMarker", flags).Invoke(controller, new[] { instance });
    }

    internal static Sprite Icon(object definition)
    {
        return Read(definition, "Icon") as Sprite;
    }

    internal static bool IsType(object data, string name)
    {
        return data != null && data.GetType().Name == name;
    }

    internal static string ReadString(object source, string name)
    {
        return Read(source, name) as string;
    }

    private static object Read(object source, string name)
    {
        if (source == null)
            return null;
        var type = source.GetType();
        Dictionary<string, MemberInfo> typeMembers;
        if (!Members.TryGetValue(type, out typeMembers))
        {
            typeMembers = new Dictionary<string, MemberInfo>();
            Members.Add(type, typeMembers);
        }

        MemberInfo member;
        if (!typeMembers.TryGetValue(name, out member))
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            member = type.GetField(name, flags);
            if (member == null)
                member = type.GetProperty(name, flags);
            typeMembers.Add(name, member);
        }

        var field = member as FieldInfo;
        if (field != null)
            return field.GetValue(source);
        var property = member as PropertyInfo;
        return property == null ? null : property.GetValue(source, null);
    }
}