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
        DataDirectory = Environment.GetEnvironmentVariable("MOD_MENAGERIE_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheModMenagerie");
        try
        {
            mutex = new System.Threading.Mutex(true, "Local\\TheModMenagerie-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataDirectory)))[..16], out var first);
            if (!first)
            {
                MessageBox.Show("The Mod Menagerie is already open for this data folder.");
                Shutdown();
                return;
            }
            var store = new SqliteStore(Path.Combine(DataDirectory, "menagerie.db"));
            provider = new ModrinthClient(logger: Log);
            new MainWindow(new Tracker(store, provider)).Show();
        }
        catch (Exception ex) { Log(ex); MessageBox.Show("The local database could not be opened. No data was reset. Check folder permissions and available disk space.\n\n" + DataDirectory + "\n\n" + ex.Message, "The Mod Menagerie", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
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
