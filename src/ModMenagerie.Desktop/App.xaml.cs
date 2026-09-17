using System.IO;
using System.Windows;
using ModMenagerie.Application;
using ModMenagerie.Infrastructure;
namespace ModMenagerie.Desktop;

public partial class App : System.Windows.Application
{
    public static string DataDirectory { get; private set; } = "";
    private ModrinthClient? provider;
    private System.Threading.Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // AppData can be virtualized when launched by a packaged host such as Codex.
        // A profile-root directory resolves consistently from both that host and Explorer.
        var dataOverride = Environment.GetEnvironmentVariable("MOD_MENAGERIE_DATA");
        DataDirectory = string.IsNullOrWhiteSpace(dataOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".modmenagerie")
            : Path.GetFullPath(dataOverride);
        try
        {
            mutex = new System.Threading.Mutex(true, "Local\\TheModMenagerie-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataDirectory)))[..16], out var first);
            if (!first)
            {
                MessageBox.Show("The Mod Menagerie is already open for this data folder.");
                Shutdown();
                return;
            }
            var databasePath = Path.Combine(DataDirectory, "menagerie.db");
            if (string.IsNullOrWhiteSpace(dataOverride) && !File.Exists(databasePath))
                MigrateLegacyDatabase(databasePath);
            var store = new SqliteStore(databasePath);
            provider = new ModrinthClient(logger: Log);
            new MainWindow(new Tracker(store, provider)).Show();
        }
        catch (Exception ex) { Log(ex); MessageBox.Show("The local database could not be opened. No data was reset. Check folder permissions and available disk space.\n\n" + DataDirectory + "\n\n" + ex.Message, "The Mod Menagerie", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    private static void MigrateLegacyDatabase(string destination)
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheModMenagerie", "menagerie.db");
        if (!File.Exists(legacy)) return;
        Directory.CreateDirectory(DataDirectory);
        // SQLite backup includes committed journal/WAL data; the original is retained.
        var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var source = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = legacy, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var target = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
            File.AppendAllText(Path.Combine(DataDirectory, "logs", DateTime.Now.ToString("yyyy-MM-dd") + ".log"), DateTimeOffset.UtcNow + " " + ex + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        provider?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
