#nullable enable
using System;
using Duble.Core.Calibration;
using Duble.Core.Model;

namespace Duble.Cli;

/// <summary>`duble kalibruj`: runs a calibration and prints the distributions and the proposal it produced.</summary>
public static class CalibrationReportPrinter
{
    public static int Run(ICalibrator calibrator, Catalog catalog, Action<string> log)
    {
        if (catalog.Garments.Count == 0)
        {
            log("[blad] pusty katalog — najpierw `duble indeks`");
            return 1;
        }

        var report = calibrator.Run(catalog);
        Print(report, log);
        return 0;
    }

    static void Print(CalibrationReport w, Action<string> log)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        log($"pozycji z geometria: {w.GarmentsWithGeometry} / {w.Garments}");
        log($"  pary identyczne co do bajtu       : {w.GeoSameFile?.Text()}");
        log($"  pary o tym samym hashu pozycji    : {w.GeoSameHash?.Text()}");
        log($"  NAJBLIZSZY OBCY MESH (na pozycje) : {w.GeoNearestForeign?.Text()}");
        log($"  par 'ten sam mesh' miedzy paczkami: {w.GeoPairsAcrossPacks}");
        log("");
        log($"  --- pary o ROZNYM meshu, a odlegloscia < 0,05: {w.GeoSuspicious} ---");
        foreach (var pair in w.Suspicious)
            log($"    d={pair.D.ToString("F4", invariant)} bbox={pair.Bbox.ToString("F3", invariant)}  {pair.A} ({pair.TriA} tri)  vs  {pair.B} ({pair.TriB} tri)");

        log("");
        log($"tekstur zdekodowanych: {w.DecodedTextures} / {w.Textures}");
        log($"  PHash: pliki identyczne  : {w.HashIdentical?.Text("F1")}");
        log($"  PHash: warianty koloru   : {w.HashVariants?.Text("F1")}");
        log($"  PHash: pary losowe       : {w.HashRandom?.Text("F1")}");
        log($"  kolor: warianty koloru   : {w.ColorVariants?.Text("F2")}");
        log($"  kolor: pary losowe       : {w.ColorRandom?.Text("F2")}");
        log($"  wariancja jasnosci       : {w.Variance?.Text("F1")}");

        if (w.Proposal is { } proposal)
        {
            log("");
            log("propozycja progow (z tej kalibracji):");
            log($"  geometria — identyczna : dist <= {proposal.GeometryIdentical.ToString("F4", invariant)}   (1/3 najblizszego obcego mesha)");
            log($"  geometria — podobna    : dist <= {proposal.GeometrySimilar.ToString("F4", invariant)}");
            log($"  tekstury — PHash       : <= {proposal.TextureHashDistance}");
            log($"  tekstury — kolor       : <= {proposal.TextureColorDistance.ToString("F2", invariant)}");
        }
    }
}
