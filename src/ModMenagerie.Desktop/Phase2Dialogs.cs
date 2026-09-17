using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ModMenagerie.Application;
using ModMenagerie.Domain;
using ModMenagerie.Infrastructure;

namespace ModMenagerie.Desktop;

internal static class DialogUi
{
    public static Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 5, 8, 8) };
    public static TextBox Entry(StackPanel panel, string label, string value = "")
    {
        panel.Children.Add(MainWindow.Text(label, 12));
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(7) };
        panel.Children.Add(box);
        return box;
    }
    public static DataGrid Table(params (string Label, string Property)[] columns)
    {
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, MinHeight = 160, Margin = new Thickness(0, 8, 0, 8) };
        table.SetResourceReference(Control.BackgroundProperty, "Surface");
        table.SetResourceReference(DataGrid.RowBackgroundProperty, "Surface");
        foreach (var (label, property) in columns)
        {
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), ElementStyle = textStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 90 });
        }
        return table;
    }
    public static void Error(TextBlock status, Exception ex)
    {
        App.Log(ex);
        status.Text = MainWindow.FriendlyError(ex);
    }
}

public partial class MainWindow
{
    private void ImportPack(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportDialog(tracker) { Owner = this };
        dialog.ShowDialog();
        LoadPacks(dialog.ImportedPackId ?? active?.Id);
    }
    private void Dependencies(object sender, RoutedEventArgs e)
    {
        if (active == null)
            return;
        new DependencyDialog(tracker, active) { Owner = this }.ShowDialog();
        Reload();
    }
    private void History(object sender, RoutedEventArgs e)
    {
        if (active != null)
            new HistoryDialog(tracker, active) { Owner = this }.ShowDialog();
    }
}

