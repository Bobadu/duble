#nullable enable
using System;
using System.Collections.Generic;
using Duble.App;
using Duble.Core.Time;

namespace Duble.Tests;

/// <summary>A clock stuck at one instant, for tests that assert on a written timestamp.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; }
}

/// <summary>
/// An IProgress that calls back on the thread that reported, unlike Progress&lt;T&gt;, which posts to the
/// synchronisation context. Tests that act on a report — cancelling after the first file, for instance — need
/// to see it before the next one starts.
/// </summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    readonly Action<T> onReport;

    public SyncProgress(Action<T> onReport) => this.onReport = onReport;

    public void Report(T value) => onReport(value);
}

/// <summary>The window, without WPF: it records what the interface asked it to do.</summary>
public sealed class FakeWindow : IHostWindow
{
    public List<string> Calls { get; } = new();

    public bool IsMaximized { get; set; }

    public void Minimize() => Calls.Add("min");

    public void MaximizeOrRestore()
    {
        IsMaximized = !IsMaximized;
        Calls.Add("max");
    }

    public void Close() => Calls.Add("close");

    public void StartDrag() => Calls.Add("drag");

    public void Invoke(Action action) => action();
}

/// <summary>The system dialogs, answering with whatever the test put in them.</summary>
public sealed class FakeDialogs : IFileDialogs
{
    public string? Folder { get; set; } = @"C:\picked";
    public string[] Files { get; set; } = { @"C:\a.rpf" };
    public string? SavedFile { get; set; } = @"C:\saved.duble";

    public string? PickFolder(string? title, string? startIn) => Folder;

    public string[] PickFiles(string? title, string? filter, bool multiple, string? startIn) => Files;

    public string? SaveFile(string? title, string? filter, string? defaultName, string? startIn) => SavedFile;
}
