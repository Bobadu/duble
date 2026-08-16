// duble — wyszukiwanie duplikatow ubran w paczkach do GTA V (Legacy i Enhanced/gen9).
// Cienka nakladka CLI na biblioteke Duble.Core (ten sam silnik ma aplikacja okienkowa Duble.App).
//
//   duble indeks <zrodlo> [<zrodlo>...] [--katalog <plik>] [--nazwa <paczka>] [--gra <folder>]
//   duble kalibruj [--katalog <plik>]
//   duble porownaj [--katalog <plik>] [--duble <plik>]
//   duble raport   [--katalog <plik>] [--duble <plik>] [--out <plik.html>] [--lang pl|en]
//   duble zastosuj [--decyzje <plik.tsv>]     — przenosi odrzucone do _odrzucone\
//   duble cofnij   [--cofka <plik.json>]      — przywraca wszystko z powrotem
//   duble lista    [--katalog <plik>]
//   duble podglad  <plik.ytd> [--out plik.png]  — miniatura tekstury (kontrola kanalow)
//   duble obj      <plik.ydd> [--out plik.obj]  — eksport geometrii (legacy/gen9) do OBJ
//   duble pusty    <we.ydd> <wy.ydd>            — model niewidzialny (wierzcholki zwiniete do zera), gen9
//   duble tekstura <plik.ytd> [--out folder]     — kazda tekstura z ytd do PNG w pelnej rozdzielczosci
//   duble ytd      <wy.ytd> <a.dds> [b.dds ...]  — buduje ytd (gen9) z DDS; nazwa tekstury = nazwa pliku
//
// Zrodlem moze byc folder rozpakowanej paczki albo archiwum .rpf.
// Katalog jest TRWALY — kazda nowa paczka porownuje sie z calym dotychczasowym dorobkiem.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Duble;

var argv = args.ToList();

string Opcja(string nazwa, string domyslnie)
{
    int i = argv.IndexOf(nazwa);
    if (i < 0 || i + 1 >= argv.Count) return domyslnie;
    var v = argv[i + 1];
    argv.RemoveRange(i, 2);
    return v;
}

if (argv.Count == 0)
{
    Console.Error.WriteLine("uzycie: duble <indeks|kalibruj|porownaj|raport|lista> [opcje]");
    return 2;
}

var korzenProjektu = Environment.GetEnvironmentVariable("MENYOO_STUDIO")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));   // tools\Duble\Duble.Cli\bin\Release\net10.0 -> korzen repo
var domyslnyKatalog = Path.Combine(korzenProjektu, "staging", "duble", "katalog.json");
var domyslneDuble = Path.Combine(korzenProjektu, "staging", "duble", "duble.json");

string sciezkaKatalogu = Opcja("--katalog", domyslnyKatalog);
string sciezkaDubli = Opcja("--duble", domyslneDuble);
string sciezkaDecyzji = Opcja("--decyzje", Path.Combine(korzenProjektu, "staging", "duble", "decyzje.tsv"));
string sciezkaCofki = Opcja("--cofka", Path.Combine(korzenProjektu, "staging", "duble", "cofnij.json"));
string nazwaPaczki = Opcja("--nazwa", null);
string folderGry = Opcja("--gra", Environment.GetEnvironmentVariable("GTAV_ENHANCED"));
string wyjscie = Opcja("--out", null);
string jezyk = Opcja("--lang", "pl");    // jezyk raportu i tekstow powodow: pl | en

string cmd = argv[0].ToLowerInvariant();
argv.RemoveAt(0);

void Log(string s) => Console.WriteLine(s);

// klucze potrzebne tylko do czytania zaszyfrowanych archiwow R*; nasze paczki sa OPEN
if (!string.IsNullOrEmpty(folderGry) && Directory.Exists(folderGry))
{
    try { GTA5Keys.LoadFromPath(folderGry, true, null); } catch { }
}

