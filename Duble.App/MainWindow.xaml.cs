// MainWindow.xaml.cs — the window: no system title bar (the interface draws its own in HTML), a WebView2
// filling it, the bridge between the two, the system dialogs, and files dragged in from Explorer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Duble.App.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Duble.App;

public partial class MainWindow : Window, IHostWindow, IFileDialogs
{
    public Session Session { get; } = App.Services.GetRequiredService<Session>();

    /// <summary>Serves the interface and its data to WebView2. Not called Resources: a Window has one already.</summary>
    public WebResources? Assets { get; private set; }

    public Bridge? Bridge { get; private set; }
    public JobRunner? Jobs { get; private set; }

    /// <summary>Whether the interface has reported that it is up.</summary>
    public bool UiReady { get; private set; }

    /// <summary>Files dropped on the window, either through WPF or through WebView2.</summary>
    public event Action<string[]>? Dropped;

    /// <summary>The developer-mode log: %TEMP%\duble-app\duble-log.txt, for diagnosing WebView2 and the bridge.</summary>
    public static void Log(string line)
    {
        if (App.Options == null || !App.Options.Dev) return;
        try
        {
            var file = Path.Combine(Path.GetTempPath(), "duble-app", "duble-log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
        }
        catch { /* the log is a convenience, never a reason to fail */ }
    }

    public MainWindow()
    {
        InitializeComponent();

        var placement = App.Settings?.Window;
        if (placement != null && placement.Width >= 600 && placement.Height >= 400)
        {
            Left = placement.X;
            Top = placement.Y;
            Width = placement.Width;
            Height = placement.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }

        Loaded += async (_, _) => await Start();
        Closing += (_, _) =>
        {
            Jobs?.Cancel();   // a job left running would keep the process alive after the window has gone
            var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            if (App.Settings != null)
                App.Settings.Window = new WindowPlacement
                {
                    X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                    Maximized = WindowState == WindowState.Maximized,
                };
        };
        StateChanged += (_, _) => Bridge?.Event("window.state", new { maks = WindowState == WindowState.Maximized });
    }

    async Task Start()
    {
        var options = App.Options;
        var uiFolder = options.Dev ? options.UiFolder ?? FindUiFolder() : null;
        Assets = new WebResources(uiFolder) { Data = Session.Asset };
        Log($"start dev={options.Dev} ui={uiFolder ?? "(embedded)"} screenshot={options.ScreenshotFile}");

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Settings.WebView2Folder, new CoreWebView2EnvironmentOptions());
            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2;

            core.Settings.IsNonClientRegionSupportEnabled = true;    // app-region: drag in HTML moves the window
            core.Settings.AreDefaultContextMenusEnabled = options.Dev;
            core.Settings.AreDevToolsEnabled = options.Dev;
            core.Settings.AreBrowserAcceleratorKeysEnabled = options.Dev;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            core.AddWebResourceRequestedFilter("https://duble.app/*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter("https://duble.data/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => Serve(environment, e);

            var bridge = new Bridge(this, this, App.Settings,
                json => Dispatcher.InvokeAsync(() => web.CoreWebView2?.PostWebMessageAsJson(json)))
            {
                Dev = options.Dev,
                // a screenshot run keeps its settings (recent projects, language) in a temporary file
                SettingsFile = string.IsNullOrEmpty(options.ScreenshotFile) ? null
                    : Path.Combine(Path.GetTempPath(), "duble-app", "settings-screenshot.json"),
            };
            Bridge = bridge;
            Jobs = new JobRunner(bridge.Event);

            foreach (var module in CommandModules.Create(App.Services, bridge, Session, Jobs))
            {
                module.Register();
                if (module is AppCommands app) app.UiReady += OnUiReady;
            }

            Dropped += paths => bridge.Event("files.dropped", new { sciezki = paths });
            core.WebMessageReceived += (_, e) => OnWebMessage(bridge, e);
            core.NavigationCompleted += (_, e) => Log($"navigation completed ok={e.IsSuccess} status={e.HttpStatusCode} err={e.WebErrorStatus}");

            var url = StartUrl(options);
            Log("navigate " + url);
            core.Navigate(url);
        }
        catch (Exception e)
        {
            Log("start FAILED: " + e);
            MessageBox.Show(
                (InPolish
                    ? "Nie udalo sie uruchomic WebView2 (potrzebny Microsoft Edge WebView2 Runtime):\n\n"
                    : "WebView2 could not be started (the Microsoft Edge WebView2 Runtime is required):\n\n") + e.Message,
                "Duble", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>--view, --lang and --theme reach the interface as query parameters, for this run only.</summary>
    static string StartUrl(StartupOptions options)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(options.View)) query.Add("view=" + Uri.EscapeDataString(options.View));
        if (!string.IsNullOrEmpty(options.Language)) query.Add("lang=" + Uri.EscapeDataString(options.Language));
        if (!string.IsNullOrEmpty(options.Theme)) query.Add("theme=" + Uri.EscapeDataString(options.Theme));
        return "https://duble.app/index.html" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
    }

    // ---------------- serving the interface and its data ----------------

    /// <summary>
    /// The two places where C# still puts words in front of the user: Windows' own file dialogs, and the
    /// message when WebView2 will not start — which happens before the interface, and its dictionaries, exist.
    /// </summary>
    static bool InPolish => (App.Options?.Language ?? App.Settings?.EffectiveLanguage ?? "en") == "pl";

    void Serve(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;

        // a texture is decoded from a game file on the first ask (50-200 ms), so data requests answer off the
        // UI thread; the interface would otherwise freeze while a comparison screen fills up
        if (uri.StartsWith("https://duble.data/", StringComparison.OrdinalIgnoreCase))
        {
            var deferral = e.GetDeferral();
            _ = Task.Run(() =>
            {
                WebResource? resource = null;
                try { resource = Assets?.Resolve(uri); }
                catch (Exception ex) { Log("resource FAILED " + uri + ": " + ex.Message); }
                Dispatcher.InvokeAsync(() =>
                {
                    Respond(environment, e, resource);
                    deferral.Complete();
                });
            });
            return;
        }

        Respond(environment, e, Assets?.Resolve(uri));
    }

    void Respond(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs e, WebResource? resource)
    {
        try
        {
            if (resource != null)
                // CORS: the page on duble.app fetches from duble.data, and without this header the browser
                // throws the answer away
                e.Response = environment.CreateWebResourceResponse(resource.Content, 200, "OK",
                    $"Content-Type: {resource.Mime}\nCache-Control: no-cache\nAccess-Control-Allow-Origin: *");
            else
            {
                Log("404 " + e.Request.Uri);
                e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found",
                    "Content-Type: text/plain\nAccess-Control-Allow-Origin: *");
            }
        }
        catch (Exception ex) { Log("resource FAILED " + e.Request.Uri + ": " + ex.Message); }
    }

