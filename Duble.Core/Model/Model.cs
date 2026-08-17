// Model.cs — struktura katalogu garderoby: co wiemy o kazdym ciuchu.
//
// JEDNOSTKA to POZYCJA, czyli jeden ciuch: model .ydd + WSZYSTKIE jego tekstury .ytd
// (warianty kolorystyczne to litery a/b/c... tego samego numeru). Deduplikacja na
// poziomie pojedynczych plikow nie mialaby sensu — o tym, czy dwa ciuchy sa te same,
// decyduje dopiero PARA (geometria, zbior tekstur).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duble.Core.Model;

/// <summary>Odcisk geometrii modelu — liczby, ktore przezywaja ponowny eksport.</summary>
public class Geo
{
    public int Wierzcholki { get; set; }
    public int Trojkaty { get; set; }
    public int Geometrie { get; set; }
    public int Lody { get; set; }          // ile poziomow LOD (High/Med/Low/VLow)
    public int Kosci { get; set; }
    public int Stride { get; set; }        // rozmiar wierzcholka w bajtach (48/64/72)
    public float[] Bbox { get; set; }      // wymiary pudelka: X, Y, Z (w metrach)

    /// <summary>
    /// Histogram odleglosci wierzcholkow od srodka ciezkosci, znormalizowany srednia
    /// odlegloscia. NIEZALEZNY od kolejnosci wierzcholkow i od skali — a wiec przezywa
    /// ponowny eksport, ktory zawsze przetasowuje bufor wierzcholkow.
    /// </summary>
    public float[] Hist { get; set; }

    /// <summary>
    /// Hash z posortowanych pozycji zaokraglonych do 1 mm. Rowny = ten sam mesh
    /// z dokladnoscia do przetasowania wierzcholkow. Sygnal mocniejszy niz histogram,
    /// ale kruchy: dowolne przeskalowanie/przesuniecie go zrywa.
    /// </summary>
    public string HashPozycji { get; set; }

    public const int Kubelki = 64;
    public const float ZakresHist = 2.5f;   // histogram obejmuje 0..2,5 sredniej odleglosci
}

/// <summary>Odcisk jednej tekstury.</summary>
public class Tekstura
{
    public string Plik { get; set; }        // nazwa pliku .ytd

    /// <summary>Skad wziac plik przy budowie raportu. Dla luznych plikow zwykla sciezka,
    /// dla wpisu w archiwum: "sciezka\do\archiwum.rpf|sciezka\wewnetrzna".</summary>
    public string Sciezka { get; set; }

    /// <summary>Znacznik zmiany pliku (rozmiar|data; dla wpisu z archiwum takze rozmiar i data archiwum) —
    /// indeksowanie przyrostowe pomija pliki, ktorych znacznik sie nie zmienil.</summary>
    public string Znacznik { get; set; }

