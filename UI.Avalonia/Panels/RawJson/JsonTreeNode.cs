using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.RawJson;

public sealed class JsonTreeNode : INotifyPropertyChanged
{
    private static readonly JsonTreeNode LoadingNode = new("Loading…", null, null, null, null, true);
    private readonly Func<IEnumerable<JsonTreeNode>>? _childLoader;
    private string _displayText;
    private bool _isExpanded;
    private bool _childrenLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayText
    {
        get => _displayText;
        private set => SetField(ref _displayText, value);
    }

    public object? Value { get; internal set; }
    public object? ParentContainer { get; }
    public JsonTreeNode? ParentNode { get; }
    public string? Key { get; internal set; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<JsonTreeNode> Children { get; } = [];

    public bool IsContainer => Value is JsonObject or JsonArray;
    public bool IsRoot => ParentContainer is null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value)
                EnsureChildrenLoaded();
        }
    }

    private JsonTreeNode(
        string displayText,
        object? value,
        object? parentContainer,
        JsonTreeNode? parentNode,
        string? key,
        bool isPlaceholder,
        Func<IEnumerable<JsonTreeNode>>? childLoader = null)
    {
        _displayText = displayText;
        Value = value;
        ParentContainer = parentContainer;
        ParentNode = parentNode;
        Key = key;
        IsPlaceholder = isPlaceholder;
        _childLoader = childLoader;

        if (childLoader != null)
            Children.Add(LoadingNode);
        else
            _childrenLoaded = true;
    }

    public static JsonTreeNode CreateRoot(JsonObject root)
    {
        var node = new JsonTreeNode("Root", root, null, null, null, false,
            () => CreateChildren(root, null));
        node.IsExpanded = true;
        return node;
    }

    internal static JsonTreeNode Create(
        string key,
        object? value,
        object parentContainer,
        JsonTreeNode? parentNode = null)
    {
        return value switch
        {
            JsonObject obj => new JsonTreeNode(
                ContainerLabel(key, obj), obj, parentContainer, parentNode, key, false,
                () => CreateChildren(obj, null)),
            JsonArray array => new JsonTreeNode(
                ContainerLabel(key, array), array, parentContainer, parentNode, key, false,
                () => CreateChildren(array, null)),
            _ => new JsonTreeNode($"{key} : {FormatValue(value)}", value, parentContainer, parentNode, key, false)
        };
    }

    internal void EnsureChildrenLoaded()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();
        if (_childLoader == null) return;

        foreach (var child in CreateChildren(Value!, this))
            Children.Add(child);
    }

    internal void RefreshLabel()
    {
        string key = Key ?? "Root";
        DisplayText = Value switch
        {
            JsonObject obj when IsRoot => "Root",
            JsonObject obj => ContainerLabel(key, obj),
            JsonArray array => ContainerLabel(key, array),
            _ => $"{key} : {FormatValue(Value)}"
        };
    }

    internal void ReindexArrayChildren()
    {
        if (Value is not JsonArray) return;
        EnsureChildrenLoaded();
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Key = $"[{i}]";
            Children[i].RefreshLabel();
        }
        RefreshLabel();
    }

    internal static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{Escape(text)}\"",
        bool boolean => boolean ? "true" : "false",
        BinaryData data => $"<binary:{data.ToHexString()}>",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
        _ => value.ToString() ?? "null"
    };

    private static IEnumerable<JsonTreeNode> CreateChildren(object container, JsonTreeNode? parentNode)
    {
        if (container is JsonObject obj)
        {
            foreach (string key in obj.Names())
                yield return Create(key, obj.Get(key), obj, parentNode);
        }
        else if (container is JsonArray array)
        {
            for (int i = 0; i < array.Length; i++)
                yield return Create($"[{i}]", array.Get(i), array, parentNode);
        }
    }

    private static string ContainerLabel(string key, JsonObject obj) =>
        $"{key}  {{...}}  ({obj.Size()} properties)";

    private static string ContainerLabel(string key, JsonArray array) =>
        $"{key}  [...]  ({array.Length} items)";

    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