    void OnWebMessage(Bridge bridge, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // files dragged from Explorer: the page posts the File objects with postMessageWithAdditionalObjects
        // and WebView2 hands them over as CoreWebView2File, which is the only way to learn their PATH — an
        // HTML5 drop does not carry one, and the WebView2 child window does not pass OLE drops to WPF
        if (e.AdditionalObjects is { Count: > 0 })
        {
            var paths = new List<string>();
            foreach (var item in e.AdditionalObjects)
                if (item is CoreWebView2File file && !string.IsNullOrEmpty(file.Path)) paths.Add(file.Path);
            Log("drop " + paths.Count + " files");
            if (paths.Count > 0) Dropped?.Invoke(paths.ToArray());
            return;
        }

        var json = e.WebMessageAsJson;
        Log("msg " + (json.Length > 300 ? json.Substring(0, 300) + "…" : json));
        // handlers can be slow (dialogs, disk), so they do not run on the UI thread; the answer goes back
        // through the dispatcher in the bridge's send callback
        _ = Task.Run(async () =>
        {
            var response = await bridge.Handle(json);
            await Dispatcher.InvokeAsync(() => web.CoreWebView2?.PostWebMessageAsJson(response));
        });
    }

    // ---------------- what happens once the interface is up ----------------

    void OnUiReady()
    {
        UiReady = true;
        Log("ui.ready");
        _ = Dispatcher.InvokeAsync(async () =>
        {
            // the project from the command line (a double click on a .duble, or --project) opens only now,
            // because the interface has to be listening for the events it produces
            if (!string.IsNullOrEmpty(App.Options.ProjectFile))
            {
                try
                {
                    var request = JsonSerializer.Serialize(new
                    {
                        id = "start",
                        cmd = "project.open",
                        args = new { sciezka = App.Options.ProjectFile },
                    });
                    Log("project from the command line: " + await Bridge!.Handle(request));
                }
                catch (Exception e) { Log("project from the command line FAILED: " + e.Message); }
            }

            if (!string.IsNullOrEmpty(App.Options.Exec))
            {
                await Task.Delay(300);
                Log("exec -> " + await web.CoreWebView2.ExecuteScriptAsync(App.Options.Exec));
            }

            if (!string.IsNullOrEmpty(App.Options.ScreenshotFile)) await ScreenshotAndExit(App.Options.ScreenshotFile);
        });
    }

