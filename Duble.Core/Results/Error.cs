namespace Duble.Core.Results;

/// <summary>A failure the caller can act on: a stable code plus a message for a log or for the user.</summary>
public readonly record struct Error(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}
