using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ModMenagerie.Application;
using ModMenagerie.Domain;

namespace ModMenagerie.Desktop;

public sealed class PackDialog : Window
{
    public Pack? Result
    {
        get; private set;
    }
    public ReferenceData References
    {
        get; private set;
    }
    public PackDialog(Pack? pack, ReferenceData references, Tracker tracker)
    {
        References = references;
        Title = pack == null ? "New modpack" : "Edit modpack";
        Width = 500;
        Height = 510;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(MainWindow.Text("Plan a modpack upgrade", 23));
        panel.Children.Add(MainWindow.Text("Name"));
        var name = new TextBox { Text = pack?.Name ?? "", Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(name);
        ComboBox Field(string label, string value, string[] items)
        {
            panel.Children.Add(MainWindow.Text(label));
            // Prefix completion can silently change exact identifiers, e.g. 1.21.1 to 1.21.11.
            var combo = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, ItemsSource = items, Text = value, Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(combo);
            return combo;
        }
        var current = Field("Current Minecraft version", pack?.Current ?? "", references.GameVersions);
        var target = Field("Target Minecraft version", pack?.Target ?? "", references.GameVersions);
        var loader = Field("Mod loader", pack?.Loader ?? "fabric", references.Loaders);
        panel.Children.Add(MainWindow.Text("Full releases are listed. You may enter an exact Minecraft identifier. Current and target are independent.", 12));
        var status = MainWindow.Text("Cached version choices work offline.", 12);
        panel.Children.Add(status);
        var buttons = new WrapPanel();
        panel.Children.Add(buttons);
        var load = new Button { Content = "Update version choices", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 12, 0) };
        buttons.Children.Add(load);
        var save = new Button { Content = "Save modpack", Padding = new Thickness(10, 7, 10, 7), IsDefault = true };
        buttons.Children.Add(save);
        var cts = new CancellationTokenSource();
        Closed += (_, _) => { cts.Cancel(); cts.Dispose(); };
        load.Click += async (_, _) =>
        {
            load.IsEnabled = false;
            try
            {
                References = await tracker.Provider.ReferencesAsync(cts.Token);
                tracker.Store.Preference("references", JsonSerializer.Serialize(References));
                var c = current.Text;
                var t = target.Text;
                var l = loader.Text;
                current.ItemsSource = target.ItemsSource = References.GameVersions;
                loader.ItemsSource = References.Loaders;
                current.Text = c;
                target.Text = t;
                loader.Text = l;
                status.Text = "Version choices updated from Modrinth.";
            }
            catch (OperationCanceledException) { status.Text = "Request canceled or timed out. Cached choices remain available."; }
            catch (Exception ex) { App.Log(ex); status.Text = "Could not update choices. Use cached choices or type an exact version."; }
            finally { load.IsEnabled = true; }
        };
        save.Click += (_, _) =>
        {
            try
            {
                var edited = pack == null ? Pack.Create(name.Text, current.Text, target.Text, loader.Text) : pack with
                {
                    Name = name.Text.Trim(),
                    Current = current.Text.Trim(),
                    Target = target.Text.Trim(),
                    Loader = loader.Text.Trim().ToLowerInvariant(),
                    Updated = DateTimeOffset.UtcNow
                };
                edited.Validate();
                if (!References.Loaders.Contains(edited.Loader))
                    throw new ArgumentException("Choose a supported mod loader. Update version choices to load additional Modrinth loaders.");
                Result = edited;
                DialogResult = true;
            }
            catch (ArgumentException ex) { status.Text = ex.Message; }
        };
        Loaded += (_, _) => name.Focus();
    }
}