    public async Task ScreenshotAndExit(string file)
    {
        await Task.Delay(App.Options?.ScreenshotDelayMs ?? 700);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write))
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        Log("screenshot " + file);
        Application.Current.Shutdown(0);
    }

    /// <summary>The ui\ folder, searched for upwards from the executable; null means the embedded copy.</summary>
    static string? FindUiFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ui", "index.html"))) return Path.Combine(folder.FullName, "ui");
            folder = folder.Parent;
        }
        return null;
    }

    // ---------------- IHostWindow ----------------

    public void Minimize() => WindowState = WindowState.Minimized;

    public void MaximizeOrRestore()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // IHostWindow.Close is Window.Close itself

    public void StartDrag()
    {
        try { DragMove(); }
        catch { /* DragMove needs the mouse button to be down, and the interface may be late telling us */ }
    }

    public bool IsMaximized => Dispatcher.CheckAccess()
        ? WindowState == WindowState.Maximized
        : Dispatcher.Invoke(() => WindowState == WindowState.Maximized);

    public void Invoke(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    // ---------------- IFileDialogs (always on the UI thread) ----------------

    static string Filter(string? key)
    {
        var all = InPolish ? "Wszystkie pliki (*.*)|*.*" : "All files (*.*)|*.*";
        return key switch
        {
            "rpf" => (InPolish ? "Archiwa RPF (*.rpf)|*.rpf|" : "RPF archives (*.rpf)|*.rpf|") + all,
            "duble" => (InPolish ? "Projekty Duble (*.duble)|*.duble|" : "Duble projects (*.duble)|*.duble|") + all,
            "png" => InPolish ? "Obrazy PNG (*.png)|*.png" : "PNG images (*.png)|*.png",
            "html" => InPolish ? "Strony HTML (*.html)|*.html" : "HTML pages (*.html)|*.html",
            "csv" => InPolish ? "Pliki CSV (*.csv)|*.csv" : "CSV files (*.csv)|*.csv",
            _ => all,
        };
    }

    public string? PickFolder(string? title, string? startIn) => Dispatcher.Invoke(() =>
    {
        var dialog = new OpenFolderDialog { Title = title ?? "Duble", Multiselect = false };
        if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn)) dialog.InitialDirectory = startIn;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    });

    public string[] PickFiles(string? title, string? filter, bool multiple, string? startIn) => Dispatcher.Invoke(() =>
    {
        var dialog = new OpenFileDialog
        {
            Title = title ?? "Duble", Filter = Filter(filter), Multiselect = multiple, CheckFileExists = true,
        };
        if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn)) dialog.InitialDirectory = startIn;
        return dialog.ShowDialog(this) == true ? dialog.FileNames : Array.Empty<string>();
    });

    public string? SaveFile(string? title, string? filter, string? defaultName, string? startIn) => Dispatcher.Invoke(() =>
    {
        var dialog = new SaveFileDialog
        {
            Title = title ?? "Duble", Filter = Filter(filter), FileName = defaultName ?? "", OverwritePrompt = true,
        };
        if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn)) dialog.InitialDirectory = startIn;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    });

    // ---------------- drag and drop from Explorer ----------------

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Dropped?.Invoke(paths);
    }
}