public sealed class ImportDialog : Window
{
    public string? ImportedPackId
    {
        get; private set;
    }
    public ImportDialog(Tracker tracker)
    {
        Title = "Import a Modrinth pack — metadata only";
        Width = 1040;
        Height = 780;
        MinWidth = 800;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        top.Children.Add(MainWindow.Text("Import .mrpack", 24));
        top.Children.Add(MainWindow.Text("Preview identified projects before saving. No mod files are downloaded, installed, or extracted."));
        var choose = DialogUi.Button("Choose .mrpack file…");
        top.Children.Add(choose);
        var info = MainWindow.Text("Choose a local exported Modrinth pack.");
        top.Children.Add(info);
        var destination = new ComboBox { ItemsSource = new object[] { "Create a new collection" }.Concat(tracker.Store.Packs()).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(MainWindow.Text("Destination", 12));
        top.Children.Add(destination);
        var name = DialogUi.Entry(top, "New collection name");
        var target = DialogUi.Entry(top, "New collection TARGET Minecraft version (entered separately)");
        var policy = MainWindow.Text("Existing collection: current version and loader must match the imported pack. Its target, notes, overrides and existing memberships are preserved.", 12);
        top.Children.Add(policy);
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var status = MainWindow.Text("No changes are made until you select Import.");
        bottom.Children.Add(status);
        var buttons = new WrapPanel();
        bottom.Children.Add(buttons);
        var commit = DialogUi.Button("Import checked projects");
        commit.IsEnabled = false;
        buttons.Children.Add(commit);
        var cancel = DialogUi.Button("Cancel identification");
        cancel.IsEnabled = false;
        buttons.Children.Add(cancel);
        var table = DialogUi.Table(("Project", "Name"), ("Type", "Form"), ("Destination", "DestinationStatus"), ("Resolution", "Status"), ("Archive path", "Path"), ("Environment", "Environment"));
        table.IsReadOnly = false;
        foreach (var col in table.Columns)
            col.IsReadOnly = true;
        table.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Include", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        root.Children.Add(table);
        ImportManifest? manifest = null;
        ImportRow[] rows = [];
        CancellationTokenSource? cts = null;
        void MarkDuplicates()
        {
            var existing = destination.SelectedItem is Pack selected ? tracker.Store.Members(selected.Id).Select(m => m.ProjectId).ToHashSet() : [];
            foreach (var row in rows)
                row.DestinationStatus = row.Project == null ? "Unresolved — skipped" : existing.Contains(row.Project.Id) ? "Already tracked — skipped" : "New membership";
            table.Items.Refresh();
        }
        destination.SelectionChanged += (_, _) => { name.IsEnabled = target.IsEnabled = destination.SelectedIndex == 0; MarkDuplicates(); };
        cancel.Click += (_, _) => cts?.Cancel();
        Closed += (_, _) => cts?.Cancel();
        choose.Click += async (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Modrinth modpack (*.mrpack)|*.mrpack", CheckFileExists = true };
            if (picker.ShowDialog(this) != true)
                return;
            choose.IsEnabled = commit.IsEnabled = false;
            cancel.IsEnabled = true;
            rows = [];
            table.ItemsSource = rows;
            cts = new CancellationTokenSource();
            try
            {
                manifest = await Task.Run(() => MrpackReader.Read(picker.FileName));
                name.Text = manifest.Name;
                info.Text = $"Imported CURRENT: {manifest.Current} • {manifest.Loader} {manifest.LoaderVersion}\n" + string.Join("\n", manifest.Warnings);
                var progress = new Progress<string>(message => status.Text = message);
                rows = await new PackImport(tracker.Provider).ResolveAsync(manifest, progress, cts.Token);
                table.ItemsSource = rows;
                MarkDuplicates();
                status.Text = $"{rows.Count(r => r.Project != null)} identified entries; {rows.Count(r => r.Project == null)} unresolved. Duplicates within the archive are unchecked. Existing projects will be skipped; their settings stay intact.";
                commit.IsEnabled = rows.Any(r => r.Project != null);
            }
            catch (OperationCanceledException) { status.Text = "Identification canceled. Nothing imported."; }
            catch (Exception ex) { DialogUi.Error(status, ex); }
            finally { choose.IsEnabled = true; cancel.IsEnabled = false; cts.Dispose(); cts = null; }
        };
        commit.Click += (_, _) =>
        {
            if (manifest == null)
                return;
            try
            {
                table.CommitEdit(DataGridEditingUnit.Cell, true);
                table.CommitEdit(DataGridEditingUnit.Row, true);
                var selected = rows.Where(r => r.Include && r.Project != null).Select(r => (r.Project!, r.Form)).ToArray();
                if (selected.Length == 0)
                    throw new ArgumentException("Check at least one identified project to import.");
                var create = destination.SelectedIndex == 0;
                var pack = create ? Pack.Create(name.Text, manifest.Current, target.Text, manifest.Loader) : (Pack)destination.SelectedItem;
                if (!create && (pack.Current != manifest.Current || pack.Loader != manifest.Loader))
                    throw new ArgumentException("Current version or loader differs. Create a new collection, or edit the destination deliberately before importing.");
                var count = tracker.Store.Import(pack, create, selected);
                tracker.Store.Preference("last-import:" + pack.Id, JsonSerializer.Serialize(rows.Select(r => new { r.Path, r.Name, r.Status, r.VersionId, r.Include })));
                tracker.Capture(pack, "Pack imported");
                ImportedPackId = pack.Id;
                commit.IsEnabled = false;
                choose.IsEnabled = false;
                destination.IsEnabled = false;
                status.Text = $"Imported {count} new projects; {selected.Select(p => p.Item1.Id).Distinct().Count() - count} already tracked. Close this window and Refresh to check the target. Unresolved entries were not added; this report is saved locally.";
            }
            catch (Exception ex) { DialogUi.Error(status, ex); }
        };
    }
}

public sealed class DependencyDialog : Window
{
    public DependencyDialog(Tracker tracker, Pack pack)
    {
        Title = "Dependencies — " + pack.Name;
        Width = 1050;
        Height = 720;
        MinWidth = 800;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        top.Children.Add(MainWindow.Text($"Dependency readiness · {pack.Target} / {pack.Loader}", 23));
        top.Children.Add(MainWindow.Text("Checks the selected target releases. Required unresolved, untracked, ignored or conflicting dependencies block this separate measure. Optional/embedded relationships are informational. Cycles remain unknown. This is not a runtime conflict solver."));
        var status = MainWindow.Text("Run analysis after refreshing project compatibility.");
        top.Children.Add(status);
        var buttons = new WrapPanel();
        top.Children.Add(buttons);
        var run = DialogUi.Button("Analyze dependencies");
        var cancel = DialogUi.Button("Cancel");
        var add = DialogUi.Button("Add selected missing project");
        cancel.IsEnabled = false;
        buttons.Children.Add(run);
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        var table = DialogUi.Table(("Dependency tree", "Tree"), ("Relationship", "Relationship"), ("Result", "Result"), ("Root project", "Root"));
        root.Children.Add(table);
        var analysis = new DependencyAnalysis(tracker);
        CancellationTokenSource? cts = null;
        void Show(DependencyReport report)
        {
            table.ItemsSource = report.Lines;
            status.Text = $"{report.Ready}/{report.Included} dependency-ready • {report.Included - report.Ready} blocked/unknown • Checked {report.Time.ToLocalTime():g}. Dated snapshot; project compatibility is unchanged.";
        }
        if (analysis.Cached(pack) is { } saved)
            Show(saved);
        Closed += (_, _) => cts?.Cancel();
        cancel.Click += (_, _) => cts?.Cancel();
        run.Click += async (_, _) =>
        {
            run.IsEnabled = add.IsEnabled = false;
            cancel.IsEnabled = true;
            cts = new();
            try
            {
                var progress = new Progress<string>(message => status.Text = message);
                Show(await Task.Run(() => analysis.AnalyzeAsync(pack, progress, cts.Token)));
            }
            catch (OperationCanceledException) { status.Text = "Canceled. Any prior completed report remains visible as a dated snapshot."; }
            catch (Exception ex) { DialogUi.Error(status, ex); }
            finally { run.IsEnabled = add.IsEnabled = true; cancel.IsEnabled = false; cts.Dispose(); cts = null; }
        };
        add.Click += (_, _) =>
        {
            if (table.SelectedItem is not DependencyLine { Project: { } project } line)
            {
                status.Text = "Select an untracked required dependency row first.";
                return;
            }
            try
            {
                var added = tracker.Store.Add(new(pack.Id, project.Id, line.Form), project);
                tracker.Capture(pack, "Required dependency added");
                status.Text = added ? "Project added. Close this window, refresh the collection, then analyze again. This displayed report is now stale." : "Already tracked. Refresh and analyze again.";
            }
            catch (Exception ex) { DialogUi.Error(status, ex); }
        };
    }
}

public sealed class HistoryDialog : Window
{
    public HistoryDialog(Tracker tracker, Pack pack)
    {
        Title = "Compatibility history — " + pack.Name;
        Width = 1050;
        Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(MainWindow.Text("Compatibility history", 24));
        heading.Children.Add(MainWindow.Text("Up to 200 snapshots per collection, retained for one year. Each snapshot preserves its own current/target/loader and membership. Old observations are not evidence for today's target."));
        var context = new ComboBox { Margin = new Thickness(0, 8, 0, 8) };
        heading.Children.Add(context);
        var entries = tracker.Store.History(pack.Id);
        context.ItemsSource = new[] { "All contexts" }.Concat(entries.Select(e => $"{e.Pack.Current} → {e.Pack.Target} / {e.Pack.Loader}").Distinct()).ToArray();
        context.SelectedIndex = 0;
        var picker = new ComboBox { Margin = new Thickness(0, 0, 0, 10), DisplayMemberPath = "Label" };
        heading.Children.Add(picker);
        var summary = MainWindow.Text("No history yet. Refresh, import or edit this collection to record an observation.");
        heading.Children.Add(summary);
        var table = DialogUi.Table(("Project", "Name"), ("Type", "Form"), ("Automatic", "Automatic"), ("Effective", "Effective"), ("Change since previous", "Change"), ("Release ID", "Candidate"), ("Manual note / source", "Note"));
        root.Children.Add(table);
        void Filter() => picker.ItemsSource = entries.Where(e => context.SelectedIndex == 0 || $"{e.Pack.Current} → {e.Pack.Target} / {e.Pack.Loader}" == (string)context.SelectedItem).Select(e => new HistoryChoice(e, $"{e.Time.ToLocalTime():g} · {e.Kind} · {e.Pack.Current} → {e.Pack.Target} / {e.Pack.Loader}")).ToArray();
        context.SelectionChanged += (_, _) => { Filter(); picker.SelectedIndex = 0; };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not HistoryChoice choice)
                return;
            var entry = choice.Entry;
            var included = entry.Items.Where(i => i.Effective != Compatibility.Ignored).ToArray();
            var previous = entries.FirstOrDefault(e => e.Id < entry.Id && e.Pack.Target == entry.Pack.Target && e.Pack.Loader == entry.Pack.Loader && e.Pack.Current == entry.Pack.Current);
            var oldIds = previous?.Items.Select(i => i.ProjectId).ToHashSet() ?? [];
            var ids = entry.Items.Select(i => i.ProjectId).ToHashSet();
            table.ItemsSource = entry.Items.Select(item =>
            {
                var old = previous?.Items.FirstOrDefault(i => i.ProjectId == item.ProjectId);
                return new
                {
                    item.Name,
                    item.Form,
                    item.Automatic,
                    item.Effective,
                    item.Candidate,
                    item.Note,
                    Change = previous == null ? "First observation" : old == null ? "Added membership" : old.Form != item.Form ? "Distribution changed" : old.Automatic != item.Automatic || old.Effective != item.Effective ? $"Auto: {old.Automatic} → {item.Automatic}; effective: {old.Effective} → {item.Effective}" : old.Candidate != item.Candidate ? "Selected release changed" : old.Note != item.Note ? "Manual evidence changed" : "Unchanged"
                };
            }).ToArray();
            summary.Text = $"{included.Count(i => CompatibilityEngine.Positive(i.Effective))}/{included.Length} compatible; {entry.Items.Length} tracked; {entry.Items.Count(i => i.Note != null)} manual. " + (previous == null ? "First retained observation in this context." : $"Membership since preceding same-context snapshot: +{ids.Except(oldIds).Count()} / −{oldIds.Except(ids).Count()}.");
        };
        Filter();
        picker.SelectedIndex = 0;
    }
    private sealed record HistoryChoice(HistoryEntry Entry, string Label);
}

