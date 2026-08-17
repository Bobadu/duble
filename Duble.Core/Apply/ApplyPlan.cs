#nullable enable
// The plan an apply follows, and the log that lets it be undone.
//
// THE PROJECT'S RULE: an original is never lost. Rejected files are not deleted, they are MOVED to a bin
// (_odrzucone next to the source, or a folder the user picked) keeping their layout relative to the source,
// and the list of moves lands in an undo log — one command and it all comes back, whole or one garment at a
// time.
//
// THE TRAP THIS HANDLES: two garments with the same slot and number (feet_050 and feet_050_1, where an
// exporter added the tail) SHARE their texture files. Moving "every file of the loser" would rob the winner.
// So before a file moves, the planner checks that no garment which stays is using it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Duble.Core.Apply;

/// <summary>What the plan decided to do with one file.</summary>
public enum FileMoveState
{
    /// <summary>It moves to the bin.</summary>
    Move,

    /// <summary>A garment that stays is using it, so it stays too.</summary>
    Shared,

    /// <summary>It lives inside an .rpf, and Duble never writes to archives.</summary>
    InArchive,

    /// <summary>It is not on disk, or its source is gone.</summary>
    Missing,
}

/// <summary>Jeden plik pozycji w planie: dokad pojdzie i czy w ogole (Stan).</summary>
public sealed class FileMove
{
    public const string Przenies = "przenies";
    public const string Wspoldzielony = "wspoldzielony";   // uzywa go pozycja, ktora zostaje
    public const string InArchiveCount = "wArchiwum";           // siedzi w .rpf — nie ruszamy archiwow
    public const string Brak = "brak";                     // nie ma go na dysku / brak zrodla

    public string GarmentId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public long Bytes { get; set; }
    public FileMoveState State { get; set; }
}

public sealed class PlannedGarment
{
    public string Id { get; set; } = "";
    /// <summary>slot_NNN, the name a person recognises.</summary>
    public string Name { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Container { get; set; } = "";
    public string BinFolder { get; set; } = "";
    public List<FileMove> Files { get; set; } = new();
    public int MoveCount => Files.Count(r => r.State == FileMoveState.Move);
    public long Bytes => Files.Where(r => r.State == FileMoveState.Move).Sum(r => r.Bytes);
    public int SharedCount => Files.Count(r => r.State == FileMoveState.Shared);
    public int InArchiveCount => Files.Count(r => r.State == FileMoveState.InArchive);
    public int MissingCount => Files.Count(r => r.State == FileMoveState.Missing);
}

/// <summary>Skad liczyc sciezke wzgledna pozycji (Korzen zrodla) i dokad ja przeniesc (Kosz).</summary>
public sealed class BinTarget
{
    public string Root { get; set; } = "";
    public string BinFolder { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceId { get; set; } = "";
}

public sealed class ApplyPlan
{
    public List<PlannedGarment> Pozycje { get; } = new();
    /// <summary>Nazwy zrodel, ktorych nie ma na dysku (pozycje z nich maja pliki w stanie Brak).</summary>
    public List<string> MissingSources { get; } = new();
    public int Files => Pozycje.Sum(p => p.MoveCount);
    public long Bytes => Pozycje.Sum(p => p.Bytes);
    public int SharedCount => Pozycje.Sum(p => p.SharedCount);
    public int InArchiveCount => Pozycje.Sum(p => p.InArchiveCount);
    public int MissingCount => Pozycje.Sum(p => p.MissingCount);

    public IEnumerable<(string kosz, int pliki, long bajty)> BinTotals()
        => Pozycje.Where(p => p.BinFolder != null).GroupBy(p => p.BinFolder, StringComparer.OrdinalIgnoreCase)
                  .Select(g => (g.Key, g.Sum(p => p.MoveCount), g.Sum(p => p.Bytes)))
                  .Where(x => x.Item2 > 0);
}

public sealed class FileRestore
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string GarmentId { get; set; } = "";
    public long Bytes { get; set; }
    public bool Undone { get; set; }
}

public sealed class UndoneGarment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string BinFolder { get; set; } = "";
    public int Files { get; set; }
}

/// <summary>Dziennik jednego zastosowania: co przeniesiono i skad — wystarcza, zeby wszystko wrocilo.</summary>
public sealed class UndoLog
{
    public string When { get; set; } = "";
    public string Description { get; set; } = "";
    public List<FileRestore> Moves { get; set; } = new();
    public List<UndoneGarment> Garments { get; set; } = new();
    public int SharedCount { get; set; }
    public int InArchiveCount { get; set; }
    public int MissingCount { get; set; }
    /// <summary>The apply stopped early (cancelled, or an error): Moves holds only what did move.</summary>
    public bool Aborted { get; set; }
    public string? Error { get; set; }
    /// <summary>When every move was undone; null while none or only some have been.</summary>
    public string? UndoneAt { get; set; }

    [JsonIgnore] public long Bytes => Moves.Sum(r => r.Bytes);
    [JsonIgnore] public bool PartlyUndone => UndoneAt == null && Moves.Any(r => r.Undone);
    /// <summary>There is something to undo: a move not yet undone, its file still in the bin and its old place free.</summary>
    [JsonIgnore] public bool CanUndo => Moves.Any(CanRestore);
    public static bool CanRestore(FileRestore r) => !r.Undone && File.Exists(r.To) && !File.Exists(r.From);

    public bool CanRestoreGarment(string id) => Moves.Any(r => r.GarmentId == id && CanRestore(r));
}
