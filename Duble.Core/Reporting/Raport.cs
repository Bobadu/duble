// Raport.cs — viewer porownan: zwykly, samowystarczalny plik HTML.
//
// Miniatury sa wpisane w plik jako data:image/png;base64, wiec raport dziala po
// skopiowaniu gdziekolwiek i bez internetu. Tekstury dekodujemy PONOWNIE ze zrodel
// (katalog trzyma tylko odciski, nie obrazy) — dlatego kazda tekstura zna swoja sciezke.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using CodeWalker.GameFiles;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Model;
using Duble.Core.Fingerprints;
using Duble.Core.Formats;
using Duble.Core.Sources;

namespace Duble.Core.Reporting;

public static class Raport
{
    const int Bok = 96;              // bok miniatury w pikselach
    const int MaxWierszy = 12;       // ile par tekstur pokazujemy na grupe
    const int MaxUnikalnych = 8;     // ile tekstur "tylko tutaj" na czlonka

    static readonly Dictionary<string, string> CacheMiniatur = new();
    static int bezPodgladu, bezPliku;

    /// <summary>Tekst raportu w jezyku (klucze raport.* z i18n Core).</summary>
    static string Tx(string jezyk, string klucz, params (string k, object v)[] p)
    {
        if (p == null || p.Length == 0) return Teksty.T(jezyk, klucz);
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in p) d[k] = Convert.ToString(v, CultureInfo.InvariantCulture);
        return Teksty.T(jezyk, klucz, d);
    }

    /// <summary>Samowystarczalny raport HTML. `rozstrzygnij` (aplikacja: decyzje uzytkownika) mowi, kto zostaje / jest odrzucony /
    /// zignorowany; brak = domyslne z porownania. `tytul` = nazwa projektu (naglowek strony).</summary>
    public static void Zbuduj(Katalog katalog, WynikPorownania wynik, string plik, Action<string> log, string jezyk = "pl",
                              Func<Grupa, Rozstrzygniecie> rozstrzygnij = null, string tytul = null)
    {
        log ??= _ => { };
        rozstrzygnij ??= g => Rozstrzygniecie.Policz(g, null);
        var wgId = katalog.Pozycje.ToDictionary(p => p.Id);
        var kolejnosc = new Dictionary<string, int>
        {
            [Porownanie.Duplikat] = 0, [Porownanie.Nadzbior] = 1,
            [Porownanie.DoWgladu] = 2, [Porownanie.Przemalowanie] = 3
        };
        var grupy = wynik.Grupy
            .Where(g => g.Pozycje.All(wgId.ContainsKey))
            .OrderBy(g => kolejnosc.TryGetValue(g.Werdykt, out var k) ? k : 9)
            .ThenByDescending(g => g.Pozycje.Count)
            .ToList();
        var rozstrzygniecia = grupy.ToDictionary(g => g, g => rozstrzygnij(g));

        long doOdzyskania = 0; int doOdrzucenia = 0;
        foreach (var g in grupy)
        {
            var r = rozstrzygniecia[g];
            if (r.Ignoruj) continue;
            foreach (var id in r.Odrzucone.Where(wgId.ContainsKey))
            {
                doOdrzucenia++;
                doOdzyskania += wgId[id].BajtyYdd + wgId[id].Tekstury.Sum(t => t.Bajty);
            }
        }

        var sb = new StringBuilder();
        sb.Append(Naglowek(katalog, wynik, grupy, doOdrzucenia, doOdzyskania, jezyk, tytul));

        int zrobione = 0;
        foreach (var g in grupy)
        {
            sb.Append(Karta(g, wgId, jezyk, rozstrzygniecia[g]));
            if (++zrobione % 10 == 0) log($"  grup: {zrobione}/{grupy.Count}");
        }

        sb.Append($"""
        </main>
        <footer>
          <p>{E(Tx(jezyk, "raport.stopka"))}</p>
        </footer>
        """);
        sb.Append("""
        <script>
        const przyciski = document.querySelectorAll('[data-filtr]');
        const szukaj = document.getElementById('szukaj');
        let aktywne = new Set();
        function odswiez() {
          const fraza = (szukaj.value || '').toLowerCase().trim();
          let widocznych = 0;
          document.querySelectorAll('article.grupa').forEach(el => {
            const pasujeWerdykt = aktywne.size === 0 || aktywne.has(el.dataset.werdykt);
            const pasujeFraza = !fraza || el.dataset.szukaj.includes(fraza);
            const ok = pasujeWerdykt && pasujeFraza;
            el.hidden = !ok;
            if (ok) widocznych++;
          });
          document.getElementById('licznik').textContent = widocznych;
        }
        przyciski.forEach(b => b.addEventListener('click', () => {
          const w = b.dataset.filtr;
          if (aktywne.has(w)) { aktywne.delete(w); b.setAttribute('aria-pressed','false'); }
          else { aktywne.add(w); b.setAttribute('aria-pressed','true'); }
          odswiez();
        }));
        szukaj.addEventListener('input', odswiez);
        const motyw = document.getElementById('motyw');
        motyw.addEventListener('click', () => {
          const teraz = document.documentElement.getAttribute('data-theme');
          const jasny = teraz === 'light' || (!teraz && !window.matchMedia('(prefers-color-scheme: dark)').matches);
          document.documentElement.setAttribute('data-theme', jasny ? 'dark' : 'light');
        });
        odswiez();
        </script>
        </body>
        </html>
        """);

        var kat = Path.GetDirectoryName(Path.GetFullPath(plik));
        if (!string.IsNullOrEmpty(kat)) Directory.CreateDirectory(kat);
        File.WriteAllText(plik, sb.ToString(), Encoding.UTF8);

        log($"  grup w raporcie: {grupy.Count}, miniatur: {CacheMiniatur.Count}"
          + (bezPodgladu > 0 ? $", bez podgladu (BC7): {bezPodgladu}" : "")
          + (bezPliku > 0 ? $", NIE ZNALAZLAM PLIKU: {bezPliku}" : ""));
        log($"  rozmiar pliku: {new FileInfo(plik).Length / 1024.0 / 1024.0:F1} MB");
    }

    // ===================== miniatury =====================

    static string Miniatura(Tekstura t, bool gen9)
    {
        if (t?.Sciezka == null) { bezPliku++; return null; }
        if (CacheMiniatur.TryGetValue(t.Sha, out var gotowa)) return gotowa;
        var bajty = Zrodla.Bajty(t.Sciezka);
        if (bajty == null) { bezPliku++; return null; }
        try
        {
            Format.Przygotuj();   // tryb gen9 czyta oba formaty po naglowku RSC7 (parametr gen9 zostal dla zgodnosci)
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, bajty, 13);
            var tex = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            var rgb = tex == null ? null : Odciski.Miniatura(tex, Bok);
            if (rgb == null) { bezPodgladu++; return null; }
            var uri = "data:image/png;base64," + Convert.ToBase64String(Png.Rgb(rgb, Bok, Bok));
            CacheMiniatur[t.Sha] = uri;
            return uri;
        }
        catch { bezPodgladu++; return null; }
    }

    static string Kafelek(Tekstura t, bool gen9, string jezyk, string etykieta = null)
    {
        if (t == null)
            return $"<div class=\"kafelek pusty\"><div class=\"placeholder\">{E(Tx(jezyk, "raport.brakOdpowiednika")).Replace(" ", "<br>")}</div></div>";
        var uri = Miniatura(t, gen9);
        var obraz = uri != null
            ? $"<img src=\"{uri}\" alt=\"{E(t.Plik)}\" loading=\"lazy\" width=\"{Bok}\" height=\"{Bok}\">"
            : $"<div class=\"placeholder\">{E(t.Format)}<br>{E(Tx(jezyk, "raport.bezPodgladu"))}</div>";
        var znaczniki = new List<string> { $"{t.W}×{t.H}", E(t.Format) };
        if (t.Mipy <= 1) znaczniki.Add($"<span class=\"zle\">{E(Tx(jezyk, "raport.bezMipow"))}</span>");
        if (t.Format == "BC1" && t.Alfa > 0.02f) znaczniki.Add($"<span class=\"zle\">{E(Tx(jezyk, "raport.bc1Alfa"))}</span>");
        return $"""
            <div class="kafelek">
              {obraz}
              <div class="nazwa" title="{E(t.Plik)}">{E(etykieta ?? t.Plik)}</div>
              <div class="meta">{string.Join(" · ", znaczniki)}</div>
            </div>
            """;
    }

    // ===================== karta grupy =====================

    static string Karta(Grupa g, Dictionary<string, Pozycja> wgId, string jezyk, Rozstrzygniecie roz)
    {
        roz ??= Rozstrzygniecie.Policz(g, null);
        var zwyciezca = roz.Zwyciezca ?? g.Zwyciezca;
        var czlonkowie = g.Pozycje.OrderByDescending(id => id == zwyciezca ? 1 : 0)
                                   .ThenByDescending(id => g.Punkty.TryGetValue(id, out var p) ? p : 0)
                                   .ToList();
        var wzorzec = wgId[czlonkowie[0]];
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        var szukaj = string.Join(" ", czlonkowie.Select(id => wgId[id].Opis)).ToLowerInvariant();
        sb.Append($"<article class=\"grupa {Klasa(g.Werdykt)}\" data-werdykt=\"{E(g.Werdykt)}\" data-szukaj=\"{E(szukaj)}\">");

        // --- naglowek ---
        sb.Append("<header class=\"glowa\">");
        sb.Append($"<span class=\"odznaka {Klasa(g.Werdykt)}\">{E(Teksty.T(jezyk, "werdykt." + g.Werdykt))}</span>");
        if (roz.Ignoruj) sb.Append($" <span class=\"odznaka w-inne\">{E(Tx(jezyk, "raport.zignorowana"))}</span>");
        else if (!roz.Domyslna) sb.Append($" <span class=\"odznaka w-inne\">{E(Tx(jezyk, "raport.twojaDecyzja"))}</span>");
        sb.Append($"<h2>{string.Join(" <span class=\"rowna\">=</span> ", czlonkowie.Select(id => $"<span class=\"tytul\">{E(wgId[id].Opis)}<sub>{E(wgId[id].Sufiks)}</sub></span>"))}</h2>");
        sb.Append($"<p class=\"powod\">{E(Teksty.Powod(g.Pary.FirstOrDefault()?.Powod ?? g.Powod, jezyk))}</p>");
        if (!string.IsNullOrWhiteSpace(roz.Notatka)) sb.Append($"<p class=\"powod notatka\">{E(Tx(jezyk, "raport.notatka"))}: {E(roz.Notatka)}</p>");
        sb.Append("</header>");

        // --- panele pozycji ---
        sb.Append("<div class=\"panele\">");
        foreach (var id in czlonkowie)
        {
            var p = wgId[id];
            bool wygrywa = !roz.Ignoruj && id == zwyciezca && roz.Odrzucone.Count > 0;
            bool odrzut = !roz.Ignoruj && roz.Odrzucone.Contains(id);
            string stan = wygrywa ? "wygrywa" : odrzut ? "odrzut" : "";
            sb.Append($"<section class=\"panel {stan}\">");
            sb.Append("<div class=\"panel-glowa\">");
            sb.Append($"<span class=\"paczka\">{E(p.Paczka)}</span>");
            if (wygrywa) sb.Append($"<span class=\"stan wygrywa\">{E(Tx(jezyk, "raport.zostaje"))}</span>");
            else if (odrzut) sb.Append($"<span class=\"stan odrzut\">{E(Tx(jezyk, "raport.doOdrzucenia"))}</span>");
            sb.Append("</div>");
            sb.Append($"<div class=\"nazwa-poz\">{E(p.Typ)}_{p.Numer:d3}<sub>{E(p.Sufiks)}</sub></div>");
            if (g.Punkty.TryGetValue(id, out var pkt))
            {
                sb.Append($"<div class=\"punkty\"><b>{pkt.ToString("F0", inv)}</b><span>{E(Tx(jezyk, "raport.pktJakosci"))}</span></div>");
                if (g.Rozpiska.TryGetValue(id, out var r))
                    sb.Append($"<div class=\"rozpiska\">{E(r.Tekst(jezyk))}</div>");
            }
            var med = p.Tekstury.Count > 0
                ? p.Tekstury.OrderBy(t => (long)t.W * t.H).ElementAt(p.Tekstury.Count / 2)
                : null;
            sb.Append("<ul class=\"znaczniki\">");
            sb.Append($"<li>{E(Tx(jezyk, "raport.tekstur", ("n", p.Tekstury.Count)))}</li>");
            if (med != null) sb.Append($"<li>{med.W}×{med.H}</li>");
            sb.Append($"<li>{(p.Geo?.Trojkaty ?? 0).ToString("N0", inv)} tri</li>");
            sb.Append($"<li>LOD {p.Geo?.Lody ?? 0}</li>");
            int bezMip = p.Tekstury.Count(t => t.Mipy <= 1);
            if (bezMip > 0) sb.Append($"<li class=\"zle\">{E(Tx(jezyk, "raport.nBezMipow", ("n", bezMip)))}</li>");
            sb.Append("</ul>");
            sb.Append("</section>");
        }
        sb.Append("</div>");

        // --- porownanie tekstur ---
        sb.Append("<div class=\"tekstury\">");
        sb.Append($"<h3>{E(Tx(jezyk, "raport.teksturyObok"))}</h3>");
        var uzyte = czlonkowie.ToDictionary(id => id, id => new HashSet<int>());
        int wierszy = 0, pominietych = 0;
        sb.Append("<div class=\"siatka\" style=\"--kolumn:" + czlonkowie.Count + "\">");
        for (int w = 0; w < wzorzec.Tekstury.Count; w++)
        {
            var wz = wzorzec.Tekstury[w];
            // Dopasowanie liczymy ZAWSZE, nawet gdy wiersza juz nie rysujemy — inaczej
            // nadmiarowe tekstury wzorca wyladowalyby falszywie w sekcji "tylko tutaj".
            uzyte[czlonkowie[0]].Add(w);
            var trafienia = new int[czlonkowie.Count];
            for (int i = 1; i < czlonkowie.Count; i++)
            {
                var inny = wgId[czlonkowie[i]];
                trafienia[i] = -1;
                for (int k = 0; k < inny.Tekstury.Count; k++)
                {
                    if (uzyte[czlonkowie[i]].Contains(k)) continue;
                    if (Porownanie.TaSamaGrafika(wz, inny.Tekstury[k])) { trafienia[i] = k; break; }
                }
                if (trafienia[i] >= 0) uzyte[czlonkowie[i]].Add(trafienia[i]);
            }
            if (wierszy >= MaxWierszy) { pominietych++; continue; }
            wierszy++;
            sb.Append("<div class=\"wiersz\">");
            sb.Append(Kafelek(wz, wzorzec.Gen9, jezyk));
            for (int i = 1; i < czlonkowie.Count; i++)
            {
                var inny = wgId[czlonkowie[i]];
                sb.Append(Kafelek(trafienia[i] >= 0 ? inny.Tekstury[trafienia[i]] : null, inny.Gen9, jezyk));
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");
        if (pominietych > 0)
            sb.Append($"<p class=\"uwaga\">{E(Tx(jezyk, "raport.pokazane", ("n", MaxWierszy), ("m", wzorzec.Tekstury.Count), ("r", pominietych)))}</p>");

        // --- tekstury wystepujace tylko u jednego ---
        foreach (var id in czlonkowie)
        {
            var p = wgId[id];
            var unikalne = Enumerable.Range(0, p.Tekstury.Count).Where(k => !uzyte[id].Contains(k)).ToList();
            if (unikalne.Count == 0) continue;
            bool stracisz = !roz.Ignoruj && roz.Odrzucone.Contains(id);
            sb.Append($"<h4>{E(Tx(jezyk, "raport.tylkoW"))} <em>{E(p.Opis)}</em> — {E(Tx(jezyk, "raport.tekstur", ("n", unikalne.Count)))}{(stracisz ? $" <span class=\"zle\">{E(Tx(jezyk, "raport.stracisz"))}</span>" : "")}</h4>");
            sb.Append("<div class=\"pasek\">");
            foreach (var k in unikalne.Take(MaxUnikalnych)) sb.Append(Kafelek(p.Tekstury[k], p.Gen9, jezyk));
            sb.Append("</div>");
            if (unikalne.Count > MaxUnikalnych)
                sb.Append($"<p class=\"uwaga\">{E(Tx(jezyk, "raport.dalszych", ("n", unikalne.Count - MaxUnikalnych)))}</p>");
        }
        sb.Append("</div></article>");
        return sb.ToString();
    }

    // ===================== CSV =====================

    /// <summary>Tabela grup i decyzji: jeden wiersz na czlonka grupy. Separator `;` dla PL (Excel PL), `,` dla EN; UTF-8 z BOM.</summary>
    public static string Csv(Katalog katalog, WynikPorownania wynik, Func<Grupa, Rozstrzygniecie> rozstrzygnij = null, string jezyk = "pl")
    {
        rozstrzygnij ??= g => Rozstrzygniecie.Policz(g, null);
        var wgId = katalog.Pozycje.ToDictionary(p => p.Id);
        var sep = Teksty.T(jezyk, "raport.csv.separator");
        if (sep.Length != 1) sep = ";";
        var inv = CultureInfo.InvariantCulture;
        string Pole(object v)
        {
            var s = Convert.ToString(v, inv) ?? "";
            return s.Contains(sep) || s.Contains('"') || s.Contains('\n') || s.Contains('\r') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var kolumny = new[] { "grupa", "werdykt", "powod", "pozycja", "sufiks", "zrodlo", "kontener", "plik", "punkty", "stan", "notatka", "wierzcholki", "trojkaty", "tekstur", "bajty" };
        var sb = new StringBuilder();
        sb.Append('\uFEFF');   // BOM: Excel rozpoznaje UTF-8
        sb.AppendLine(string.Join(sep, kolumny.Select(k => Pole(Teksty.T(jezyk, "raport.csv." + k)))));
        int nr = 0;
        foreach (var g in wynik.Grupy.Where(g => g.Pozycje != null && g.Pozycje.All(wgId.ContainsKey)))
        {
            nr++;
            var r = rozstrzygnij(g);
            var powod = Teksty.Powod(g.Pary.FirstOrDefault()?.Powod ?? g.Powod, jezyk);
            foreach (var id in g.Pozycje)
            {
                var p = wgId[id];
                var stan = r.Ignoruj ? "zignorowana" : (r.Zwyciezca == id && r.Odrzucone.Count > 0) ? "zostaje" : r.Odrzucone.Contains(id) ? "odrzucona" : "bezZmian";
                var wiersz = new object[]
                {
                    nr, Teksty.T(jezyk, "werdykt." + g.Werdykt), powod, $"{p.Typ}_{p.Numer:d3}", p.Sufiks, p.Paczka, p.Kontener, p.SciezkaYdd,
                    g.Punkty.TryGetValue(id, out var pkt) ? pkt.ToString("F0", inv) : "", Teksty.T(jezyk, "raport.stan." + stan), r.Notatka ?? "",
                    p.Geo?.Wierzcholki ?? 0, p.Geo?.Trojkaty ?? 0, p.Tekstury.Count, p.BajtyYdd + p.Tekstury.Sum(t => t.Bajty),
                };
                sb.AppendLine(string.Join(sep, wiersz.Select(Pole)));
            }
        }
        return sb.ToString();
    }

    static string Klasa(string werdykt) => werdykt switch
    {
        Porownanie.Duplikat => "w-duplikat",
        Porownanie.Nadzbior => "w-nadzbior",
        Porownanie.DoWgladu => "w-wglad",
        Porownanie.Przemalowanie => "w-przemalowanie",
        _ => "w-inne"
    };

    static string E(string s) => WebUtility.HtmlEncode(s ?? "");

    // ===================== naglowek dokumentu =====================

    static string Naglowek(Katalog katalog, WynikPorownania wynik, List<Grupa> grupy, int doOdrzucenia, long doOdzyskania, string jezyk, string tytul)
    {
        int Ile(string w) => grupy.Count(g => g.Werdykt == w);
        var kultura = CultureInfo.InvariantCulture;
        string T(string k, params (string k, object v)[] p) => E(Tx(jezyk, k, p));
        string W(string w) => E(Teksty.T(jezyk, "werdykt." + w));
        var naglowek = string.IsNullOrWhiteSpace(tytul) ? Tx(jezyk, "raport.tytul") : Tx(jezyk, "raport.tytulProjektu", ("nazwa", tytul));
        var lang = Teksty.Jezyki.Contains((jezyk ?? "pl").ToLowerInvariant()) ? jezyk.ToLowerInvariant() : "pl";

        return $$"""
        <!doctype html>
        <html lang="{{lang}}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(naglowek)}}</title>
        <style>
        :root {
          --tlo:#f6f3ee; --karta:#fffdfa; --tekst:#1b1712; --cichy:#7a6f61;
          --linia:#e4ddd2; --akcent:#8a6a12; --pole:#efe9e0;
          --duplikat:#b3402f; --nadzbior:#96690f; --wglad:#3d6b8a; --przemalowanie:#457645;
          --zle:#b3402f;
        }
        @media (prefers-color-scheme: dark) {
          :root:not([data-theme="light"]) {
            --tlo:#14120e; --karta:#1e1a15; --tekst:#eae4d9; --cichy:#9b9182;
            --linia:#2f2921; --akcent:#d9b04a; --pole:#26221b;
            --duplikat:#e8705c; --nadzbior:#e0a83c; --wglad:#84b8d8; --przemalowanie:#8cc98c;
            --zle:#e8705c;
          }
        }
        :root[data-theme="dark"] {
          --tlo:#14120e; --karta:#1e1a15; --tekst:#eae4d9; --cichy:#9b9182;
          --linia:#2f2921; --akcent:#d9b04a; --pole:#26221b;
          --duplikat:#e8705c; --nadzbior:#e0a83c; --wglad:#84b8d8; --przemalowanie:#8cc98c;
          --zle:#e8705c;
        }
        * { box-sizing:border-box; }
        body {
          margin:0; background:var(--tlo); color:var(--tekst);
          font:15px/1.55 ui-sans-serif,"Segoe UI",system-ui,sans-serif;
          font-variant-numeric:tabular-nums;
        }
        code, .mono, .rozpiska, .meta, .znaczniki, .punkty, .nazwa {
          font-family:ui-monospace,"Cascadia Mono",Consolas,monospace;
        }
        header.strona {
          position:sticky; top:0; z-index:10; background:var(--karta);
          border-bottom:1px solid var(--linia); padding:14px 22px;
          display:flex; flex-wrap:wrap; gap:14px; align-items:center;
        }
        header.strona h1 {
          margin:0; font-size:17px; letter-spacing:.14em; text-transform:uppercase;
          color:var(--akcent); font-weight:650;
        }
        .chipy { display:flex; gap:7px; flex-wrap:wrap; }
        .chip {
          font-family:ui-monospace,Consolas,monospace; font-size:12px;
          padding:4px 9px; border:1px solid var(--linia); border-radius:2px;
          background:var(--pole); color:var(--cichy);
        }
        .chip b { color:var(--tekst); }
        button[data-filtr], #motyw {
          font:inherit; font-size:12px; font-family:ui-monospace,Consolas,monospace;
          padding:5px 11px; border:1px solid var(--linia); border-radius:2px;
          background:var(--karta); color:var(--tekst); cursor:pointer;
          letter-spacing:.05em;
        }
        button[data-filtr]:hover, #motyw:hover { border-color:var(--akcent); }
        button[data-filtr][aria-pressed="true"] { background:var(--akcent); color:var(--karta); border-color:var(--akcent); }
        button:focus-visible, input:focus-visible { outline:2px solid var(--akcent); outline-offset:2px; }
        #szukaj {
          font:inherit; font-size:13px; padding:5px 10px; min-width:190px;
          background:var(--pole); color:var(--tekst);
          border:1px solid var(--linia); border-radius:2px;
        }
        main { padding:20px 22px 60px; display:flex; flex-direction:column; gap:18px; }

        article.grupa {
          background:var(--karta); border:1px solid var(--linia); border-radius:3px;
          border-left:4px solid var(--linia); overflow:hidden;
        }
        article.w-duplikat { border-left-color:var(--duplikat); }
        article.w-nadzbior { border-left-color:var(--nadzbior); }
        article.w-wglad { border-left-color:var(--wglad); }
        article.w-przemalowanie { border-left-color:var(--przemalowanie); }

        .glowa { padding:14px 18px 12px; border-bottom:1px solid var(--linia); }
        .glowa h2 { margin:8px 0 4px; font-size:16px; font-weight:600; text-wrap:balance; }
        .tytul { font-family:ui-monospace,Consolas,monospace; }
        .tytul sub { color:var(--cichy); font-size:.75em; }
        .rowna { color:var(--cichy); margin:0 4px; }
        .powod { margin:0; color:var(--cichy); font-size:13.5px; }
        .odznaka {
          display:inline-block; font-family:ui-monospace,Consolas,monospace; font-size:11px;
          letter-spacing:.1em; padding:3px 8px; border-radius:2px; color:#fff;
        }
        .odznaka.w-duplikat { background:var(--duplikat); }
        .odznaka.w-nadzbior { background:var(--nadzbior); }
        .odznaka.w-wglad { background:var(--wglad); }
        .odznaka.w-przemalowanie { background:var(--przemalowanie); }
        .odznaka.w-inne { background:var(--cichy); }
        .powod.notatka { color:var(--tekst); font-style:italic; }

        .panele { display:grid; grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:1px; background:var(--linia); }
        .panel { background:var(--karta); padding:13px 16px; }
        .panel.wygrywa { background:color-mix(in srgb, var(--przemalowanie) 8%, var(--karta)); }
        .panel.odrzut { background:color-mix(in srgb, var(--duplikat) 7%, var(--karta)); }
        .panel-glowa { display:flex; justify-content:space-between; align-items:center; gap:8px; }
        .paczka { font-size:11.5px; letter-spacing:.07em; text-transform:uppercase; color:var(--cichy); }
        .stan { font-family:ui-monospace,Consolas,monospace; font-size:10.5px; letter-spacing:.08em; padding:2px 6px; border-radius:2px; }
        .stan.wygrywa { background:var(--przemalowanie); color:#fff; }
        .stan.odrzut { background:var(--duplikat); color:#fff; }
        .nazwa-poz { font-family:ui-monospace,Consolas,monospace; font-size:16px; margin:5px 0 7px; }
        .nazwa-poz sub { color:var(--cichy); font-size:.7em; }
        .punkty { display:flex; align-items:baseline; gap:5px; }
        .punkty b { font-size:23px; color:var(--akcent); }
        .punkty span { font-size:11px; color:var(--cichy); }
        .rozpiska { font-size:10.5px; color:var(--cichy); margin-top:4px; line-height:1.5; word-break:break-word; }
        .znaczniki { list-style:none; display:flex; flex-wrap:wrap; gap:5px; padding:0; margin:9px 0 0; }
        .znaczniki li { font-size:11px; padding:2px 7px; background:var(--pole); border-radius:2px; color:var(--cichy); }
        .znaczniki li.zle, .zle { color:var(--zle); }

        .tekstury { padding:14px 18px 18px; }
        .tekstury h3 {
          margin:0 0 11px; font-size:11px; letter-spacing:.12em; text-transform:uppercase;
          color:var(--cichy); font-weight:600;
        }
        .tekstury h4 { margin:16px 0 8px; font-size:12.5px; font-weight:600; }
        .tekstury h4 em { font-family:ui-monospace,Consolas,monospace; font-style:normal; }
        .siatka { overflow-x:auto; display:flex; flex-direction:column; gap:9px; }
        .wiersz { display:grid; grid-template-columns:repeat(var(--kolumn),minmax(110px,150px)); gap:9px; }
        .pasek { display:flex; gap:9px; flex-wrap:wrap; }
        .kafelek { width:110px; }
        .kafelek img, .placeholder {
          width:110px; height:110px; display:block; border-radius:2px;
          border:1px solid var(--linia); background:var(--pole); object-fit:cover;
          image-rendering:auto;
        }
        .placeholder {
          display:flex; align-items:center; justify-content:center; text-align:center;
          font-size:10px; color:var(--cichy); font-family:ui-monospace,Consolas,monospace;
        }
        .kafelek.pusty .placeholder { border-style:dashed; }
        .kafelek .nazwa { font-size:10px; margin-top:4px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
        .kafelek .meta { font-size:9.5px; color:var(--cichy); }
        .uwaga { font-size:11.5px; color:var(--cichy); margin:8px 0 0; }
        footer { padding:0 22px 40px; color:var(--cichy); font-size:12.5px; }
        </style>
        </head>
        <body>
        <header class="strona">
          <h1>{{E(naglowek)}}</h1>
          <div class="chipy">
            <span class="chip">{{T("raport.pozycjiWKatalogu")}} <b>{{katalog.Pozycje.Count}}</b></span>
            <span class="chip">{{T("raport.tekstury")}} <b>{{katalog.Pozycje.Sum(p => p.Tekstury.Count)}}</b></span>
            <span class="chip">{{T("raport.widocznychGrup")}} <b id="licznik">0</b></span>
            <span class="chip">{{T("raport.doOdrzuceniaN")}} <b>{{doOdrzucenia}}</b></span>
            <span class="chip">{{T("raport.odzyskaSie")}} <b>{{(doOdzyskania / 1024.0 / 1024.0).ToString("F1", kultura)}} MB</b></span>
            <span class="chip">{{T("raport.zbudowano")}} <b>{{E(wynik.Zbudowany ?? "")}}</b></span>
          </div>
          <div class="chipy">
            <button data-filtr="DUPLIKAT" aria-pressed="false">{{W(Porownanie.Duplikat)}} {{Ile(Porownanie.Duplikat)}}</button>
            <button data-filtr="DUPLIKAT-NADZBIOR" aria-pressed="false">{{W(Porownanie.Nadzbior)}} {{Ile(Porownanie.Nadzbior)}}</button>
            <button data-filtr="DO WGLADU" aria-pressed="false">{{W(Porownanie.DoWgladu)}} {{Ile(Porownanie.DoWgladu)}}</button>
            <button data-filtr="PRZEMALOWANIE" aria-pressed="false">{{W(Porownanie.Przemalowanie)}} {{Ile(Porownanie.Przemalowanie)}}</button>
          </div>
          <input id="szukaj" type="search" placeholder="{{T("raport.szukaj")}}">
          <button id="motyw">{{T("raport.motyw")}}</button>
        </header>
        <main>
        """;
    }
}
