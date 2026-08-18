// HostServices.cs — what the commands need from the window they run in. MainWindow implements both; the
// tests implement them with fakes, which is what keeps the command layer testable without starting WPF.
using System;

namespace Duble.App;

/// <summary>The window itself. The title bar is drawn by the interface in HTML, so it asks for these.</summary>
public interface IHostWindow
{
    void Minimize();
    void MaximizeOrRestore();
    void Close();

    /// <summary>Fallback for dragging the window when the HTML `app-region: drag` does not take effect.</summary>
    void StartDrag();

    bool IsMaximized { get; }

    /// <summary>Runs an action on the UI thread. Bridge commands arrive on the thread pool.</summary>
    void Invoke(Action action);
}

/// <summary>The system file dialogs, which only exist on the UI thread.</summary>
public interface IFileDialogs
{
    string? PickFolder(string? title, string? startIn);
    string[] PickFiles(string? title, string? filter, bool multiple, string? startIn);
    string? SaveFile(string? title, string? filter, string? defaultName, string? startIn);
}
