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

namespace Duble;

public static class Raport
{
    const int Bok = 96;              // bok miniatury w pikselach
    const int MaxWierszy = 12;       // ile par tekstur pokazujemy na grupe
    const int MaxUnikalnych = 8;     // ile tekstur "tylko tutaj" na czlonka

    static readonly Dictionary<string, string> CacheMiniatur = new();
    static int bezPodgladu, bezPliku;

    public static void Zbuduj(Katalog katalog, WynikPorownania wynik, string plik, Action<string> log)
    {
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

        long doOdzyskania = 0;
        foreach (var g in grupy.Where(g => g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior))
            foreach (var id in g.Pozycje.Where(x => x != g.Zwyciezca))
                doOdzyskania += wgId[id].BajtyYdd + wgId[id].Tekstury.Sum(t => t.Bajty);

        var sb = new StringBuilder();
        sb.Append(Naglowek(katalog, wynik, grupy, doOdzyskania));

        int zrobione = 0;
        foreach (var g in grupy)
        {
            sb.Append(Karta(g, wgId));
            if (++zrobione % 10 == 0) log($"  grup: {zrobione}/{grupy.Count}");
        }

        sb.Append("""
        </main>
        <footer>
          <p>Raport zbudowany przez <code>duble raport</code>. Nic nie zostalo skasowane — to tylko podglad.</p>
        </footer>
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
            RpfManager.IsGen9 = gen9;
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

    static string Kafelek(Tekstura t, bool gen9, string etykieta = null)
    {
        if (t == null)
            return "<div class=\"kafelek pusty\"><div class=\"placeholder\">brak<br>odpowiednika</div></div>";
        var uri = Miniatura(t, gen9);
        var obraz = uri != null
            ? $"<img src=\"{uri}\" alt=\"{E(t.Plik)}\" loading=\"lazy\" width=\"{Bok}\" height=\"{Bok}\">"
            : $"<div class=\"placeholder\">{E(t.Format)}<br>bez podgladu</div>";
        var znaczniki = new List<string> { $"{t.W}×{t.H}", E(t.Format) };
        if (t.Mipy <= 1) znaczniki.Add("<span class=\"zle\">bez mipow</span>");
        if (t.Format == "BC1" && t.Alfa > 0.02f) znaczniki.Add("<span class=\"zle\">BC1 z alfa</span>");
        return $"""
            <div class="kafelek">
              {obraz}
              <div class="nazwa" title="{E(t.Plik)}">{E(etykieta ?? t.Plik)}</div>
              <div class="meta">{string.Join(" · ", znaczniki)}</div>
            </div>
            """;
    }

    // ===================== karta grupy =====================

    static string Karta(Grupa g, Dictionary<string, Pozycja> wgId)
    {
        var czlonkowie = g.Pozycje.OrderByDescending(id => id == g.Zwyciezca ? 1 : 0)
                                   .ThenByDescending(id => g.Punkty.TryGetValue(id, out var p) ? p : 0)
                                   .ToList();
        var wzorzec = wgId[czlonkowie[0]];
        bool usuwamy = g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior;

        var sb = new StringBuilder();
        var szukaj = string.Join(" ", czlonkowie.Select(id => wgId[id].Opis)).ToLowerInvariant();
        sb.Append($"<article class=\"grupa {Klasa(g.Werdykt)}\" data-werdykt=\"{E(g.Werdykt)}\" data-szukaj=\"{E(szukaj)}\">");

        // --- naglowek ---
        sb.Append("<header class=\"glowa\">");
        sb.Append($"<span class=\"odznaka {Klasa(g.Werdykt)}\">{E(g.Werdykt)}</span>");
        sb.Append($"<h2>{string.Join(" <span class=\"rowna\">=</span> ", czlonkowie.Select(id => $"<span class=\"tytul\">{E(wgId[id].Opis)}<sub>{E(wgId[id].Sufiks)}</sub></span>"))}</h2>");
        sb.Append($"<p class=\"powod\">{E(g.Pary.FirstOrDefault()?.Powod ?? g.Powod ?? "")}</p>");
        sb.Append("</header>");

        // --- panele pozycji ---
        sb.Append("<div class=\"panele\">");
        foreach (var id in czlonkowie)
        {
            var p = wgId[id];
            bool wygrywa = id == g.Zwyciezca;
            string stan = !usuwamy ? "" : wygrywa ? "wygrywa" : "odrzut";
            sb.Append($"<section class=\"panel {stan}\">");
            sb.Append("<div class=\"panel-glowa\">");
            sb.Append($"<span class=\"paczka\">{E(p.Paczka)}</span>");
            if (usuwamy) sb.Append(wygrywa ? "<span class=\"stan wygrywa\">ZOSTAJE</span>" : "<span class=\"stan odrzut\">DO ODRZUCENIA</span>");
            sb.Append("</div>");
            sb.Append($"<div class=\"nazwa-poz\">{E(p.Typ)}_{p.Numer:d3}<sub>{E(p.Sufiks)}</sub></div>");
            if (g.Punkty.TryGetValue(id, out var pkt))
            {
                sb.Append($"<div class=\"punkty\"><b>{pkt:F0}</b><span>/100 pkt jakosci</span></div>");
                if (g.Rozpiska.TryGetValue(id, out var r))
                    sb.Append($"<div class=\"rozpiska\">{E(r)}</div>");
            }
            var med = p.Tekstury.Count > 0
                ? p.Tekstury.OrderBy(t => (long)t.W * t.H).ElementAt(p.Tekstury.Count / 2)
                : null;
            sb.Append("<ul class=\"znaczniki\">");
            sb.Append($"<li>{p.Tekstury.Count} tekstur</li>");
            if (med != null) sb.Append($"<li>{med.W}×{med.H}</li>");
            sb.Append($"<li>{p.Geo?.Trojkaty ?? 0:N0} tri</li>");
            sb.Append($"<li>LOD {p.Geo?.Lody ?? 0}</li>");
            int bezMip = p.Tekstury.Count(t => t.Mipy <= 1);
            if (bezMip > 0) sb.Append($"<li class=\"zle\">{bezMip} bez mipow</li>");
            sb.Append("</ul>");
            sb.Append("</section>");
        }
        sb.Append("</div>");

        // --- porownanie tekstur ---
        sb.Append("<div class=\"tekstury\">");
        sb.Append($"<h3>Tekstury obok siebie</h3>");
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
            sb.Append(Kafelek(wz, wzorzec.Gen9));
            for (int i = 1; i < czlonkowie.Count; i++)
            {
                var inny = wgId[czlonkowie[i]];
                sb.Append(Kafelek(trafienia[i] >= 0 ? inny.Tekstury[trafienia[i]] : null, inny.Gen9));
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");
        if (pominietych > 0)
            sb.Append($"<p class=\"uwaga\">pokazane {MaxWierszy} z {wzorzec.Tekstury.Count} tekstur wzorca — reszta ({pominietych}) pominieta w podgladzie, ale policzona w werdykcie</p>");

        // --- tekstury wystepujace tylko u jednego ---
        foreach (var id in czlonkowie)
        {
            var p = wgId[id];
            var unikalne = Enumerable.Range(0, p.Tekstury.Count).Where(k => !uzyte[id].Contains(k)).ToList();
            if (unikalne.Count == 0) continue;
            sb.Append($"<h4>Tylko w <em>{E(p.Opis)}</em> — {unikalne.Count} tekstur{(usuwamy && id != g.Zwyciezca ? " <span class=\"zle\">(to stracisz, jesli odrzucisz te pozycje)</span>" : "")}</h4>");
            sb.Append("<div class=\"pasek\">");
            foreach (var k in unikalne.Take(MaxUnikalnych)) sb.Append(Kafelek(p.Tekstury[k], p.Gen9));
            sb.Append("</div>");
            if (unikalne.Count > MaxUnikalnych)
                sb.Append($"<p class=\"uwaga\">+ {unikalne.Count - MaxUnikalnych} dalszych</p>");
        }
        sb.Append("</div></article>");
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

    static string Naglowek(Katalog katalog, WynikPorownania wynik, List<Grupa> grupy, long doOdzyskania)
    {
        int Ile(string w) => grupy.Count(g => g.Werdykt == w);
        int doOdrzucenia = grupy.Where(g => g.Werdykt == Porownanie.Duplikat || g.Werdykt == Porownanie.Nadzbior)
                                .Sum(g => g.Pozycje.Count - 1);
        var kultura = CultureInfo.InvariantCulture;

        return $$"""
        <!doctype html>
        <html lang="pl">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Duble garderoby</title>
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
          <h1>Duble garderoby</h1>
          <div class="chipy">
            <span class="chip">pozycji w katalogu <b>{{katalog.Pozycje.Count}}</b></span>
            <span class="chip">tekstur <b>{{katalog.Pozycje.Sum(p => p.Tekstury.Count)}}</b></span>
            <span class="chip">widocznych grup <b id="licznik">0</b></span>
            <span class="chip">do odrzucenia <b>{{doOdrzucenia}}</b></span>
            <span class="chip">odzyska sie <b>{{(doOdzyskania / 1024.0 / 1024.0).ToString("F1", kultura)}} MB</b></span>
          </div>
          <div class="chipy">
            <button data-filtr="DUPLIKAT" aria-pressed="false">DUPLIKAT {{Ile(Porownanie.Duplikat)}}</button>
            <button data-filtr="DUPLIKAT-NADZBIOR" aria-pressed="false">NADZBIOR {{Ile(Porownanie.Nadzbior)}}</button>
            <button data-filtr="DO WGLADU" aria-pressed="false">DO WGLADU {{Ile(Porownanie.DoWgladu)}}</button>
            <button data-filtr="PRZEMALOWANIE" aria-pressed="false">PRZEMALOWANIE {{Ile(Porownanie.Przemalowanie)}}</button>
          </div>
          <input id="szukaj" type="search" placeholder="szukaj: jbib, civil01...">
          <button id="motyw">motyw</button>
        </header>
        <main>
        """;
    }
}
