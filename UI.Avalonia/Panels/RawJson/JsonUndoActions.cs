using System.IO.Compression;
using System.Text;
using NMSE.Core;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.RawJson;

internal interface IJsonUndoAction
{
    void Undo();
    void Redo();
}

internal sealed class JsonValueAction(object parent, string key, object? oldValue, object? newValue) : IJsonUndoAction
{
    public void Undo() => SetValue(parent, key, oldValue);
    public void Redo() => SetValue(parent, key, newValue);

    internal static void SetValue(object parent, string key, object? value)
    {
        if (parent is JsonObject obj)
            obj.Set(key, value);
        else if (parent is JsonArray array)
            array.Set(ParseIndex(key), value);
    }

    internal static int ParseIndex(string key) =>
        int.Parse(key.Trim('[', ']'), System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class JsonAddAction(object parent, string key, object? value) : IJsonUndoAction
{
    public void Undo()
    {
        if (parent is JsonObject obj) obj.Remove(key);
        else if (parent is JsonArray array) array.RemoveAt(JsonValueAction.ParseIndex(key));
    }

    public void Redo()
    {
        if (parent is JsonObject obj) obj.Add(key, value);
        else if (parent is JsonArray array) array.Insert(JsonValueAction.ParseIndex(key), value);
    }
}

internal sealed class JsonMemberAction(JsonObject parent, string oldKey, string newKey, object? oldValue, object? newValue) : IJsonUndoAction
{
    public void Undo() { parent.Rename(newKey, oldKey); parent.Set(oldKey, oldValue); }
    public void Redo() { parent.Rename(oldKey, newKey); parent.Set(newKey, newValue); }
}

internal sealed class JsonDeleteAction(object parent, string key, object? value) : IJsonUndoAction
{
    public void Undo()
    {
        if (parent is JsonObject obj) obj.Add(key, value);
        else if (parent is JsonArray array) array.Insert(JsonValueAction.ParseIndex(key), value);
    }

    public void Redo()
    {
        if (parent is JsonObject obj) obj.Remove(key);
        else if (parent is JsonArray array) array.RemoveAt(JsonValueAction.ParseIndex(key));
    }
}

internal sealed class JsonReorderAction(object parent, int fromIndex, int toIndex) : IJsonUndoAction
{
    public void Undo() => Move(parent, toIndex, fromIndex);
    public void Redo() => Move(parent, fromIndex, toIndex);

    private static void Move(object parent, int from, int to)
    {
        if (parent is JsonObject obj)
        {
            obj.Reorder(from, to);
            return;
        }

        if (parent is JsonArray array)
        {
            object? value = array.Get(from);
            array.RemoveAt(from);
            array.Insert(to, value);
        }
    }
}

internal sealed class JsonRootAction(JsonObject target, byte[] oldJson, byte[] newJson) : IJsonUndoAction
{
    public void Undo() => Replace(target, oldJson);
    public void Redo() => Replace(target, newJson);

    internal static byte[] Compress(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    internal static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static string[] ReadLines(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = new List<string>(Math.Max(16, bytes.Length / 8));
        while (reader.ReadLine() is { } line) lines.Add(line);
        return [.. lines];
    }

    internal static void Replace(JsonObject target, byte[] compressedJson) =>
        Replace(target, RawJsonLogic.ParseJson(Decompress(compressedJson)));

    internal static void Replace(JsonObject target, JsonObject source)
    {
        foreach (string name in target.Names()) target.Remove(name);
        foreach (string name in source.Names()) target.Add(name, CloneValue(source.Get(name)));
    }

    private static object? CloneValue(object? value) => value switch
    {
        JsonObject obj => obj.DeepClone(),
        JsonArray array => array.DeepClone(),
        _ => value
    };
}
