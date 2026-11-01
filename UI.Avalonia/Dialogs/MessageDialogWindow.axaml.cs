using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NMSE.UI.Avalonia.Dialogs;

internal sealed partial class MessageDialogWindow : Window
{
    public MessageDialogWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MessageDialogWindow(string title, string message, bool showCancel = false) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("CancelButton")!.IsVisible = showCancel;
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
