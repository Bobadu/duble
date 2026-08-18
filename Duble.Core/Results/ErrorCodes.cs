namespace Duble.Core.Results;

/// <summary>
/// Every failure code Core can return. The app maps them to bridge error codes and i18n keys, the CLI prints
/// them. The codes are part of the contract: rename one and the app stops recognising the failure.
/// </summary>
public static class ErrorCodes
{
    public const string ProjectUnreadable = "project.unreadable";
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
