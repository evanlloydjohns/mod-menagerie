using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ModMenagerie.Application;
using ModMenagerie.Domain;

namespace ModMenagerie.Desktop;

public partial class MainWindow : Window
{
    private readonly Tracker tracker;
    private Pack? active;
    private ProjectRow[] rows = [];
    private bool ready;
    private CancellationTokenSource? refresh;
    private string sort = "Name";
    private ListSortDirection direction;
    private ReferenceData references = new([], ["fabric", "forge", "neoforge", "quilt"]);
    public MainWindow(Tracker tracker)
    {
        this.tracker = tracker;
        Theme.Apply(tracker.Store.Preference("theme") != "light");
        Theme.SetAccent(tracker.Store.Preference("accent"));
        InitializeComponent();
        AccentPicker.ItemsSource = Theme.Accents;
        AccentPicker.SelectedItem = Theme.Accent;
        ThemeButton.Content = Theme.IsDark ? "Light mode" : "Dark mode";
        StatusFilter.ItemsSource = new[] { "All statuses", "Compatible", "Blockers", "Stable", "Beta", "Alpha", "Not detected / incompatible", "Unknown", "Ignored" };
        StatusFilter.SelectedIndex = 0;
        TypeFilter.ItemsSource = new[] { "All types", "Mod", "Datapack" };
        TypeFilter.SelectedIndex = 0;
        LifecycleFilter.ItemsSource = new[] { "All lifecycle", "archived", "approved", "unlisted", "unknown" };
        LifecycleFilter.SelectedIndex = 0;
        LinkFilter.ItemsSource = new[] { "Any links", "Source", "Issues", "Wiki", "Community" };
        LinkFilter.SelectedIndex = 0;
        var columns = new[] { ("Project", "Name", 170), ("Compatibility", "Status", 205), ("Target release", "Candidate", 145), ("Freshness", "Freshness", 190), ("Type", "Type", 100), ("Updated", "Updated", 145), ("Downloads", "Downloads", 110), ("Target release date", "CandidateDate", 145), ("Authors / team", "Authors", 180), ("Loaders", "Loaders", 120), ("Minecraft versions", "GameVersions", 180), ("License", "License", 110), ("Lifecycle", "Lifecycle", 100), ("Latest overall", "LatestVersion", 140) };
        foreach (var (label, property, width) in columns)
        {
            if (property == "Name")
            {
                var template = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
                    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                      <StackPanel Orientation="Horizontal" ToolTip="{Binding Project.Description}">
                        <Image Source="{Binding Project.Icon}" Width="26" Height="26" Margin="3,0,8,0"/>
                        <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                      </StackPanel>
                    </DataTemplate>
                    """);
                Table.Columns.Add(new DataGridTemplateColumn { Header = label, CellTemplate = template, SortMemberPath = property, Width = width });
                continue;
            }
            var binding = new Binding(property);
            if (property is "Updated" or "CandidateDate")
                binding.StringFormat = "g";
            Table.Columns.Add(new DataGridTextColumn { Header = label, Binding = binding, SortMemberPath = property == "Status" ? "Effective" : property, Width = width, Visibility = Table.Columns.Count < 4 ? Visibility.Visible : Visibility.Collapsed });
        }
        var style = new Style(typeof(DataGridRow), (Style)Table.FindResource(typeof(DataGridRow)));
        foreach (var pair in new[] { (Compatibility.Stable, "StableInk"), (Compatibility.Beta, "BetaInk"), (Compatibility.Alpha, "AlphaInk"), (Compatibility.NotDetected, "BlockedInk"), (Compatibility.NotCompatible, "BlockedInk") })
        {
            var trigger = new DataTrigger { Binding = new Binding("Effective"), Value = pair.Item1 };
            trigger.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension(pair.Item2)));
            style.Triggers.Add(trigger);
        }
        Table.RowStyle = style;
        RestorePreferences();
        ready = true;
        LoadPacks(tracker.Store.Preference("selectedPack"));
        Closing += (_, e) => { try { SavePreferences(); refresh?.Cancel(); } catch (Exception ex) { Error(ex); e.Cancel = true; } };
    }
    private void ChangeAccent(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || AccentPicker.SelectedItem is not string accent) return;
        Do(() =>
        {
            tracker.Store.Preference("accent", accent);
            Theme.SetAccent(accent);
        });
    }
    private void ToggleTheme(object sender, RoutedEventArgs e) => Do(() =>
    {
        tracker.Store.Preference("theme", Theme.IsDark ? "light" : "dark");
        Theme.Apply(!Theme.IsDark);
        ThemeButton.Content = Theme.IsDark ? "Light mode" : "Dark mode";
    });
    internal static string FriendlyError(Exception ex) => ex switch
    {
        Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException => "The local data could not be saved or read. Check available disk space, folder permissions, and whether another program has locked the database. See the local log for details.",
        OperationCanceledException => "The request was canceled or timed out. Check your connection and retry.",
        ArgumentException or System.IO.InvalidDataException => ex.Message,
        System.Net.Http.HttpRequestException => "Modrinth could not complete the request. Check your connection, then retry. The service or project may be unavailable.",
        _ => "The operation could not be completed. Retry, or consult the local log for technical details."
    };
    private void Error(Exception ex)
    {
        App.Log(ex);
        MessageBox.Show(this, "The operation could not be completed or saved. Previously saved data is retained.\n\n" + FriendlyError(ex), "The Mod Menagerie", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Do(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) { Error(ex); }
    }
    private void LoadPacks(string? id)
    {
        var packs = tracker.Store.Packs();
        Packs.ItemsSource = packs;
        Packs.SelectedItem = packs.FirstOrDefault(p => p.Id == id) ?? packs.FirstOrDefault();
        if (Packs.SelectedItem == null)
        {
            active = null;
            Reload();
        }
    }
    private void SelectPack(object sender, SelectionChangedEventArgs e)
    {
        if (!ready)
            return;
        Do(() => { active = Packs.SelectedItem as Pack; if (active != null) tracker.Store.Preference("selectedPack", active.Id); Reload(); });
    }
    private void Reload()
    {
        Heading.Text = active?.Name ?? "Welcome to The Mod Menagerie";
        Context.Text = active == null ? "Create a modpack to plan your next upgrade." : $"Current: {active.Current}   →   Target: {active.Target}    •    {active.Loader}" + (active.Current == active.Target ? "  (same version)" : "");
        rows = active == null ? [] : tracker.Rows(active);
        var r = Readiness.Calculate(rows);
        Summary.Text = r.Percent == null ? "No included projects" : $"{r.Compatible} / {r.Included} compatible  —  {r.Percent:0}% ready";
        ReadinessBar.Value = r.Percent ?? 0;
        Counts.Text = $"Tracked {r.Tracked}   •   Stable {r.Stable}   •   Beta {r.Beta}   •   Alpha {r.Alpha}   •   Not detected / incompatible {r.NotDetected}   •   Unknown {r.Unknown}\nBlockers {r.Blockers}   •   Ignored {r.Ignored}   •   Manual decisions {r.Manual}";
        ApplyFilters();
    }
    private void ApplyFilters()
    {
        if (!ready)
            return;
        var selected = (Table.SelectedItem as ProjectRow)?.Project.Id;
        static bool Has(string value, string query) => value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        var filtered = rows.Where(r => Has(r.SearchText, Search.Text) && Has(r.Loaders, LoaderFilter.Text) && Has(string.Join(" ", r.Project.Categories), CategoryFilter.Text) && Has(r.License, LicenseFilter.Text)
            && (ManualFilter.IsChecked != true || r.Manual) && (TypeFilter.SelectedIndex == 0 || r.Type == (string)TypeFilter.SelectedItem)
            && (LifecycleFilter.SelectedIndex == 0 || r.Lifecycle == (string)LifecycleFilter.SelectedItem)
            && (LinkFilter.SelectedIndex == 0 || r.Project.Links.ContainsKey((string)LinkFilter.SelectedItem))
            && (StatusFilter.SelectedIndex switch
            {
                1 => CompatibilityEngine.Positive(r.Effective),
                2 => !CompatibilityEngine.Positive(r.Effective) && r.Effective != Compatibility.Ignored,
                3 => r.Effective == Compatibility.Stable,
                4 => r.Effective == Compatibility.Beta,
                5 => r.Effective == Compatibility.Alpha,
                6 => r.Effective is Compatibility.NotDetected or Compatibility.NotCompatible,
                7 => r.Effective == Compatibility.Unknown,
                8 => r.Effective == Compatibility.Ignored,
                _ => true
            })).ToArray();
        var view = new ListCollectionView(filtered);
        Table.ItemsSource = view;
        view.SortDescriptions.Add(new(sort, direction));
        if (sort != "Name")
            view.SortDescriptions.Add(new("Name", ListSortDirection.Ascending));
        view.SortDescriptions.Add(new("Project.Id", ListSortDirection.Ascending));
        foreach (var column in Table.Columns)
            column.SortDirection = column.SortMemberPath == sort ? direction : null;
        Table.SelectedItem = filtered.FirstOrDefault(r => r.Project.Id == selected);
        Empty.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = active == null ? "Create your first modpack." : rows.Length == 0 ? "Add mods or datapacks to track this upgrade." : "No projects match these filters.";
        Footer.Text = $"{filtered.Length} visible / {rows.Length} tracked. Summary always includes the whole modpack.   •   Cached results; dates shown in local time.";
    }
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (ready)
            ApplyFilters();
    }
    private void ClearFilters(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        LoaderFilter.Clear();
        CategoryFilter.Clear();
        LicenseFilter.Clear();
        StatusFilter.SelectedIndex = TypeFilter.SelectedIndex = LifecycleFilter.SelectedIndex = LinkFilter.SelectedIndex = 0;
        ManualFilter.IsChecked = false;
    }
    private void SortTable(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        direction = sort == e.Column.SortMemberPath && direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        sort = e.Column.SortMemberPath;
        foreach (var c in Table.Columns)
            c.SortDirection = null;
        e.Column.SortDirection = direction;
        ApplyFilters();
    }
    private void NewPack(object sender, RoutedEventArgs e) => Edit(null);
    private void EditPack(object sender, RoutedEventArgs e)
    {
        if (active != null)
            Edit(active);
    }
    private void Edit(Pack? pack)
    {
        var dialog = new PackDialog(pack, references, tracker) { Owner = this };
        if (dialog.ShowDialog() == true)
            Do(() => { tracker.Store.SavePack(dialog.Result!); references = dialog.References; LoadPacks(dialog.Result!.Id); });
    }
    private void DeletePack(object sender, RoutedEventArgs e)
    {
        if (active != null && MessageBox.Show(this, $"Delete ‘{active.Name}’ and its local memberships and decisions? Other modpacks remain intact.", "Delete modpack", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Do(() => { tracker.Store.DeletePack(active.Id); LoadPacks(null); });
    }
    private void AddProject(object sender, RoutedEventArgs e)
    {
        if (active == null)
            return;
        new AddDialog(tracker, active) { Owner = this }.ShowDialog();
        Do(Reload);
    }
    private void RemoveProject(object sender, RoutedEventArgs e)
    {
        if (Table.SelectedItem is ProjectRow r && MessageBox.Show(this, $"Remove {r.Name} from this modpack and remove its scoped decisions?", "Remove project", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Do(() => { tracker.Store.Remove(r.Membership); Reload(); });
    }
    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (active == null || refresh != null)
            return;
        var pack = active;
        refresh = new();
        RefreshButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        try
        {
            ProgressText.Text = $"Checking {pack.Name}…";
            var progress = new Progress<RefreshProgress>(p => ProgressText.Text = $"{pack.Name}: {p.Completed}/{p.Total} checked · {p.Succeeded} succeeded · {p.Failed} failed · {p.Project}");
            // Keep JSON mapping, evaluation and database writes off the dispatcher too.
            var token = refresh.Token;
            var result = await Task.Run(() => tracker.RefreshAsync(pack, progress, token));
            Reload();
            ProgressText.Text = $"{pack.Name}: {result.Succeeded} succeeded, {result.Failed} failed, {result.Unprocessed} canceled/unprocessed. Saved results are cached; Refresh retries failures.";
        }
        catch (Exception ex) { ProgressText.Text = "Refresh stopped. A save or retrieval failed; completed saved results are retained."; Error(ex); }
        finally { refresh.Dispose(); refresh = null; RefreshButton.IsEnabled = true; CancelButton.IsEnabled = false; }
    }
    private void CancelRefresh(object sender, RoutedEventArgs e) => refresh?.Cancel();
    internal static TextBlock Text(string text, int size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    internal static void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private void Link(string label, string url)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = url };
        button.Click += (_, _) => Do(() => OpenLink(url));
        Details.Children.Add(button);
    }
    private void SelectProject(object sender, SelectionChangedEventArgs e)
    {
        Details.Children.Clear();
        if (Table.SelectedItem is not ProjectRow r || active == null)
        {
            Details.Children.Add(Text("Select a project", 20));
            return;
        }
        var p = r.Project;
        if (p.Icon != null)
        {
            var image = new Image { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            try
            {
                image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(p.Icon));
            }
            catch (Exception ex) { App.Log(ex); }
            Details.Children.Add(image);
        }
        Details.Children.Add(Text(p.Name, 22));
        Details.Children.Add(Text(r.Status, 17));
        Details.Children.Add(Text(p.Description));
        Details.Children.Add(Text($"{r.Type} · Lifecycle: {p.Lifecycle}\n{p.Authors}\nID: {p.Id} · Slug: {p.Slug}"));
        Link("Open Modrinth project", p.Url);
        var copy = new Button { Content = "Copy project URL", Margin = new Thickness(0, 0, 0, 12) };
        copy.Click += (_, _) => Do(() => Clipboard.SetText(p.Url));
        Details.Children.Add(copy);
        Details.Children.Add(Text("Current → target evidence", 18));
        var comparison = new Grid();
        comparison.ColumnDefinitions.Add(new());
        comparison.ColumnDefinitions.Add(new());
        var current = Text($"CURRENT · {active.Current}\n{CompatibilityEngine.Label(r.Current.Status)}\n{r.Current.Candidate?.Number ?? "No detected release"}\n{(r.Current.Candidate == null ? "" : r.Current.Candidate.Published.ToLocalTime().ToString("g"))}", 13);
        var target = Text($"TARGET · {active.Target}\n{CompatibilityEngine.Label(r.Evaluation?.Automatic.Status ?? Compatibility.Unknown)}\n{r.Candidate}\n{r.CandidateDate?.ToLocalTime():g}", 13);
        Grid.SetColumn(target, 1);
        comparison.Children.Add(current);
        comparison.Children.Add(target);
        Details.Children.Add(comparison);
        Details.Children.Add(Text($"Automatic: {r.Evaluation?.Automatic.Reason ?? "Not checked"}\nScope: {active.Target} / {active.Loader} / {r.Type}\nLast attempt: {r.Evaluation?.Attempted.ToLocalTime():g}\nLast success: {r.Evaluation?.LastSuccess?.Time.ToLocalTime():g}\nMetadata cached: {p.Retrieved?.ToLocalTime():g}"));
        if (r.Evaluation?.Error != null)
            Details.Children.Add(Text($"Check failed: {r.Evaluation.Error}\nLast known: {CompatibilityEngine.Label(r.Evaluation.LastSuccess?.Status ?? Compatibility.Unknown)} · {r.Evaluation.LastSuccess?.Time.ToLocalTime():g}\nLast-known release: {r.Evaluation.LastSuccess?.Candidate?.Number ?? "None"}"));
        if (r.Evaluation?.Automatic.Candidate is { } candidate)
            Link("Open selected target release", $"https://modrinth.com/mod/{p.Slug}/version/{candidate.Id}");
        Details.Children.Add(Text("Manual decision", 18));
        if (r.Decision != null)
        {
            Details.Children.Add(Text($"Manual · {r.Decision.Modified.ToLocalTime():g}\n{r.Decision.Note}"));
            if (r.Decision.Reference != null)
                Link("Open evidence reference", r.Decision.Reference);
        }
        var options = new[] { "Use automatic status", "Stable compatible", "Beta compatible", "Alpha compatible", "Not compatible", "Ignore project for this upgrade" };
        Compatibility?[] choices = [null, Compatibility.Stable, Compatibility.Beta, Compatibility.Alpha, Compatibility.NotCompatible, Compatibility.Ignored];
        var choice = new ComboBox { ItemsSource = options, SelectedIndex = Array.IndexOf(choices, r.Decision?.Status), Margin = new Thickness(0, 0, 0, 8) };
        Details.Children.Add(choice);
        Details.Children.Add(Text("Note (optional)", 12));
        var note = new TextBox { Text = r.Decision?.Note ?? "", AcceptsReturn = true, Height = 65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        Details.Children.Add(note);
        Details.Children.Add(Text("Evidence URL (optional)", 12));
        var reference = new TextBox { Text = r.Decision?.Reference ?? "", Margin = new Thickness(0, 0, 0, 8) };
        Details.Children.Add(reference);
        var pack = active;
        var save = new Button { Content = "Save decision", Margin = new Thickness(0, 0, 0, 18) };
        save.Click += (_, _) => Do(() => { tracker.SetDecision(pack, r.Membership, choices[choice.SelectedIndex], note.Text, reference.Text); Reload(); });
        Details.Children.Add(save);
        Details.Children.Add(Text($"Latest overall: {r.Latest?.Number ?? "Unavailable"}\nChannel: {r.Latest?.Channel ?? "Unavailable"} · {r.Latest?.Published.ToLocalTime():g}\nUpdated: {p.Updated?.ToLocalTime():g}\nDownloads: {p.Downloads:N0}\nLicense: {p.License}\nCategories: {string.Join(", ", p.Categories)}\nLoaders: {r.Loaders}\nMinecraft versions: {r.GameVersions}"));
        foreach (var link in p.Links)
            Link(link.Key, link.Value);
        Details.Children.Add(Text("Full description (plain text)", 18));
        Details.Children.Add(Text(string.IsNullOrWhiteSpace(p.Body) ? "Unavailable" : p.Body));
    }
    private void Columns(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var col in Table.Columns)
        {
            var item = new MenuItem { Header = col.Header, IsCheckable = true, IsChecked = col.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            item.Click += (_, _) => col.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private sealed record Preferences(string Search, int Status, int Type, bool Manual, string Loader, string Category, string License, int Lifecycle, int Link, string Sort, ListSortDirection Direction, bool[] Visible, double[] Widths);
    private void SavePreferences() => tracker.Store.Preference("table", JsonSerializer.Serialize(new Preferences(Search.Text, StatusFilter.SelectedIndex, TypeFilter.SelectedIndex, ManualFilter.IsChecked == true, LoaderFilter.Text, CategoryFilter.Text, LicenseFilter.Text, LifecycleFilter.SelectedIndex, LinkFilter.SelectedIndex, sort, direction, Table.Columns.Select(c => c.Visibility == Visibility.Visible).ToArray(), Table.Columns.Select(c => c.ActualWidth).ToArray())));
    private void RestorePreferences()
    {
        try
        {
            if (tracker.Store.Preference("references") is string refs)
                references = JsonSerializer.Deserialize<ReferenceData>(refs) ?? references;
            if (tracker.Store.Preference("table") is not string json || JsonSerializer.Deserialize<Preferences>(json) is not { } p)
                return;
            Search.Text = p.Search;
            StatusFilter.SelectedIndex = p.Status;
            TypeFilter.SelectedIndex = p.Type;
            ManualFilter.IsChecked = p.Manual;
            LoaderFilter.Text = p.Loader;
            CategoryFilter.Text = p.Category;
            LicenseFilter.Text = p.License;
            LifecycleFilter.SelectedIndex = p.Lifecycle;
            LinkFilter.SelectedIndex = p.Link;
            sort = p.Sort;
            direction = p.Direction;
            for (var i = 0; i < Math.Min(p.Visible.Length, Table.Columns.Count); i++)
            {
                Table.Columns[i].Visibility = p.Visible[i] ? Visibility.Visible : Visibility.Collapsed;
                if (i < p.Widths.Length && p.Widths[i] > 30)
                    Table.Columns[i].Width = p.Widths[i];
            }
        }
        catch (JsonException ex) { App.Log(ex); }
    }
}
