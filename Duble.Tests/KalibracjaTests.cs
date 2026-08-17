using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Duble.Tests;

/// <summary>Kalibracja.Policz na sztucznym katalogu (Sztuczne.Siedem): rozklady, histogramy, propozycja progow, anulowanie.</summary>
public class KalibracjaTests
{
    [Fact]
    public void Rozklad_liczy_percentyle_i_histogram()
    {
        var r = Rozklad.Z(new double[] { 0, 0.1, 0.2, 0.3, 0.4, 5.0 }, 0, 0.5, 5);
        Assert.Equal(6, r.N); Assert.Equal(0, r.Min); Assert.Equal(5.0, r.Max); Assert.Equal(0.3, r.P50);
        Assert.Equal(new[] { 1, 1, 1, 1, 2 }, r.Kubelki);   // 5.0 laduje w ostatnim kubelku (ponad zakres)
        Assert.Equal(6, r.Kubelki.Sum());
        var pusty = Rozklad.Z(Array.Empty<double>(), 0, 1, 4);
        Assert.Equal(0, pusty.N); Assert.Equal(4, pusty.Kubelki.Length); Assert.Equal("brak danych", pusty.Tekst());
        Assert.Contains("n=6", r.Tekst("F1"));
    }

    [Fact]
    public void Policz_na_sztucznym_katalogu()
    {
        var tmp = Sciezki.Tymczasowy("kalib");
        try
        {
            var kat = new Catalog(); kat.Upsert(Sztuczne.Siedem(tmp));
            var w = Kalibracja.Policz(kat);
            Assert.Equal(7, w.Pozycje); Assert.Equal(7, w.PozycjeZGeometria);
            Assert.Equal(9, w.Tekstury); Assert.Equal(9, w.TeksturyZdekodowane);
            // ten sam mesh (H1: a=b, H3: c=d, H5: e=f=g) -> pary o tym samym hashu: 1 + 1 + 3 = 5; brak identycznych plikow ydd (SHA null == null? -> ShaYdd null u obu = "identyczne")
            Assert.True(w.GeoTenSamHash.N + w.GeoIdentyczneSha.N >= 5);
            Assert.Equal(7, w.GeoNajblizszyObcy.N);
            Assert.All(new[] { w.GeoNajblizszyObcy, w.PHashWarianty, w.PHashLosowe, w.KolorLosowe }, r => Assert.Equal(r.N, r.Kubelki.Sum()));
            // identyczne SHA tekstur: a/b (2 pary: S1p11=S1p11? Sha = sha+paczka+numer ujednolicone dla par a/b -> 2 pary), e/f/g (3 pary) -> 5
            Assert.Equal(5, w.PHashIdentyczne.N); Assert.Equal(0, w.PHashIdentyczne.Max);
            // warianty koloru: a ma 2 tekstury (S1,S2 rozne sha) -> 1 para; b tak samo -> 1; reszta po 1 teksturze -> 2 pary
            Assert.Equal(2, w.PHashWarianty.N);
            Assert.True(w.PHashLosowe.N > 0);
            Assert.NotNull(w.Propozycja);
            Assert.InRange(w.Propozycja.GeoIdentyczna, 0, 1); Assert.True(w.Propozycja.GeoPodobna >= w.Propozycja.GeoIdentyczna);
            Assert.InRange(w.Propozycja.TexPHash, 4, 256); Assert.True(w.Propozycja.TexKolor >= 0);
            Assert.Empty(w.Propozycja.Sprawdz());
            Assert.Equal(Progi.Domyslne.TexPHash, w.Progi.TexPHash);
            Assert.False(string.IsNullOrEmpty(w.Kiedy));

            // wydruk CLI dziala i zawiera propozycje
            var log = new System.Collections.Generic.List<string>();
            Assert.Equal(0, Kalibracja.Uruchom(kat, log.Add));
            Assert.Contains(log, l => l.Contains("PROPOZYCJA PROGOW"));
            Assert.Contains(log, l => l.Contains("NAJBLIZSZY OBCY MESH"));

            // anulowanie
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Kalibracja.Policz(kat, null, cts.Token));
        }
        finally { Directory.Delete(tmp, true); }
    }
}