public sealed class AddDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    public AddDialog(Tracker tracker, Pack pack)
    {
        Title = "Add projects — " + pack.Name;
        Width = 880;
        Height = 700;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        top.Children.Add(MainWindow.Text("Find mods and datapacks", 24));
        top.Children.Add(MainWindow.Text("Search is not restricted to target support. Preview a project's identity before adding.", 13));
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(bar);
        var find = new Button { Content = "Search Modrinth", Padding = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(find, Dock.Right);
        bar.Children.Add(find);
        var query = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(query, "Modrinth search query");
        bar.Children.Add(query);
        var urlbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(urlbar);
        var resolve = new Button { Content = "Preview URL", Padding = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(resolve, Dock.Right);
        urlbar.Children.Add(resolve);
        var url = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 0), ToolTip = "https://modrinth.com/mod/name or /datapack/name" };
        System.Windows.Automation.AutomationProperties.SetName(url, "Modrinth project URL");
        urlbar.Children.Add(url);
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var feedback = MainWindow.Text("Enter a search or paste a Modrinth project URL.");
        bottom.Children.Add(feedback);
        var form = new ComboBox { ItemsSource = new[] { "Mod", "Datapack" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) };
        bottom.Children.Add(MainWindow.Text("Track distribution form (explicit version metadata must support it)", 12));
        bottom.Children.Add(form);
        var buttons = new WrapPanel();
        bottom.Children.Add(buttons);
        var add = new Button { Content = "Add previewed project", Padding = new Thickness(12, 8, 12, 8), IsEnabled = false, Margin = new Thickness(0, 0, 12, 0) };
        buttons.Children.Add(add);
        var more = new Button { Content = "Load more results", Padding = new Thickness(12, 8, 12, 8), IsEnabled = false };
        buttons.Children.Add(more);
        var middle = new Grid();
        middle.ColumnDefinitions.Add(new());
        middle.ColumnDefinitions.Add(new());
        root.Children.Add(middle);
        var results = new ListBox { Margin = new Thickness(0, 0, 14, 14), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        results.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Margin="4,8"><Grid.ColumnDefinitions><ColumnDefinition Width="42"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Image Source="{Binding Icon}" Width="32" Height="32" VerticalAlignment="Top"/>
                <StackPanel Grid.Column="1"><TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding Authors}" FontSize="11"/><TextBlock Text="{Binding Type}" FontSize="11"/>
                <TextBlock Text="{Binding Description}" FontSize="12" TextWrapping="Wrap" MaxHeight="48" TextTrimming="CharacterEllipsis"/></StackPanel>
              </Grid>
            </DataTemplate>
            """);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        middle.Children.Add(results);
        var previewPanel = new StackPanel();
        var scroller = new ScrollViewer { Content = previewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroller, 1);
        middle.Children.Add(scroller);
        Project? preview = null;
        var hits = new List<Project>();
        int offset = 0, total = 0, previewSequence = 0;
        string searched = "";
        bool busy = false;
        void Show(Project p)
        {
            previewPanel.Children.Clear();
            if (p.Icon != null)
            {
                try
                {
                    previewPanel.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(p.Icon)), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left });
                }
                catch (Exception ex) { App.Log(ex); }
            }
            previewPanel.Children.Add(MainWindow.Text(p.Name, 21));
            previewPanel.Children.Add(MainWindow.Text($"{p.Authors}\n{p.Type}\nID: {p.Id}\n{p.Description}\n{p.Url}"));
        }
        async Task Search(bool next)
        {
            if (busy)
                return;
            busy = true;
            find.IsEnabled = more.IsEnabled = false;
            try
            {
                if (!next)
                {
                    searched = query.Text;
                    offset = 0;
                    hits.Clear();
                    preview = null;
                    add.IsEnabled = false;
                }
                feedback.Text = "Searching Modrinth…";
                var page = await tracker.Provider.SearchAsync(searched, offset, cancellation.Token);
                hits.AddRange(page.Projects);
                total = page.Total;
                offset += page.Projects.Length;
                results.ItemsSource = hits.ToArray();
                feedback.Text = hits.Count == 0 ? "No results. Try another project name." : $"{hits.Count} of {total} results. Select a result to preview.";
            }
            catch (Exception ex) { App.Log(ex); feedback.Text = "Search could not complete. " + MainWindow.FriendlyError(ex); }
            finally { busy = false; find.IsEnabled = true; more.IsEnabled = offset < total; }
        }
        find.Click += async (_, _) => await Search(false);
        more.Click += async (_, _) => await Search(true);
        query.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Search(false); };
        results.SelectionChanged += async (_, _) =>
        {
            var sequence = ++previewSequence;
            preview = null;
            add.IsEnabled = false;
            if (results.SelectedItem is not Project hit)
                return;
            Show(hit);
            feedback.Text = "Loading full project identity…";
            try
            {
                var full = await tracker.Provider.ProjectAsync(hit.Id, cancellation.Token);
                if (sequence != previewSequence)
                    return;
                preview = full;
                Show(full);
                form.SelectedIndex = full.Loaders.Contains("datapack") && !full.Loaders.Any(l => l != "datapack") ? 1 : 0;
                add.IsEnabled = true;
                feedback.Text = "Review identity and distribution form, then add.";
            }
            catch (Exception ex) { App.Log(ex); if (sequence == previewSequence) feedback.Text = "Preview failed. " + MainWindow.FriendlyError(ex); }
        };
        resolve.Click += async (_, _) =>
        {
            var sequence = ++previewSequence;
            preview = null;
            add.IsEnabled = false;
            resolve.IsEnabled = false;
            try
            {
                var parsed = Tracker.ParseUrl(url.Text);
                feedback.Text = "Resolving project URL…";
                var full = await tracker.Provider.ProjectAsync(parsed.Slug, cancellation.Token);
                if (sequence != previewSequence)
                    return;
                preview = full;
                Show(full);
                form.SelectedIndex = parsed.Form == Distribution.Datapack ? 1 : 0;
                add.IsEnabled = true;
                feedback.Text = "Review identity before adding.";
            }
            catch (Exception ex) { App.Log(ex); feedback.Text = MainWindow.FriendlyError(ex); }
            finally { resolve.IsEnabled = true; }
        };
        add.Click += (_, _) =>
        {
            if (preview == null)
                return;
            try
            {
                var distribution = form.SelectedIndex == 1 ? Distribution.Datapack : Distribution.Mod;
                if (distribution == Distribution.Datapack && !preview.Loaders.Contains("datapack") && preview.Type != "datapack")
                    throw new ArgumentException("This project does not advertise datapack distribution. Choose Mod or another project.");
                var added = tracker.Store.Add(new(pack.Id, preview.Id, distribution), preview);
                feedback.Text = added ? $"Added {preview.Name}. Refresh the modpack to evaluate support." : "This project is already in the modpack; duplicate prevented.";
            }
            catch (Exception ex) { App.Log(ex); feedback.Text = "Not saved. " + MainWindow.FriendlyError(ex); }
        };
        Closed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); };
        Loaded += (_, _) => query.Focus();
    }
}
