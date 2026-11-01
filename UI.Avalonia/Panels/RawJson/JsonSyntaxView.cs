using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace NMSE.UI.Avalonia.Panels.RawJson;

/// <summary>
/// A lightweight, selectable JSON syntax preview. The paired TextBox remains the
/// authoritative editor; this view deliberately caps rendering work for very large saves.
/// </summary>
public sealed class JsonSyntaxView : ScrollViewer
{
    private const int MaxHighlightedCharacters = 2_000_000;
    private readonly TextBlock _textBlock;
    private CancellationTokenSource? _highlightCancellation;

    public static readonly StyledProperty<string> JsonTextProperty =
        AvaloniaProperty.Register<JsonSyntaxView, string>(nameof(JsonText), string.Empty);

    public string JsonText
    {
        get => GetValue(JsonTextProperty);
        set
        {
            SetValue(JsonTextProperty, value);
            QueueHighlight(value);
        }
    }

    public JsonSyntaxView()
    {
        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _textBlock = new TextBlock
        {
            Margin = new Thickness(8),
            FontFamily = new FontFamily("Menlo, Cascadia Mono, monospace"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap
        };
        Content = _textBlock;
    }

    private void QueueHighlight(string text)
    {
        _highlightCancellation?.Cancel();
        _highlightCancellation?.Dispose();
        var cancellation = _highlightCancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                Highlight(text ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
            }
        }, DispatcherPriority.Background);
    }

    private void Highlight(string text)
    {
        bool truncated = text.Length > MaxHighlightedCharacters;
        if (truncated)
            text = text[..MaxHighlightedCharacters];

        _textBlock.Inlines?.Clear();
        foreach (var token in Tokenize(text))
            _textBlock.Inlines?.Add(new Run(token.Text) { Foreground = token.Brush });

        if (truncated)
        {
            _textBlock.Inlines?.Add(new Run(
                "\n\n… Syntax preview truncated; the editable document remains complete. …")
            {
                Foreground = Brushes.Orange
            });
        }
    }

    private static IEnumerable<(string Text, IBrush Brush)> Tokenize(string text)
    {
        var plain = new StringBuilder();

        static bool IsNumberStart(char ch) => char.IsDigit(ch) || ch == '-';
        static bool IsWordPart(char ch) => char.IsLetter(ch);

        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '"')
            {
                if (plain.Length > 0)
                {
                    yield return (plain.ToString(), Brushes.Gray);
                    plain.Clear();
                }

                int start = i++;
                bool escaped = false;
                while (i < text.Length)
                {
                    char current = text[i++];
                    if (current == '"' && !escaped)
                        break;
                    escaped = current == '\\' && !escaped;
                    if (current != '\\') escaped = false;
                }

                int lookahead = i;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead])) lookahead++;
                bool isKey = lookahead < text.Length && text[lookahead] == ':';
                yield return (text[start..i], isKey ? Brushes.DodgerBlue : Brushes.SeaGreen);
                continue;
            }

            if (IsNumberStart(ch))
            {
                if (plain.Length > 0)
                {
                    yield return (plain.ToString(), Brushes.Gray);
                    plain.Clear();
                }
                int start = i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or 'e' or 'E' or '+' or '-')) i++;
                yield return (text[start..i], Brushes.DarkOrange);
                continue;
            }

            if (IsWordPart(ch))
            {
                if (plain.Length > 0)
                {
                    yield return (plain.ToString(), Brushes.Gray);
                    plain.Clear();
                }
                int start = i++;
                while (i < text.Length && IsWordPart(text[i])) i++;
                string word = text[start..i];
                yield return (word, word is "true" or "false" or "null" ? Brushes.MediumPurple : Brushes.Gray);
                continue;
            }

            if (ch is '{' or '}' or '[' or ']' or ':' or ',')
            {
                if (plain.Length > 0)
                {
                    yield return (plain.ToString(), Brushes.Gray);
                    plain.Clear();
                }
                yield return (ch.ToString(), Brushes.SlateGray);
                i++;
                continue;
            }

            plain.Append(ch);
            i++;
        }

        if (plain.Length > 0)
            yield return (plain.ToString(), Brushes.Gray);
    }
}
