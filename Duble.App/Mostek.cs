// Mostek.cs — kanal UI (HTML/JS) <-> C#. Router komend "grupa.akcja" + zdarzenia do UI.
//
// Kontrakt (nie zmieniac bez testow MostekTests i docs/superpowers/plans/…etap2…):
//   zadanie   {id, cmd, args}      odpowiedz {id, ok:true, result} | {id, ok:false, error:{code,message}}
//   zdarzenie {event, data}        (bez id; z C# do UI)
// Kody bledow: unknown_command, bad_args, no_project, busy, not_found, io, cancelled, internal.
// Handlery moga rzucac BladMostka(kod, tekst) -> kod trafia do UI; inne wyjatki -> "internal".
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Duble.App;

public interface IOkno
{
    void Minimalizuj(); void MaksymalizujAlboPrzywroc(); void Zamknij();
    void RozpocznijPrzeciaganie();   // awaryjnie, gdy app-region: drag nie zadziala
    bool Zmaksymalizowane { get; }
    void Uruchom(Action a);   // wykonaj na watku UI
}

public interface IDialogi
{
    string WybierzFolder(string tytul, string start);
    string[] WybierzPliki(string tytul, string filtr, bool wiele, string start);
    string ZapiszPlik(string tytul, string filtr, string domyslnaNazwa, string start);
}

public sealed class BladMostka : Exception
{
    public string Kod { get; }
    public BladMostka(string kod, string tekst) : base(tekst) { Kod = kod; }
}

public sealed class Mostek
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly Dictionary<string, Func<JsonElement, Task<object>>> handlery = new(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> wyslij;
    public IOkno Okno { get; }
    public IDialogi Dialogi { get; }
    public Ustawienia Ustawienia { get; }
    public bool Dev { get; set; }
    /// <summary>Gdzie zapisywac ustawienia (null = domyslne %AppData%); testy podaja folder tymczasowy.</summary>
    public string PlikUstawien { get; set; }

    public Mostek(IOkno okno, IDialogi dialogi, Ustawienia ustawienia, Action<string> wyslijJson)
    { Okno = okno; Dialogi = dialogi; Ustawienia = ustawienia; wyslij = wyslijJson; }

    public void Rejestruj(string cmd, Func<JsonElement, Task<object>> handler) => handlery[cmd] = handler;
    public void Rejestruj(string cmd, Func<JsonElement, object> handler) => handlery[cmd] = a => Task.FromResult(handler(a));
    public bool Zna(string cmd) => handlery.ContainsKey(cmd);

    public async Task<string> Obsluz(string zadanieJson)
    {
        string id = null;
        try
        {
            using var doc = JsonDocument.Parse(zadanieJson);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var i) ? i.ToString() : null;
            var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() : null;
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            if (cmd == null || !handlery.TryGetValue(cmd, out var h)) return Blad(id, "unknown_command", "nieznana komenda: " + cmd);
            var wynik = await h(args).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { id, ok = true, result = wynik }, Json);
        }
        catch (BladMostka b) { return Blad(id, b.Kod, b.Message); }
        catch (OperationCanceledException) { return Blad(id, "cancelled", "anulowano"); }
        catch (Exception e) { return Blad(id, "internal", e.GetType().Name + ": " + e.Message); }
    }

    static string Blad(string id, string kod, string tekst)
        => JsonSerializer.Serialize(new { id, ok = false, error = new { code = kod, message = tekst } }, Json);

    public void Zdarzenie(string nazwa, object dane) => wyslij(JsonSerializer.Serialize(new { @event = nazwa, data = dane }, Json));

    // --- pomocnicze do argumentow ---
    public static string Tekst(JsonElement args, string nazwa, bool wymagany = false)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(nazwa, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        if (wymagany) throw new BladMostka("bad_args", "brak argumentu: " + nazwa);
        return null;
    }
    public static bool Flaga(JsonElement args, string nazwa, bool domyslnie = false)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(nazwa, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : domyslnie;
    public static List<string> Lista(JsonElement args, string nazwa)
    {
        var wy = new List<string>();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(nazwa, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) wy.Add(x.GetString());
        return wy;
    }
    public static int Liczba(JsonElement args, string nazwa, int domyslnie = 0)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(nazwa, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : domyslnie;
}
