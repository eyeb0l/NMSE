using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Dialogs;

internal static class SeedSearchWindows
{
    private static Window Create(string title, StackPanel content) => new()
    {
        Title = title, Width = 780, Height = 650,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new ScrollViewer { Content = content },
    };

    internal static async Task<uint?> SearchTechnologyAsync(Window owner, GameItem item)
    {
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        var rows = item.StatLevels.DistinctBy(stat => stat.Stat).Select(stat =>
        {
            var mode = new ComboBox { ItemsSource = Enum.GetValues<ProcTechLogic.StatMatchMode>(), SelectedItem = ProcTechLogic.StatMatchMode.Maximise, Width = 130 };
            var target = new NumericUpDown { Value = (decimal)stat.ValueMax, Width = 120 };
            var priority = new CheckBox { Content = "Priority" };
            content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new TextBlock { Text = stat.Name, Width = 270 }, mode, target, priority } });
            return (Stat: stat.Stat, Mode: mode, Target: target, Priority: priority);
        }).ToArray();
        var highGrade = new CheckBox { Content = "High grade" };
        var results = new ListBox { Height = 260 };
        var search = new Button { Content = "Search seeds" };
        var apply = new Button { Content = UiStrings.Get("common.apply"), IsEnabled = false };
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true };
        content.Children.Add(highGrade); content.Children.Add(search); content.Children.Add(results);
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } });
        var window = Create(item.Name + " — Choose Stats", content);
        IReadOnlyList<ProcTechLogic.SeedCandidate> candidates = [];
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        results.SelectionChanged += (_, _) => apply.IsEnabled = results.SelectedIndex >= 0;
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false; apply.IsEnabled = false; candidates = [];
            var criteria = rows.Select(row => new ProcTechLogic.StatCriterion(row.Stat,
                (ProcTechLogic.StatMatchMode)row.Mode.SelectedItem!, (float)(row.Target.Value ?? 0), row.Priority.IsChecked == true)).ToArray();
            bool boosted = highGrade.IsChecked == true;
            try
            {
                candidates = await Task.Run(() => ProcTechLogic.Search(item, criteria, highGrade: boosted));
                if (closed) return;
                results.ItemsSource = candidates.Select(candidate => $"{candidate.Seed:D5}: " +
                    string.Join("; ", candidate.Rolls.Select(roll => ProcTechLogic.FormatRollLine(roll, item.BaseStat, null, null)))).ToArray();
                results.SelectedIndex = candidates.Count > 0 ? 0 : -1;
            }
            catch (Exception ex) { if (!closed) results.ItemsSource = new[] { ex.Message }; }
            finally { search.IsEnabled = true; }
        };
        apply.Click += (_, _) => { if (results.SelectedIndex >= 0 && results.SelectedIndex < candidates.Count) window.Close(candidates[results.SelectedIndex].Seed); };
        cancel.Click += (_, _) => window.Close(null);
        return await window.ShowDialog<uint?>(owner);
    }

    internal static async Task<PetBattleLogic.SolveCandidate?> SearchPetAsync(Window owner,
        double scale, int healthLevel, int speedLevel, int combatLevel)
    {
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        var classes = new List<ComboBox>();
        var targets = new List<NumericUpDown>();
        var exact = new List<CheckBox>();
        foreach (string name in new[] { "Health", "Speed", "Combat" })
        {
            var cls = new ComboBox { ItemsSource = new[] { "C", "B", "A", "S" }, SelectedIndex = 3, Width = 80 };
            var useTarget = new CheckBox { Content = "Exact target" };
            var value = new NumericUpDown { Minimum = 0, Value = 0, Width = 160 };
            classes.Add(cls); targets.Add(value); exact.Add(useTarget);
            content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new TextBlock { Text = name, Width = 90 }, cls, useTarget, value } });
        }
        var adjust = new CheckBox { Content = "Adjust pet size", IsChecked = false };
        var results = new ListBox { Height = 300 };
        var search = new Button { Content = UiStrings.Get("companion.battle_search_seed") };
        var apply = new Button { Content = UiStrings.Get("common.apply"), IsEnabled = false };
        var cancel = new Button { Content = UiStrings.Get("common.cancel"), IsCancel = true };
        content.Children.Add(adjust); content.Children.Add(search); content.Children.Add(results);
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } });
        var window = Create(UiStrings.Get("companion.battle_search_seed"), content);
        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        window.Closed += (_, _) => cancellation.Cancel();
        IReadOnlyList<PetBattleLogic.SolveCandidate> candidates = [];
        results.SelectionChanged += (_, _) => apply.IsEnabled = results.SelectedIndex >= 0;
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false; apply.IsEnabled = false; candidates = [];
            float? Target(int i) => exact[i].IsChecked == true ? (float)(targets[i].Value ?? 0) : null;
            var request = new PetBattleLogic.SolveRequest(new(classes[2].SelectedIndex, classes[1].SelectedIndex, classes[0].SelectedIndex),
                Target(0), Target(1), Target(2), scale, adjust.IsChecked == true, healthLevel, speedLevel, combatLevel);
            try
            {
                candidates = await Task.Run(() => PetBattleLogic.Solve(request, token));
                if (token.IsCancellationRequested) return;
                results.ItemsSource = candidates.Select(candidate => string.Create(CultureInfo.InvariantCulture,
                    $"Health {candidate.Stats.Health}, Speed {candidate.Stats.Speed}, Combat {candidate.Stats.Combat}; Size {candidate.Scale:0.###}; {candidate.SpeciesSeed:X}/{candidate.GenusSeed:X}")).ToArray();
                results.SelectedIndex = candidates.Count > 0 ? 0 : -1;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { results.ItemsSource = new[] { ex.Message }; }
            finally { search.IsEnabled = true; }
        };
        apply.Click += (_, _) => { if (results.SelectedIndex >= 0 && results.SelectedIndex < candidates.Count) window.Close(candidates[results.SelectedIndex]); };
        cancel.Click += (_, _) => window.Close(null);
        return await window.ShowDialog<PetBattleLogic.SolveCandidate?>(owner);
    }
}
