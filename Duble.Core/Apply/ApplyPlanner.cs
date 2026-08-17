#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Duble.Core.Comparison;
using Duble.Core.Model;
using Duble.Core.Sources;

namespace Duble.Core.Apply;

/// <summary>Works out what an apply would do, without doing any of it.</summary>
public interface IApplyPlanner
{
    /// <summary>
    /// The plan for rejecting those garments: which files move where, which are left alone because a garment
    /// that stays shares them, which sit inside an archive, and which are missing. The target callback says
    /// where each garment's bin is; returning null for a garment marks its source as gone.
    /// </summary>
    ApplyPlan Plan(Catalog catalog, IEnumerable<string> rejectedIds, Func<Garment, BinTarget?> target);
}

/// <inheritdoc />
public sealed class ApplyPlanner : IApplyPlanner
{
    public static void ZapiszDecyzje(ComparisonResult wynik, Catalog katalog, string sciezka)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Lista pozycji, ktore `duble zastosuj` przeniesie do _odrzucone\\.");
        sb.AppendLine("# Zmien TAK na NIE w pierwszej kolumnie przy tych, ktore chcesz zachowac.");
        sb.AppendLine("# Kolumny rozdzielone TABEM. Linie z # sa pomijane.");
        sb.AppendLine("odrzucic\twerdykt\tpozycja\tzostaje_zamiast\tpowod");
        foreach (var g in wynik.Groups.Where(g => g.Verdict == Verdict.Duplicate || g.Verdict == Verdict.Superset))
            foreach (var id in g.Members.Where(x => x != g.Winner))
            {
                var powod = Texts.Reason(g.Pairs.FirstOrDefault()?.Reason ?? g.Reason, "pl");
                sb.AppendLine($"TAK\t{g.Verdict}\t{id}\t{g.Winner}\t{powod.Replace('\t', ' ')}");
            }
        var kat = Path.GetDirectoryName(Path.GetFullPath(sciezka));
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        File.WriteAllText(sciezka, sb.ToString(), Encoding.UTF8);
    }

    // ===================== plan =====================

    /// <summary>Plan przeniesien dla odrzuconych pozycji. `cel(p)` mowi, skad liczyc sciezke wzgledna i dokad przeniesc
    /// (null = zrodla nie ma na dysku -> pliki pozycji w stanie Brak). Files uzywane przez pozycje, ktore zostaja,
    /// dostaja stan Wspoldzielony; wpisy z archiwow (sciezka z '|') — InArchiveCount.</summary>

    public ApplyPlan Plan(Catalog katalog, IEnumerable<string> odrzucone, Func<Garment, BinTarget?> cel)
    {
        var plan = new ApplyPlan();
        var wgId = katalog.Garments.Where(g => g.Id != null).ToDictionary(g => g.Id!);
        var odrzucane = new HashSet<string>(odrzucone.Where(wgId.ContainsKey));
        if (odrzucane.Count == 0) return plan;

        // Files uzywane przez pozycje, KTORE ZOSTAJA. Wszystko z tej listy jest nietykalne.
        var chronione = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in katalog.Garments.Where(g => g.Id != null && !odrzucane.Contains(g.Id)))
        {
            if (p.ModelPath != null) chronione.Add(p.ModelPath);
            foreach (var t in p.Textures) if (t.Path != null) chronione.Add(t.Path);
        }

        // ten sam plik moze byc w dwoch odrzucanych pozycjach (feet_050 i feet_050_1 obie odrzucone) — przenosimy raz
        var zaplanowane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brakZrodel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kolejnosc = odrzucane.Select(id => wgId[id]).OrderBy(p => p.PackName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Slot, StringComparer.Ordinal).ThenBy(p => p.Number).ThenBy(p => p.Suffix, StringComparer.Ordinal);
        foreach (var p in kolejnosc)
        {
            var c = cel?.Invoke(p);
            var pp = new PlannedGarment
            {
                Id = p.Id!, Name = $"{p.Slot}_{p.Number:d3}", Suffix = p.Suffix, Container = p.Container ?? "",
                SourceName = c?.SourceName ?? p.PackName ?? "", SourceId = c?.SourceId ?? p.SourceId ?? "",
                BinFolder = c?.BinFolder ?? "",
            };
            if (c == null) brakZrodel.Add(pp.SourceName);

            var pliki = new List<(string sciezka, long bajty)>();
            if (p.ModelPath != null) pliki.Add((p.ModelPath, p.ModelSize));
            pliki.AddRange(p.Textures.Where(t => t.Path != null).Select(t => (t.Path!, t.Size)));
            foreach (var (sciezka, bajty) in pliki.DistinctBy(x => x.sciezka, StringComparer.OrdinalIgnoreCase))
            {
                var r = new FileMove { GarmentId = p.Id!, From = sciezka, Bytes = bajty };
                if (sciezka.Contains('|')) r.State = FileMoveState.InArchive;
                else if (chronione.Contains(sciezka)) r.State = FileMoveState.Shared;
                else if (c == null || !File.Exists(sciezka)) r.State = FileMoveState.Missing;
                else if (!zaplanowane.Add(sciezka)) continue;   // juz w planie innej odrzucanej pozycji
                else { r.State = FileMoveState.Move; r.To = Path.Combine(c.BinFolder, RelativeTo(c.Root, sciezka)); }
                pp.Files.Add(r);
            }
            plan.Pozycje.Add(pp);
        }
        plan.MissingSources.AddRange(brakZrodel.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    /// <summary>Sciezka wzgledem korzenia zrodla (zachowujemy uklad kontenerow w koszu); plik spoza korzenia -> sama nazwa.</summary>
    public static string RelativeTo(string korzen, string plik)
    {
        if (string.IsNullOrEmpty(korzen)) return Path.GetFileName(plik);
        try
        {
            var pelnyKorzen = Path.GetFullPath(korzen).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pelny = Path.GetFullPath(plik);
            // the root is a file (an archive): measure from the folder it sits in
            if (File.Exists(pelnyKorzen)) pelnyKorzen = Path.GetDirectoryName(pelnyKorzen) ?? pelnyKorzen;
            var wzgl = Path.GetRelativePath(pelnyKorzen, pelny);
            if (wzgl.StartsWith("..") || Path.IsPathRooted(wzgl)) return Path.GetFileName(plik);
            return wzgl;
        }
        catch { return Path.GetFileName(plik); }
    }

    // ===================== wykonanie =====================

    /// <summary>Przenosi pliki w stanie Przenies. Nie rzuca przy anulowaniu — konczy petle i ustawia Przerwano, zeby wolajacy
    /// ZAWSZE mogl zapisac cofke z tym, co juz sie przenioslo. Wyjatek IO przy pojedynczym pliku tez przerywa (Blad).</summary>

}
