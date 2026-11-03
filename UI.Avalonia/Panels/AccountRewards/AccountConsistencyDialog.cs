using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Avalonia.Coordination;

namespace NMSE.UI.Avalonia.Panels.AccountRewards;

internal sealed class AccountConsistencyDialog : Window
{
    private readonly ObservableCollection<AccountLogic.ConsistencyIssue> _issues;
    private readonly JsonObject _saveData;

    internal AccountConsistencyDialog(
        IEnumerable<AccountLogic.ConsistencyIssue> issues,
        JsonObject saveData,
        AvaloniaResourceCatalog resources)
    {
        _issues = new ObservableCollection<AccountLogic.ConsistencyIssue>(issues);
        _saveData = saveData;
        Title = UiStrings.Get("account.consistency_check");
        Width = 940;
        Height = 560;
        MinWidth = 720;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var addAll = new Button { Content = UiStrings.Get("account.consistency_add_all") };
        addAll.Click += (_, _) => ResolveAll(add: true);
        var removeAll = new Button { Content = UiStrings.Get("account.consistency_remove_all") };
        removeAll.Click += (_, _) => ResolveAll(add: false);
        var count = new TextBlock
        {
            Text = UiStrings.Format("account.consistency_issues_found", _issues.Count),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { addAll, removeAll, count },
        };

        var warning = new TextBlock
        {
            Text = UiStrings.Get("account.consistency_warning"),
            Foreground = Brushes.DarkOrange,
            TextWrapping = TextWrapping.Wrap,
        };

        var list = new ListBox
        {
            ItemsSource = _issues,
            ItemTemplate = new FuncDataTemplate<AccountLogic.ConsistencyIssue>((issue, _) =>
                issue is null ? new Panel() : CreateIssueRow(issue, resources)),
        };
        var close = new Button
        {
            Content = UiStrings.Get("common.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 90,
            IsDefault = true,
        };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Children = { toolbar, warning, list, close },
        };
        Grid.SetRow(warning, 1);
        Grid.SetRow(list, 2);
        Grid.SetRow(close, 3);
    }

    private Control CreateIssueRow(AccountLogic.ConsistencyIssue issue, AvaloniaResourceCatalog resources)
    {
        string lookupId = CatalogueLogic.StripCaretPrefix(issue.Id);
        var icon = resources.Icons?.GetIconForItem(lookupId, resources.Items);
        var add = new Button { Content = UiStrings.Format("account.consistency_add_to", Humanize(issue.MissingArray)) };
        add.Click += (_, _) => Resolve(issue, add: true);
        var remove = new Button { Content = UiStrings.Format("account.consistency_remove_from", Humanize(issue.CurrentArray)) };
        remove.Click += (_, _) => Resolve(issue, add: false);
        return new Grid
        {
            Margin = new Thickness(2),
            ColumnDefinitions = new ColumnDefinitions("36,180,140,*,Auto,Auto"),
            ColumnSpacing = 6,
            Children =
            {
                new Image { Source = icon, Width = 28, Height = 28, Stretch = Stretch.Uniform },
                Positioned(new TextBlock { Text = issue.Name, TextWrapping = TextWrapping.Wrap }, 1),
                Positioned(new TextBlock { Text = issue.Id, TextWrapping = TextWrapping.Wrap }, 2),
                Positioned(new TextBlock { Text = issue.Description, TextWrapping = TextWrapping.Wrap,
                    Foreground = issue.IsAmbiguous ? Brushes.DarkOrange : Foreground }, 3),
                Positioned(add, 4),
                Positioned(remove, 5),
            },
        };
    }

    private static T Positioned<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    internal bool HasChanges { get; private set; }

    private void Resolve(AccountLogic.ConsistencyIssue issue, bool add)
    {
        AccountLogic.ResolveConsistencyIssue(_saveData, issue, add);
        HasChanges = true;
        _issues.Remove(issue);
    }

    private void ResolveAll(bool add)
    {
        HasChanges |= _issues.Count > 0;
        foreach (AccountLogic.ConsistencyIssue issue in _issues.ToArray())
            AccountLogic.ResolveConsistencyIssue(_saveData, issue, add);
        _issues.Clear();
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var result = new System.Text.StringBuilder(value.Length + 4).Append(value[0]);
        for (int index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
                result.Append(' ');
            result.Append(value[index]);
        }
        return result.ToString();
    }
}
