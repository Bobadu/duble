#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Duble.Core.Apply;

/// <summary>Carries a plan out, and puts it back.</summary>
public interface IApplyExecutor
{
    /// <summary>
    /// Moves what the plan says to move and returns the log that undoes it. Cancelling stops between files:
    /// what has already moved stays moved and is in the log, and the log is marked as aborted.
    /// </summary>
    UndoLog Execute(ApplyPlan plan, string description,
                    IProgress<ProgressReport>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Puts the files back where they came from. Without garmentIds the whole log is undone; with them, only
    /// those garments. Returns how many files came back and how many were skipped — a file is skipped when it
    /// is no longer in the bin, or when something else is already sitting where it would return to.
    /// </summary>
    (int restored, int skipped) Undo(UndoLog log, IEnumerable<string>? garmentIds = null,
                                     IProgress<ProgressReport>? progress = null);
}

/// <inheritdoc />
public sealed class ApplyExecutor : IApplyExecutor
{
    public UndoLog Execute(ApplyPlan plan, string description,
                           IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        var cofka = new UndoLog
        {
            When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Description = description,
            SharedCount = plan.SharedCount, InArchiveCount = plan.InArchiveCount, MissingCount = plan.MissingCount,
        };
        var ruchy = plan.Pozycje.SelectMany(p => p.Files.Where(r => r.State == FileMoveState.Move).Select(r => (p, r))).ToList();
        int n = ruchy.Count, i = 0;
        var pozycje = new Dictionary<string, UndoneGarment>();
        foreach (var (p, r) in ruchy)
        {
            progress?.Report(new ProgressReport("zastosuj", i, n, p.Name));
            if (ct.IsCancellationRequested) { cofka.Aborted = true; break; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.To) ?? "");
                if (File.Exists(r.To)) File.Delete(r.To);   // stary odrzut o tej samej nazwie — nadpisujemy (to kosz)
                File.Move(r.From, r.To);
            }
            catch (Exception e) { cofka.Aborted = true; cofka.Error = $"{r.From}: {e.Message}"; break; }
            cofka.Moves.Add(new FileRestore { From = r.From, To = r.To, GarmentId = p.Id, Bytes = r.Bytes });
            if (!pozycje.TryGetValue(p.Id, out var pc))
                pozycje[p.Id] = pc = new UndoneGarment { Id = p.Id, Name = p.Name + (string.IsNullOrEmpty(p.Suffix) ? "" : " " + p.Suffix), SourceName = p.SourceName, SourceId = p.SourceId, BinFolder = p.BinFolder };
            pc.Files++;
            i++;
        }
        progress?.Report(new ProgressReport("zastosuj", i, n, null));
        cofka.Garments = pozycje.Values.ToList();
        return cofka;
    }

    // ===================== cofniecie =====================

    /// <summary>Przywraca pliki (wszystkie albo tylko podanych pozycji). Ruch pominiety, gdy pliku nie ma juz w koszu albo
    /// miejsce zrodlowe jest zajete. Oznacza Cofniety; gdy nie zostal zaden niecofniety ruch — ustawia UndoneAt.</summary>
    public (int restored, int skipped) Undo(UndoLog cofka, IEnumerable<string>? tylkoPozycje = null,
                                            IProgress<ProgressReport>? progress = null)
    {
        var tylko = tylkoPozycje == null ? null : new HashSet<string>(tylkoPozycje);
        var doCofniecia = cofka.Moves.Where(r => !r.Undone && (tylko == null || tylko.Contains(r.GarmentId))).ToList();
        int wrocilo = 0, pominieto = 0, i = 0;
        foreach (var r in doCofniecia)
        {
            progress?.Report(new ProgressReport("cofnij", i++, doCofniecia.Count, Path.GetFileName(r.From)));
            if (!File.Exists(r.To))
            {
                // pliku nie ma w koszu, a jest na starym miejscu = w praktyce juz cofniety (np. cofka nie zdazyla sie zapisac) — uznajemy
                if (File.Exists(r.From)) { r.Undone = true; } else pominieto++;
                continue;
            }
            if (File.Exists(r.From)) { pominieto++; continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.From) ?? "");
                File.Move(r.To, r.From);
                r.Undone = true; wrocilo++;
                UsunPusteFoldery(Path.GetDirectoryName(r.To) ?? "");
            }
            catch { pominieto++; }
        }
        if (cofka.Moves.All(r => r.Undone) && cofka.Moves.Count > 0) cofka.UndoneAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return (wrocilo, pominieto);
    }

    /// <summary>Po cofnieciu sprzatamy puste foldery kosza (do 8 poziomow w gore) — zeby po Cofnij nie zostawal szkielet _odrzucone.</summary>
    static void UsunPusteFoldery(string folder)
    {
        for (int k = 0; k < 8 && !string.IsNullOrEmpty(folder); k++)
        {
            try
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return;
                Directory.Delete(folder);
            }
            catch { return; }
            folder = Path.GetDirectoryName(folder) ?? "";
        }
    }

    // ===================== CLI (plik decyzji TSV + jeden korzen kosza) =====================


}
