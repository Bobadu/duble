namespace Duble.Core.Results;

/// <summary>
/// Every failure code Core can return, alongside the message. They are part of the engine's contract: an
/// application is meant to be able to tell "this project is from a newer Duble" from "this file is corrupt"
/// without reading the sentence.
///
/// Duble's own app does not yet — it shows the message and its own bridge code. That is a gap in the app, not
/// a reason for the engine to stop saying which failure it was.
/// </summary>
public static class ErrorCodes
{
    public const string ProjectUnreadable = "project.unreadable";
    public const string ProjectUnwritable = "project.unwritable";
    public const string ProjectUnsupportedVersion = "project.unsupported_version";
    public const string SourceMissing = "source.missing";
    public const string SourceUnreadable = "source.unreadable";
    public const string ArchiveUnreadable = "archive.unreadable";
    public const string ModelUnreadable = "model.unreadable";
    public const string TextureUndecodable = "texture.undecodable";
    public const string CatalogUnwritable = "catalog.unwritable";
    public const string ApplyIo = "apply.io";
    public const string ReportUnwritable = "report.unwritable";
}