switch (cmd)
{
    case "odswiez":
    case "indeks":
        {
            var katalog = Katalog.Wczytaj(sciezkaKatalogu);
            if (cmd == "odswiez")
            {
                if (katalog.Zrodla.Count == 0) { Console.Error.WriteLine("[blad] katalog nie zna zadnych zrodel — uzyj `duble indeks <folder>`"); return 1; }
                argv = katalog.Zrodla.Values.ToList();
            }
            if (argv.Count == 0) { Console.Error.WriteLine("[blad] podaj co najmniej jedno zrodlo"); return 2; }
            foreach (var zrodlo in argv)
            {
                Log($"== {zrodlo}");
                var nazwa = nazwaPaczki ?? Path.GetFileName(Path.GetFullPath(zrodlo).TrimEnd(Path.DirectorySeparatorChar));
                if (nazwa.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) nazwa = Path.GetFileNameWithoutExtension(nazwa);
                var start = DateTime.Now;
                var pozycje = Indeks.Zrodlo(zrodlo, nazwa, Log);
                katalog.UsunPaczke(nazwa);
                katalog.Wstaw(pozycje);
                katalog.Zrodla[nazwa] = Path.GetFullPath(zrodlo);
                var tex = pozycje.Sum(p => p.Tekstury.Count);
                var nieodczytane = pozycje.Sum(p => p.Tekstury.Count(t => !t.Zdekodowana));
                Log($"  {pozycje.Count} pozycji, {tex} tekstur"
                    + (nieodczytane > 0 ? $" ({nieodczytane} nie do zdekodowania — BC7)" : "")
                    + $", {(DateTime.Now - start).TotalSeconds:F0} s");
            }
            katalog.Zbudowany = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            katalog.Zapisz(sciezkaKatalogu);
            Log($"katalog: {sciezkaKatalogu} ({katalog.Pozycje.Count} pozycji)");
            return 0;
        }

    case "lista":
        {
            var katalog = Katalog.Wczytaj(sciezkaKatalogu);
            foreach (var g in katalog.Pozycje.GroupBy(p => p.Paczka))
            {
                Log($"{g.Key,-28} {g.Count(),5} pozycji, {g.Sum(p => p.Tekstury.Count),6} tekstur");
                foreach (var t in g.GroupBy(p => p.Typ).OrderBy(x => x.Key))
                    Log($"    {t.Key,-10} {t.Count(),4}");
            }
            return 0;
        }

    case "kalibruj":
        return Kalibracja.Uruchom(Katalog.Wczytaj(sciezkaKatalogu), Log);

    case "obj":
        {
            // narzedzie kontrolne: eksportuje najwyzszy LOD modelu .ydd (legacy albo gen9) do OBJ
            // z normalnymi i UV — do ogladania/porownywania geometrii w Blenderze (np. ciala:
            // waniliowe vs Killstore). Format (legacy/gen9) z naglowka RSC7; tryb gen9 czyta oba (Format.cs).
            if (argv.Count < 1) { Console.Error.WriteLine("uzycie: duble obj <plik.ydd> [--out plik.obj]"); return 2; }
            var bajty = File.ReadAllBytes(argv[0]);
            YddFile ydd = null; string fmt = Format.Nazwa(Rsc7.Gen9(bajty, ".ydd"));
            try
            {
                var y = new YddFile();
                RpfFile.LoadResourceFile(y, bajty, 165);
                var d0 = y.Drawables?.FirstOrDefault();
                var n0 = d0?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
                if (n0 > 0 && n0 < 5_000_000) ydd = y;
            }
            catch { }
            if (ydd == null) { Console.Error.WriteLine("[blad] nie udalo sie wczytac modelu"); return 1; }
            var dr = ydd.Drawables.First();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# duble obj — " + Path.GetFileName(argv[0]) + " (" + fmt + ")");
            int baseV = 1, baseN = 1, baseT = 1, gi = 0, sumV = 0, sumF = 0;
            var lods = new[] { ("high", dr.DrawableModels?.High), ("med", dr.DrawableModels?.Med), ("low", dr.DrawableModels?.Low), ("vlow", dr.DrawableModels?.VLow) };
            foreach (var (nazwaLod, arr) in lods)
            {
                if (arr == null) continue;
                int lv = 0, lf = 0;
                foreach (var m in arr) foreach (var geo in m?.Geometries ?? Array.Empty<DrawableGeometry>())
                { lv += (int)(geo?.VertexBuffer?.VertexCount ?? 0); lf += (int)((geo?.IndicesCount ?? 0) / 3); }
                Log($"LOD {nazwaLod,-4} modeli={arr.Length} wierzcholkow={lv} trojkatow={lf}");
            }
            var bmin = dr.BoundingBoxMin; var bmax = dr.BoundingBoxMax;
            Log($"bbox min=({bmin.X:F3},{bmin.Y:F3},{bmin.Z:F3}) max=({bmax.X:F3},{bmax.Y:F3},{bmax.Z:F3}) kosci={dr.Skeleton?.Bones?.Items?.Length ?? 0}");
            foreach (var m in dr.DrawableModels?.High ?? Array.Empty<DrawableModel>())
            {
                foreach (var geo in m?.Geometries ?? Array.Empty<DrawableGeometry>())
                {
                    var vd = geo?.VertexBuffer?.Data1 ?? geo?.VertexBuffer?.Data2;
                    if (vd?.VertexBytes == null || vd.Info == null) continue;
                    var info = vd.Info; int stride = (int)info.Stride; int n = (int)vd.VertexCount; var b = vd.VertexBytes;
                    bool maNorm = ((info.Flags >> 3) & 1) == 1, maUv = ((info.Flags >> 6) & 1) == 1;
                    int offP = info.GetComponentOffset(0), offN = info.GetComponentOffset(3), offT = info.GetComponentOffset(6);
                    var typT = info.GetComponentType(6);
                    sb.AppendLine("g geo_" + gi + " shader_" + geo.ShaderID);
                    Log($"geo {gi}: wierzcholkow={n} stride={stride} flags=0x{info.Flags:X} types=0x{(ulong)info.Types:X} normalne={maNorm} uv={maUv}({typT})");
                    // shader + tekstury tej geometrii (rozowy kwadrat w grze = tekstura, ktorej nie ma w ytd)
                    var shs = dr.ShaderGroup?.Shaders?.data_items;
                    if (shs != null && geo.ShaderID < shs.Length && shs[geo.ShaderID] != null)
                    {
                        var sh = shs[geo.ShaderID];
                        var texy = new List<string>();
                        var prs = sh.ParametersList?.Parameters; var hs = sh.ParametersList?.Hashes;
                        if (prs != null)
                            for (int k = 0; k < prs.Length; k++)
                                if (prs[k].DataType == 0 && prs[k].Data is TextureBase tb)
                                    texy.Add((hs != null && k < hs.Length ? hs[k].ToString() : "?") + "=" + (tb.Name ?? "(null)"));
                        Log($"   shader {sh.Name} ({sh.FileName}) tekstury: {string.Join(", ", texy)}");
                    }
                    for (int v = 0; v < n; v++)
                    {
                        int o = v * stride;
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}",
                            BitConverter.ToSingle(b, o + offP), BitConverter.ToSingle(b, o + offP + 4), BitConverter.ToSingle(b, o + offP + 8)));
                    }
                    if (maNorm) for (int v = 0; v < n; v++)
                    {
                        int o = v * stride + offN;
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "vn {0:R} {1:R} {2:R}",
                            BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8)));
                    }
                    if (maUv) for (int v = 0; v < n; v++)
                    {
                        int o = v * stride + offT; float tu, tv;
                        if (typT == VertexComponentType.Half2) { tu = (float)BitConverter.ToHalf(b, o); tv = (float)BitConverter.ToHalf(b, o + 2); }
                        else { tu = BitConverter.ToSingle(b, o); tv = BitConverter.ToSingle(b, o + 4); }
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "vt {0:R} {1:R}", tu, 1 - tv));
                    }
                    var idx = geo.IndexBuffer?.Indices;
                    if (idx != null)
                    {
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            string F(int k)
                            {
                                int a = idx[t + k];
                                return (baseV + a) + "/" + (maUv ? (baseT + a).ToString() : "") + "/" + (maNorm ? (baseN + a).ToString() : "");
                            }
                            sb.AppendLine("f " + F(0) + " " + F(1) + " " + F(2));
                        }
                        sumF += idx.Length / 3;
                    }
                    baseV += n; if (maNorm) baseN += n; if (maUv) baseT += n; sumV += n; gi++;
                }
            }
            var objOut = wyjscie ?? Path.ChangeExtension(argv[0], ".obj");
            File.WriteAllText(objOut, sb.ToString());
            Log($"OBJ: {objOut} (geometrii={gi}, wierzcholkow={sumV}, trojkatow={sumF})");
            return 0;
        }

    case "pusty":
        {
            // robi z modelu .ydd model NIEWIDZIALNY: wszystkie wierzcholki zwiniete do (0,0,0) — zdegenerowane
            // trojkaty nic nie rysuja. Po co: „pusty" top/podkoszulek dla bazy topless (jbib_015/accs_015).
            // Waniliowe „puste" R* (accs_014: kwadracik 3 cm w srodku klatki) wystaja rozowym kwadratem, gdy
            // tors nie ma brzucha (KS V2). Wynik zapisany jako gen9 (v159).
            if (argv.Count < 2) { Console.Error.WriteLine("uzycie: duble pusty <wejscie.ydd> <wyjscie.ydd>"); return 2; }
            var bajtyP = File.ReadAllBytes(argv[0]);
            YddFile yddP = null;
            try
            {
                var y = new YddFile();
                RpfFile.LoadResourceFile(y, bajtyP, 165);   // tryb gen9 czyta oba formaty po naglowku (Format.cs)
                var n0 = y.Drawables?.FirstOrDefault()?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
                if (n0 > 0) yddP = y;
            }
            catch { }
            if (yddP == null) { Console.Error.WriteLine("[blad] nie udalo sie wczytac modelu"); return 1; }
            int zwiniete = 0;
            foreach (var dr in yddP.Drawables)
            {
                var dm = dr.DrawableModels;
                foreach (var arr in new[] { dm?.High, dm?.Med, dm?.Low, dm?.VLow })
                {
                    if (arr == null) continue;
                    foreach (var m in arr) foreach (var geo in m?.Geometries ?? Array.Empty<DrawableGeometry>())
                    {
                        var vd = geo?.VertexBuffer?.Data1 ?? geo?.VertexBuffer?.Data2;
                        if (vd?.VertexBytes == null || vd.Info == null) continue;
                        int stride = (int)vd.Info.Stride, off = vd.Info.GetComponentOffset(0), n = (int)vd.VertexCount;
                        for (int v = 0; v < n; v++) { int o = v * stride + off; for (int k = 0; k < 12; k++) vd.VertexBytes[o + k] = 0; zwiniete++; }
                    }
                }
                dr.BoundingBoxMin = new SharpDX.Vector3(-0.001f, -0.001f, -0.001f); dr.BoundingBoxMax = new SharpDX.Vector3(0.001f, 0.001f, 0.001f);
                dr.BoundingCenter = SharpDX.Vector3.Zero; dr.BoundingSphereRadius = 0.001f;
            }
            RpfManager.IsGen9 = true;   // zapis: gen9 (Save() patrzy na flage)
            var wyj = yddP.Save();
            File.WriteAllBytes(argv[1], wyj);
            Log($"pusty: zwinieto {zwiniete} wierzcholkow -> {argv[1]} ({wyj.Length} B, gen9)");
            return 0;
        }

    case "tekstura":
        {
            // pelna rozdzielczosc: kazda tekstura z .ytd -> PNG (do Blendera / ogladania w 100 %)
            if (argv.Count < 1) { Console.Error.WriteLine("uzycie: duble tekstura <plik.ytd> [--out folder]"); return 2; }
            var bajtyT = File.ReadAllBytes(argv[0]);
            YtdFile ytdT = null;
            try { var y = new YtdFile(); RpfFile.LoadResourceFile(y, bajtyT, 13);   // tryb gen9 czyta oba formaty po naglowku
                  var t0 = y.TextureDict?.Textures?.data_items?.FirstOrDefault();
                  if (t0 != null && t0.Width > 0 && t0.Width <= 16384 && t0.Levels >= 1 && t0.Levels <= 16) ytdT = y; }
            catch { }
            if (ytdT == null) { Console.Error.WriteLine("[blad] nie udalo sie wczytac ytd"); return 1; }
            var folder = wyjscie ?? Path.GetDirectoryName(Path.GetFullPath(argv[0]));
            Directory.CreateDirectory(folder);
            foreach (var t in ytdT.TextureDict.Textures.data_items)
            {
                var px = Tekstury.Piksele(t, 0, out int tw, out int th);   // DDSIO + BC7
                if (px == null) { Log($"{t.Name}: nie zdekodowano ({Odciski.NazwaFormatu(t)})"); continue; }
                var rgb = new byte[tw * th * 3];
                for (int i = 0, j = 0; i < px.Length; i += 4, j += 3) { rgb[j] = px[i + 2]; rgb[j + 1] = px[i + 1]; rgb[j + 2] = px[i]; }
                var pngT = Path.Combine(folder, t.Name + ".png");
                File.WriteAllBytes(pngT, Png.Rgb(rgb, tw, th));
                Log($"{t.Name}  {tw}x{th} {Odciski.NazwaFormatu(t)} -> {pngT}");
            }
            return 0;
        }

    case "ytd":
        {
            // buduje .ytd (gen9) z plikow DDS: nazwa tekstury = nazwa pliku bez rozszerzenia.
            // Uzycie: duble ytd <wyjscie.ytd> <plik1.dds> [plik2.dds ...]
            if (argv.Count < 2) { Console.Error.WriteLine("uzycie: duble ytd <wyjscie.ytd> <plik.dds> [...]"); return 2; }
            var lista = new List<Texture>();
            foreach (var dds in argv.Skip(1))
            {
                var t = DDSIO.GetTexture(File.ReadAllBytes(dds));
                if (t == null) { Console.Error.WriteLine("[blad] nie odczytano DDS: " + dds); return 1; }
                t.Name = Path.GetFileNameWithoutExtension(dds);
                t.NameHash = JenkHash.GenHash(t.Name.ToLowerInvariant());
                lista.Add(t);
                Log($"{t.Name}  {t.Width}x{t.Height} {Odciski.NazwaFormatu(t)} mipy={t.Levels}");
            }
            var tdNew = new TextureDictionary();
            tdNew.BuildFromTextureList(lista);
            var ytdNew = new YtdFile { TextureDict = tdNew };
            RpfManager.IsGen9 = true;   // zapis: gen9 (Save() patrzy na flage)
            var dane = ytdNew.Save();
            File.WriteAllBytes(argv[0], dane);
            Log($"YTD: {argv[0]} ({dane.Length} B, gen9, tekstur {lista.Count})");
            return 0;
        }

    case "podglad":
        {
            // narzedzie kontrolne: zapisuje miniature pojedynczej .ytd do PNG.
            // Sluzy do sprawdzenia, czy kolejnosc kanalow (DDSIO oddaje BGRA) jest dobra —
            // przy pomylce skora wychodzi niebieska, a tego na liczbach nie widac.
            if (argv.Count < 1) { Console.Error.WriteLine("uzycie: duble podglad <plik.ytd> [--out plik.png]"); return 2; }
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, File.ReadAllBytes(argv[0]), 13);   // tryb gen9 czyta oba formaty po naglowku
            var tx = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (tx == null) { Console.Error.WriteLine("[blad] brak tekstury w pliku"); return 1; }
            var odc = new Tekstura();
            var rgb = Odciski.Tekstura(tx, odc, 256);
            if (rgb == null) { Console.Error.WriteLine("[blad] nie udalo sie zdekodowac (BC7?)"); return 1; }
            var png = wyjscie ?? Path.ChangeExtension(argv[0], ".png");
            File.WriteAllBytes(png, Png.Rgb(rgb, 256, 256));
            Log($"{odc.Nazwa}  {odc.W}x{odc.H} {odc.Format} mipy={odc.Mipy} alfa={odc.Alfa:P1}");
            Log($"PNG: {png}");
            return 0;
        }

    case "porownaj":
        {
            var katalog = Katalog.Wczytaj(sciezkaKatalogu);
            if (katalog.Pozycje.Count == 0) { Console.Error.WriteLine("[blad] pusty katalog — najpierw `duble indeks`"); return 1; }
            var wynik = Porownanie.Znajdz(katalog, Log);
            wynik.Zapisz(sciezkaDubli);
            Zastosowanie.ZapiszDecyzje(wynik, katalog, sciezkaDecyzji);
            Log($"duble:   {sciezkaDubli}");
            Log($"decyzje: {sciezkaDecyzji}  (mozesz poprawic TAK/NIE przed `zastosuj`)");
            return 0;
        }

    case "zastosuj":
        return Zastosowanie.Zastosuj(Katalog.Wczytaj(sciezkaKatalogu), sciezkaDecyzji,
            Path.Combine(korzenProjektu, "staging", "wardrobe2", "_odrzucone"), sciezkaCofki, Log);

    case "cofnij":
        return Zastosowanie.Cofnij(sciezkaCofki, Log);

    case "raport":
        {
            var katalog = Katalog.Wczytaj(sciezkaKatalogu);
            var wynik = WynikPorownania.Wczytaj(sciezkaDubli);
            if (wynik.Grupy.Count == 0) { Console.Error.WriteLine("[uwaga] brak grup — najpierw `duble porownaj`"); }
            var plik = wyjscie ?? Path.Combine(korzenProjektu, "docs", "duble-raport.html");
            Raport.Zbuduj(katalog, wynik, plik, Log, jezyk);
            Log($"raport: {plik}");
            return 0;
        }

    default:
        Console.Error.WriteLine("[blad] nieznana komenda: " + cmd);
        return 2;
}