    public string Nazwa { get; set; }       // nazwa tekstury w srodku slownika
    public string Sha { get; set; }         // SHA-256 calego pliku .ytd
    public long Bajty { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int Mipy { get; set; }
    public string Format { get; set; }      // BC1_UNORM / BC3_UNORM / BC7_UNORM / ...

    /// <summary>
    /// Perceptualny hash 256-bitowy (DCT 16x16 na obrazie 64x64 w skali szarosci),
    /// w czterech slowach. NIEZALEZNY od rozdzielczosci i kompresji — 1024^2 i 2048^2
    /// tej samej grafiki daja ten sam odcisk.
    ///
    /// DLACZEGO 256, A NIE 64 BITY: kalibracja 15.08 na 9437 teksturach pokazala, ze przy
    /// 64 bitach warianty KOLORYSTYCZNE tego samego ciucha maja p05 = 0 roznicy (w skali
    /// szarosci sa nierozroznialne), a losowe pary schodza do 0 — czyli prog nie istnial.
    /// Tekstury ubran to atlasy z duza iloscia pustego tla, wiec redukcja do 32x32 gubila
    /// za duzo. null = nie udalo sie zdekodowac (BC7).
    /// </summary>
    public ulong[] PHash { get; set; }

    /// <summary>Czy udalo sie zdekodowac piksele. Gdy false, PHash/Kolor/Alfa sa bez wartosci.</summary>
    public bool Zdekodowana { get; set; }

    /// <summary>Sygnatura koloru: siatka 8x8, po 3 bajty RGB = 192 bajty, w base64.
    /// KONIECZNA — w skali szarosci dwa kolory tej samej sukienki maja identyczny PHash.</summary>
    public string Kolor { get; set; }

    /// <summary>Odchylenie standardowe jasnosci. Przy plaskiej teksturze (male) bity PHasha
    /// pochodza z szumu i nie wolno im ufac — wtedy rozstrzyga sam kolor.</summary>
    public float Wariancja { get; set; }

    /// <summary>Udzial pikseli z alfa &lt; 250. Gdy &gt; 0, kompresja BC1 (alfa 1-bitowa) jest strata.</summary>
    public float Alfa { get; set; }

    [JsonIgnore] public long Piksele => (long)W * H;
}

/// <summary>Jeden ciuch: model + jego tekstury.</summary>
public class Pozycja
{
    public string Id { get; set; }          // paczka|kontener|typ|numer
    public string Paczka { get; set; }
    public string Kontener { get; set; }    // np. civil01_female.rpf
    public string Typ { get; set; }         // jbib / hair / feet ... albo p_head dla propsow
    public int Numer { get; set; }

    /// <summary>"u" (uniwersalny) / "r" (rasowy), ewentualnie z ogonkiem eksportera: "u_1".</summary>
    public string Sufiks { get; set; } = "u";

    public bool Props { get; set; }
    public bool Gen9 { get; set; }

    public string SciezkaYdd { get; set; }
    /// <summary>Znacznik zmiany pliku ydd (jak Tekstura.Znacznik).</summary>
    public string Znacznik { get; set; }
    /// <summary>Id zrodla w projekcie aplikacji (ZrodloProjektu.Id); CLI zostawia null.</summary>
    public string ZrodloId { get; set; }
    public long BajtyYdd { get; set; }
    public string ShaYdd { get; set; }
    public Geo Geo { get; set; }
    public List<Tekstura> Tekstury { get; set; } = new();

    /// <summary>Krotki, czytelny opis do raportu.</summary>
    [JsonIgnore] public string Opis => $"{Paczka} / {Typ}_{Numer:d3}";
}

public class Katalog
{
    public int Wersja { get; set; } = 2;   // 2 = znaczniki plikow + ZrodloId (16.08)
    public string Zbudowany { get; set; }

    /// <summary>Nazwa paczki -> folder albo archiwum, z ktorego ja wzielismy.
    /// Dzieki temu `duble odswiez` wie, co przeindeksowac, bez podawania sciezek.</summary>
    public Dictionary<string, string> Zrodla { get; set; } = new();

    public List<Pozycja> Pozycje { get; set; } = new();

    static readonly JsonSerializerOptions Opcje = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Katalog Wczytaj(string sciezka)
    {
        if (!File.Exists(sciezka)) return new Katalog();
        return JsonSerializer.Deserialize<Katalog>(File.ReadAllText(sciezka), Opcje) ?? new Katalog();
    }

    public void Zapisz(string sciezka)
    {
        var kat = Path.GetDirectoryName(Path.GetFullPath(sciezka));
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        File.WriteAllText(sciezka, JsonSerializer.Serialize(this, Opcje));
    }

    /// <summary>Wstawia albo podmienia pozycje o tym samym Id (ponowne indeksowanie zrodla).</summary>
    public void Wstaw(IEnumerable<Pozycja> nowe)
    {
        var wg = Pozycje.ToDictionary(p => p.Id);
        foreach (var p in nowe) wg[p.Id] = p;
        Pozycje = wg.Values.OrderBy(p => p.Paczka).ThenBy(p => p.Typ).ThenBy(p => p.Numer).ToList();
    }

    public void UsunPaczke(string paczka)
        => Pozycje = Pozycje.Where(p => !string.Equals(p.Paczka, paczka, StringComparison.OrdinalIgnoreCase)).ToList();
}
