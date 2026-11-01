using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.UI.Avalonia.Panels.Catalogue;

/// <summary>Read-only refining and cooking recipe browser embedded in Catalogue.</summary>
public sealed class RecipeBrowserPanel : UserControl
{
    private readonly RecipeDatabase _recipes;
    private readonly GameItemDatabase _items;
    private readonly ObservableCollection<RecipeBrowserRow> _rows = [];
    private readonly ComboBox _type;
    private readonly TextBox _search;
    private readonly DataGrid _grid;
    private readonly TextBlock _details;
    private readonly Button _web;
    private readonly TextBlock _typeLabel;

    public Func<string, CancellationToken, Task<bool>>? OpenUrlAsync { get; set; }

    public RecipeBrowserPanel(RecipeDatabase recipes, GameItemDatabase items)
    {
        _recipes = recipes;
        _items = items;
        _typeLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _type = new ComboBox { MinWidth = 130, SelectedIndex = 0 };
        _search = new TextBox { Width = 250 };
        _web = new Button();
        var clear = CatalogueUi.Button("×", () => { _search.Text = string.Empty; _type.SelectedIndex = 0; });
        _type.SelectionChanged += (_, _) => Refresh();
        _search.TextChanged += (_, _) => Refresh();
        _web.Click += async (_, _) =>
        {
            if (OpenUrlAsync != null)
                await OpenUrlAsync("https://nomansskyrecipes.com/", CancellationToken.None);
        };
        var toolbar = CatalogueUi.Toolbar(_typeLabel, _type, _search, clear, _web);

        _grid = CatalogueUi.Grid(selection: DataGridSelectionMode.Single);
        _grid.ItemsSource = _rows;
        _grid.Columns.Add(CatalogueUi.TextColumn("", nameof(RecipeBrowserRow.Ingredients), star: true));
        _grid.Columns.Add(CatalogueUi.TextColumn("▶", nameof(RecipeBrowserRow.Arrow), 42));
        _grid.Columns.Add(CatalogueUi.TextColumn("", nameof(RecipeBrowserRow.Result), 260));
        _grid.Columns.Add(CatalogueUi.TextColumn("", nameof(RecipeBrowserRow.Time), 85));
        _grid.Columns.Add(CatalogueUi.TextColumn("", nameof(RecipeBrowserRow.Type), 120));
        _grid.SelectionChanged += (_, _) => ShowSelected();

        _details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4) };
        Content = CatalogueUi.PageGrid(toolbar, _grid, _details);
        ApplyUiLocalisation();
        Refresh();
    }

    public void RefreshLanguage()
    {
        Refresh();
        ShowSelected();
    }

    public void ApplyUiLocalisation()
    {
        _typeLabel.Text = UiStrings.Get("recipe.type_filter");
        int selected = Math.Max(0, _type.SelectedIndex);
        _type.ItemsSource = new[] { UiStrings.Get("common.all"), UiStrings.Get("recipe.type_refining"), UiStrings.Get("recipe.type_cooking") };
        _type.SelectedIndex = Math.Min(selected, 2);
        _search.PlaceholderText = UiStrings.Get("recipe.filter_placeholder");
        _web.Content = UiStrings.Get("recipe.nms_recipes_web");
        _grid.Columns[0].Header = UiStrings.Get("recipe.col_ingredients");
        _grid.Columns[2].Header = UiStrings.Get("recipe.col_result");
        _grid.Columns[3].Header = UiStrings.Get("recipe.col_time");
        _grid.Columns[4].Header = UiStrings.Get("recipe.col_type");
        if (_grid.SelectedItem == null) _details.Text = UiStrings.Get("recipe.details_placeholder");
    }

    private void Refresh()
    {
        int type = Math.Max(0, _type.SelectedIndex);
        string search = (_search.Text ?? string.Empty).Trim();
        Recipe? selected = (_grid.SelectedItem as RecipeBrowserRow)?.Recipe;
        _rows.Clear();
        foreach (Recipe recipe in _recipes.Recipes)
        {
            if (type == 1 && recipe.Cooking) continue;
            if (type == 2 && !recipe.Cooking) continue;
            string ingredients = FormatIngredients(recipe);
            string result = FormatResult(recipe);
            if (search.Length > 0 && !ingredients.Contains(search, StringComparison.CurrentCultureIgnoreCase) &&
                !result.Contains(search, StringComparison.CurrentCultureIgnoreCase) &&
                !recipe.RecipeName.Contains(search, StringComparison.CurrentCultureIgnoreCase)) continue;
            _rows.Add(new RecipeBrowserRow
            {
                Ingredients = ingredients,
                Result = result,
                Time = recipe.TimeToMake,
                Type = recipe.Cooking ? UiStrings.Get("recipe.type_cooking") : UiStrings.Get("recipe.type_refining"),
                Recipe = recipe
            });
        }
        if (selected != null) _grid.SelectedItem = _rows.FirstOrDefault(row => ReferenceEquals(row.Recipe, selected));
    }

    private void ShowSelected()
    {
        if (_grid.SelectedItem is not RecipeBrowserRow row)
        {
            _details.Text = UiStrings.Get("recipe.details_placeholder");
            return;
        }
        Recipe recipe = row.Recipe;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(recipe.RecipeName)) parts.Add(UiStrings.Format("recipe.detail_recipe", recipe.RecipeName));
        parts.Add(UiStrings.Format("recipe.detail_type", row.Type));
        parts.Add(UiStrings.Format("recipe.detail_time", recipe.TimeToMake));
        if (recipe.Ingredients.Length > 0)
            parts.Add(UiStrings.Format("recipe.detail_ingredients", string.Join(", ", recipe.Ingredients.Select(i => $"{i.Amount}x {ItemName(i.Id)} ({i.Id})"))));
        if (recipe.Result != null)
            parts.Add(UiStrings.Format("recipe.detail_result", recipe.Result.Amount, ItemName(recipe.Result.Id), recipe.Result.Id));
        _details.Text = string.Join("  |  ", parts);
    }

    private string FormatIngredients(Recipe recipe) => string.Join(" + ", recipe.Ingredients.Select(i => $"{i.Amount}x {ItemName(i.Id)}"));
    private string FormatResult(Recipe recipe) => recipe.Result == null ? string.Empty : $"{recipe.Result.Amount}x {ItemName(recipe.Result.Id)}";
    private string ItemName(string id) => _items.GetItem(id)?.Name ?? id;
}
