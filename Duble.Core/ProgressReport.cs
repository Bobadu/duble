namespace Duble.Core;

/// <summary>
/// How far a long job has got. One shape for all of them — indexing, comparing, applying — because the
/// interface shows them all in the same bar.
/// </summary>
/// <param name="Stage">Which part of the job is running, for example "models" or "textures".</param>
/// <param name="Done">Items finished.</param>
/// <param name="Total">Items in this stage, or 0 when it cannot be counted in advance.</param>
/// <param name="Container">What is being worked on — a pack or a file name — or null.</param>
public sealed record ProgressReport(string Stage, int Done, int Total, string? Container);