public sealed class RangeDialog : Window
{
    public RangeDialog(Tracker tracker, Pack pack, Project project)
    {
        Title = "Version-range evidence — " + project.Name;
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var panel = new StackPanel();
        DockPanel.SetDock(panel, Dock.Top);
        root.Children.Add(panel);
        panel.Children.Add(MainWindow.Text("Explicit version-range evidence", 23));
        panel.Children.Add(MainWindow.Text("Enter a range only when an authoritative source explicitly supports it for this exact release. The app does not infer ranges from descriptions or version numbers. Rules are scoped to this collection, project, loader and distribution; manual overrides still take precedence."));
        var member = tracker.Store.Members(pack.Id).Single(m => m.ProjectId == project.Id);
        var required = member.Form == Distribution.Datapack ? "datapack" : pack.Loader;
        var releases = new ComboBox { ItemsSource = tracker.Store.Versions(project.Id)?.Releases.Where(r => r.Valid && r.Loaders.Contains(required)).OrderByDescending(r => r.Published).ToArray(), DisplayMemberPath = "Number", Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(releases);
        var pattern = DialogUi.Entry(panel, "Supported family or inclusive range (26.x, 26.3.x, 26.3.1–26.3.5)");
        var url = DialogUi.Entry(panel, "Authoritative evidence URL (HTTPS)");
        var note = DialogUi.Entry(panel, "Evidence note — what the source explicitly confirms");
        var status = MainWindow.Text("Select a cached release. Refresh first if this list is empty.");
        panel.Children.Add(status);
        var actions = new WrapPanel();
        panel.Children.Add(actions);
        var save = DialogUi.Button("Save evidence rule");
        var remove = DialogUi.Button("Remove selected rule");
        actions.Children.Add(save);
        actions.Children.Add(remove);
        var table = DialogUi.Table(("Release ID", "ReleaseId"), ("Pattern", "Pattern"), ("Evidence", "Reference"), ("Note", "Note"));
        root.Children.Add(table);
        void Load() => table.ItemsSource = tracker.Store.Rules(pack.Id, project.Id).Where(r => r.Loader == pack.Loader && r.Form == member.Form).ToArray();
        Load();
        save.Click += (_, _) =>
        {
            try
            {
                if (releases.SelectedItem is not Release release)
                    throw new ArgumentException("Select a release first.");
                tracker.Store.SaveRule(new(pack.Id, project.Id, release.Id, pack.Loader, member.Form, pattern.Text.Trim(), url.Text.Trim(), note.Text.Trim(), DateTimeOffset.UtcNow));
                tracker.Capture(pack, "Range evidence saved");
                Load();
                status.Text = "Rule saved. Automatic evidence will show the range and its source when it matches.";
            }
            catch (Exception ex) { DialogUi.Error(status, ex); }
        };
        remove.Click += (_, _) =>
        {
            if (table.SelectedItem is not RangeRule rule)
                return;
            try
            {
                tracker.Store.DeleteRule(rule);
                tracker.Capture(pack, "Range evidence removed");
                Load();
                status.Text = "Rule removed; exact provider evidence and manual decisions remain.";
            }
            catch (Exception ex) { DialogUi.Error(status, ex); }
        };
    }
}
