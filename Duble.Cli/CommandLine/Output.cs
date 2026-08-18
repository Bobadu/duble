using System;

namespace Duble.Cli.CommandLine;

/// <summary>
/// Everything the tool prints. Results go to stdout so they can be piped; anything that went wrong goes to
/// stderr, so a script can keep the two apart.
/// </summary>
public sealed class Output
{
    readonly Action<string> write;
    readonly Action<string> writeError;

    public Output(Action<string> write, Action<string> writeError)
    {
        this.write = write;
        this.writeError = writeError;
    }

    public static Output Console() => new(System.Console.Out.WriteLine, System.Console.Error.WriteLine);

    public void Line(string text = "") => write(text);

    /// <summary>Indented under whatever was said last — progress and detail.</summary>
    public void Detail(string text) => write("  " + text);

    public void Error(string text) => writeError("[error] " + text);

    public void Warning(string text) => writeError("[warning] " + text);
}
