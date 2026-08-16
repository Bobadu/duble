// MainWindow.xaml.cs — okno bez systemowego paska (pasek tytulu rysuje UI w HTML), WebView2 na caly obszar,
// mostek UI<->C# (Mostek), dialogi systemowe (IDialogi), przeciaganie plikow z Eksploratora.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Duble.App;

public partial class MainWindow : Window, IOkno, IDialogi
{
    public Zasoby Zasoby { get; private set; }
    public Mostek Mostek { get; private set; }
    public Sesja Sesja { get; } = new Sesja();
    public bool UiGotowe { get; private set; }

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
            Zadania?.Anuluj();   // indeksowanie w tle: przerwij, zeby proces nie wisial
            var st = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (App.Ustawienia != null) App.Ustawienia.Okno = new OknoStan { X = st.X, Y = st.Y, W = st.Width, H = st.Height, Maks = WindowState == WindowState.Maximized };
        };
        StateChanged += (s, e) => Mostek?.Zdarzenie("window.state", new { maks = WindowState == WindowState.Maximized });
    }

    async Task Start()
    {
        var arg = App.Argumenty;
        string uiFolder = null;
        if (arg.Dev) uiFolder = arg.UiFolder ?? ZnajdzFolderUi();
        Zasoby = new Zasoby(uiFolder) { Dane = (kategoria, klucz, query) => Sesja.Zasob(kategoria, klucz, query) };
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
                void Odpowiedz(bool ok, Stream tresc, string mime, int status)
                {
                    try
                    {
                        if (ok)
                            // CORS: strona z duble.app pobiera dane z duble.data (fetch/three.js) — bez tego naglowka przegladarka odrzuca odpowiedz
                            e.Response = env.CreateWebResourceResponse(tresc, 200, "OK", $"Content-Type: {mime}\nCache-Control: no-cache\nAccess-Control-Allow-Origin: *");
                        else
                        {
                            Log("404 " + e.Request.Uri);
                            e.Response = env.CreateWebResourceResponse(new MemoryStream(), status, status == 404 ? "Not Found" : "Error", "Content-Type: text/plain\nAccess-Control-Allow-Origin: *");
                        }
                    }
                    catch (Exception ex) { Log("zasob BLAD " + e.Request.Uri + ": " + ex.Message); }
                }
                var uri = e.Request.Uri;
                if (uri.StartsWith("https://duble.data/", StringComparison.OrdinalIgnoreCase))
                {
                    // dane (tekstura generowana z pliku gry ~50-200 ms) — poza watkiem UI, odpowiedz z odroczeniem
                    var odroczenie = e.GetDeferral();
                    _ = Task.Run(() =>
                    {
                        bool ok; Stream tresc = null; string mime = null; int status = 500;
                        try { ok = Zasoby.Rozwiaz(uri, out tresc, out mime, out status); }
                        catch (Exception ex) { Log("zasob BLAD " + uri + ": " + ex.Message); ok = false; }
                        Dispatcher.InvokeAsync(() => { Odpowiedz(ok, tresc, mime, status); odroczenie.Complete(); });
                    });
                    return;
                }
                bool ok2 = Zasoby.Rozwiaz(uri, out var tresc2, out var mime2, out int status2);
                Odpowiedz(ok2, tresc2, mime2, status2);
            };

            Mostek = new Mostek(this, this, App.Ustawienia, json => Dispatcher.InvokeAsync(() => web.CoreWebView2?.PostWebMessageAsJson(json))) { Dev = arg.Dev };
            Komendy.Okno.Zarejestruj(Mostek);
            Komendy.Okno.UiGotowe += UiJestGotowe;
            Upuszczono += sciezki => Mostek.Zdarzenie("files.dropped", new { sciezki });
            ZarejestrujKomendy();

            core.WebMessageReceived += (s, e) =>
            {
                var json = e.WebMessageAsJson;
                Log("msg " + (json.Length > 300 ? json.Substring(0, 300) + "…" : json));
                // handlery bywaja dlugie (dialogi, dysk) — nie blokujemy watku UI; odpowiedz wraca przez Dispatcher w wyslij()
                _ = Task.Run(async () => { var odp = await Mostek.Obsluz(json); await Dispatcher.InvokeAsync(() => web.CoreWebView2?.PostWebMessageAsJson(odp)); });
            };
            core.NavigationCompleted += (s, e) => Log($"navigation completed ok={e.IsSuccess} status={e.HttpStatusCode} err={e.WebErrorStatus}");
            var q = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(arg.Widok)) q.Add("view=" + Uri.EscapeDataString(arg.Widok));
            if (!string.IsNullOrEmpty(arg.Jezyk)) q.Add("lang=" + Uri.EscapeDataString(arg.Jezyk));
            if (!string.IsNullOrEmpty(arg.Motyw)) q.Add("theme=" + Uri.EscapeDataString(arg.Motyw));
            var url = "https://duble.app/index.html" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
            Log("navigate " + url);
            core.Navigate(url);
        }
        catch (Exception e)
        {
            Log("BLAD startu: " + e);
            MessageBox.Show("Nie udalo sie uruchomic WebView2 (potrzebny Microsoft Edge WebView2 Runtime):\n\n" + e.Message, "Duble", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>Komendy z danymi: projekt, zrodla (+rozpakuj), grupy (+zastosuj), historia (+eksport), katalog.</summary>
    void ZarejestrujKomendy()
    {
        Komendy.Projekty.Zarejestruj(Mostek, Sesja);
        Zadania = new JobRunner(Mostek.Zdarzenie);
        Komendy.Zrodla.Zarejestruj(Mostek, Sesja, Zadania);
        Komendy.Grupy.Zarejestruj(Mostek, Sesja, Zadania);
        Komendy.Historia.Zarejestruj(Mostek, Sesja, Zadania);
        Komendy.KatalogPozycji.Zarejestruj(Mostek, Sesja);
    }
    public JobRunner Zadania { get; private set; }

    void UiJestGotowe()
    {
        UiGotowe = true;
        Log("ui.ready");
        _ = Dispatcher.InvokeAsync(async () =>
        {
            // projekt z argumentow (dwuklik na .duble albo --project) — dopiero teraz, bo UI juz nasluchuje zdarzen
            if (!string.IsNullOrEmpty(App.Argumenty.Projekt))
            {
                try
                {
                    var odp = await Mostek.Obsluz(System.Text.Json.JsonSerializer.Serialize(new { id = "start", cmd = "project.open", args = new { sciezka = App.Argumenty.Projekt } }));
                    Log("projekt z argumentow: " + odp);
                }
                catch (Exception e) { Log("projekt z argumentow BLAD: " + e.Message); }
            }
            if (!string.IsNullOrEmpty(App.Argumenty.Exec))
            {
                await Task.Delay(300);
                var wynik = await web.CoreWebView2.ExecuteScriptAsync(App.Argumenty.Exec);
                Log("exec -> " + wynik);
            }
            if (!string.IsNullOrEmpty(App.Argumenty.Zrzut)) await ZrobZrzutIZamknij(App.Argumenty.Zrzut);
        });
    }

    public async Task ZrobZrzutIZamknij(string plik)
    {
        await Task.Delay(App.Argumenty?.ZrzutOpoznienie ?? 700);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plik)));
        using (var fs = new FileStream(plik, FileMode.Create, FileAccess.Write))
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        Log("zrzut " + plik);
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

    // ---------------- IOkno ----------------
    public void Minimalizuj() => WindowState = WindowState.Minimized;
    public void MaksymalizujAlboPrzywroc() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    public void Zamknij() => Close();
    public void RozpocznijPrzeciaganie() { try { DragMove(); } catch { /* DragMove wymaga wcisnietego przycisku myszy */ } }
    public bool Zmaksymalizowane => Dispatcher.CheckAccess() ? WindowState == WindowState.Maximized : Dispatcher.Invoke(() => WindowState == WindowState.Maximized);
    public void Uruchom(Action a) { if (Dispatcher.CheckAccess()) a(); else Dispatcher.Invoke(a); }

    // ---------------- IDialogi (systemowe okna Windows; zawsze na watku UI) ----------------
    static string Filtr(string klucz) => klucz switch
    {
        "rpf" => "Archiwa RPF (*.rpf)|*.rpf|Wszystkie pliki (*.*)|*.*",
        "duble" => "Projekty Duble (*.duble)|*.duble|Wszystkie pliki (*.*)|*.*",
        "png" => "Obrazy PNG (*.png)|*.png",
        "html" => "Strony HTML (*.html)|*.html",
        "csv" => "Pliki CSV (*.csv)|*.csv",
        _ => "Wszystkie pliki (*.*)|*.*",
    };

    public string WybierzFolder(string tytul, string start) => Dispatcher.Invoke(() =>
    {
        var d = new OpenFolderDialog { Title = tytul ?? "Duble", Multiselect = false };
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.InitialDirectory = start;
        return d.ShowDialog(this) == true ? d.FolderName : null;
    });

    public string[] WybierzPliki(string tytul, string filtr, bool wiele, string start) => Dispatcher.Invoke(() =>
    {
        var d = new OpenFileDialog { Title = tytul ?? "Duble", Filter = Filtr(filtr), Multiselect = wiele, CheckFileExists = true };
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.InitialDirectory = start;
        return d.ShowDialog(this) == true ? d.FileNames : Array.Empty<string>();
    });

    public string ZapiszPlik(string tytul, string filtr, string domyslnaNazwa, string start) => Dispatcher.Invoke(() =>
    {
        var d = new SaveFileDialog { Title = tytul ?? "Duble", Filter = Filtr(filtr), FileName = domyslnaNazwa ?? "", OverwritePrompt = true };
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.InitialDirectory = start;
        return d.ShowDialog(this) == true ? d.FileName : null;
    });

    // ---------------- drag & drop z Eksploratora (AllowExternalDrop=false w WebView2, wiec zdarzenia trafiaja do WPF) ----------------
    public event Action<string[]> Upuszczono;
    void OknoDragOver(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    void OknoDrop(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var sciezki = (string[])e.Data.GetData(DataFormats.FileDrop);
        Upuszczono?.Invoke(sciezki);
    }
}
