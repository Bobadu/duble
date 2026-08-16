// MainWindow.xaml.cs — okno bez systemowego paska (pasek tytulu rysuje UI w HTML), WebView2 na caly obszar.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Duble.App;

public partial class MainWindow : Window
{
    public Zasoby Zasoby { get; private set; }
    public bool UiGotowe { get; private set; }
    public event Action<string[]> Upuszczono;

    /// <summary>Dziennik trybu dev: %TEMP%\duble-app\duble-log.txt (diagnostyka startu WebView2 i mostka).</summary>
    public static void Log(string s)
    {
        if (App.Argumenty == null || !App.Argumenty.Dev) return;
        try
        {
            var f = Path.Combine(Path.GetTempPath(), "duble-app", "duble-log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
        }
        catch { }
    }

    public MainWindow()
    {
        InitializeComponent();
        var u = App.Ustawienia;
        if (u?.Okno != null && u.Okno.W >= 600 && u.Okno.H >= 400)
        {
            Left = u.Okno.X; Top = u.Okno.Y; Width = u.Okno.W; Height = u.Okno.H;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (u.Okno.Maks) WindowState = WindowState.Maximized;
        }
        Loaded += async (s, e) => await Start();
        Closing += (s, e) =>
        {
            var st = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (App.Ustawienia != null) App.Ustawienia.Okno = new OknoStan { X = st.X, Y = st.Y, W = st.Width, H = st.Height, Maks = WindowState == WindowState.Maximized };
        };
    }

    async Task Start()
    {
        var arg = App.Argumenty;
        string uiFolder = null;
        if (arg.Dev) uiFolder = arg.UiFolder ?? ZnajdzFolderUi();
        Zasoby = new Zasoby(uiFolder);
        Log($"start dev={arg.Dev} ui={(uiFolder ?? "(osadzone)")} zrzut={arg.Zrzut}");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Ustawienia.FolderWebView2, new CoreWebView2EnvironmentOptions());
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.IsNonClientRegionSupportEnabled = true;    // app-region: drag w HTML = przeciaganie okna
            core.Settings.AreDefaultContextMenusEnabled = arg.Dev;
            core.Settings.AreDevToolsEnabled = arg.Dev;
            core.Settings.AreBrowserAcceleratorKeysEnabled = arg.Dev;
            core.Settings.IsZoomControlEnabled = false; core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.IsSwipeNavigationEnabled = false;
            core.AddWebResourceRequestedFilter("https://duble.app/*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter("https://duble.data/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (s, e) =>
            {
                try
                {
                    if (Zasoby.Rozwiaz(e.Request.Uri, out var tresc, out var mime, out int status))
                        // CORS: strona z duble.app pobiera dane z duble.data (fetch/three.js) — bez tego naglowka przegladarka odrzuca odpowiedz
                        e.Response = env.CreateWebResourceResponse(tresc, 200, "OK", $"Content-Type: {mime}\nCache-Control: no-cache\nAccess-Control-Allow-Origin: *");
                    else
                    {
                        Log("404 " + e.Request.Uri);
                        e.Response = env.CreateWebResourceResponse(new MemoryStream(), status, status == 404 ? "Not Found" : "Error", "Content-Type: text/plain");
                    }
                }
                catch (Exception ex) { Log("zasob BLAD " + e.Request.Uri + ": " + ex.Message); }
            };
            core.WebMessageReceived += (s, e) => { Log("msg " + e.WebMessageAsJson); OdebranoWiadomosc(e.WebMessageAsJson); };
            core.NavigationCompleted += (s, e) => Log($"navigation completed ok={e.IsSuccess} status={e.HttpStatusCode} err={e.WebErrorStatus}");
            PoInicjalizacji();
            Log("navigate");
            core.Navigate("https://duble.app/index.html");
        }
        catch (Exception e)
        {
            Log("BLAD startu: " + e);
            MessageBox.Show("Nie udalo sie uruchomic WebView2 (potrzebny Microsoft Edge WebView2 Runtime):\n\n" + e.Message, "Duble", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>Miejsce na podpiecie mostka (Zadanie 2).</summary>
    partial void PoInicjalizacjiCzesc();
    void PoInicjalizacji() => PoInicjalizacjiCzesc();

    /// <summary>Wiadomosc z UI. Do czasu podpiecia mostka obslugujemy tylko sygnal gotowosci (zrzut ekranu).</summary>
    void OdebranoWiadomosc(string json)
    {
        if (ObslugaWiadomosci != null) { ObslugaWiadomosci(json); return; }
        if (json.Contains("\"ui.ready\"")) UiJestGotowe();
    }

    /// <summary>Podpina Mostek (Zadanie 2). Gdy null, wiadomosci trafiaja do prostej obslugi powyzej.</summary>
    public Action<string> ObslugaWiadomosci { get; set; }

    public void UiJestGotowe()
    {
        UiGotowe = true;
        if (!string.IsNullOrEmpty(App.Argumenty.Zrzut)) _ = ZrobZrzutIZamknij(App.Argumenty.Zrzut);
    }

    public async Task ZrobZrzutIZamknij(string plik)
    {
        await Task.Delay(700);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plik)));
        using (var fs = new FileStream(plik, FileMode.Create, FileAccess.Write))
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        Application.Current.Shutdown(0);
    }

    static string ZnajdzFolderUi()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var k = Path.Combine(d.FullName, "ui", "index.html");
            if (File.Exists(k)) return Path.Combine(d.FullName, "ui");
            d = d.Parent;
        }
        return null;   // brak folderu -> Zasoby(null) = osadzone
    }

    void OknoDragOver(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    void OknoDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var sciezki = (string[])e.Data.GetData(DataFormats.FileDrop);
        Upuszczono?.Invoke(sciezki);
    }
}
