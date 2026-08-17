// JobRunner.cs — jedno ciezkie zadanie naraz (indeksowanie, porownanie, zastosuj…), postep jako zdarzenia "job", anulowanie.
//
// zdarzenie "job": { typ, opis, stan: start|postep|koniec|anulowano|blad, etap, zrobione, wszystkie, procent, tekst, blad }
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Duble.App;

public sealed class JobRunner
{
    readonly Action<string, object> zdarzenie;
    readonly object klucz = new();
    CancellationTokenSource cts;
    public bool Zajety { get; private set; }
    public string Biezace { get; private set; }

    public JobRunner(Action<string, object> zdarzenie) { this.zdarzenie = zdarzenie; }

    /// <summary>Uruchamia prace w tle. false = inne zadanie w toku (nic nie uruchomiono).</summary>
    public async Task<bool> Uruchom(string typ, string opis, Func<CancellationToken, Action<Duble.Core.Indexing.Postep>, Task> praca)
    {
        CancellationTokenSource moj;
        lock (klucz)
        {
            if (Zajety) return false;
            Zajety = true; Biezace = typ; cts = moj = new CancellationTokenSource();
        }
        var ct = moj.Token;
        zdarzenie("job", new { typ, opis, stan = "start" });
        // postep bywa zglaszany per plik (zastosuj/cofnij: setki razy na sekunde) — do UI idzie najwyzej co ~100 ms
        // (plus zawsze: nowy etap i ostatni krok etapu), bo kazde zdarzenie odswieza widoki
        long ostatniTik = 0; string ostatniEtap = null; var klucz2 = new object();
        void Postep(Duble.Core.Indexing.Postep p)
        {
            lock (klucz2)
            {
                var teraz = Environment.TickCount64;
                bool koniecEtapu = p.Wszystkie > 0 && p.Zrobione >= p.Wszystkie;
                bool nowyEtap = p.Etap != ostatniEtap;
                if (!koniecEtapu && !nowyEtap && teraz - ostatniTik < 100) return;
                ostatniTik = teraz; ostatniEtap = p.Etap;
            }
            zdarzenie("job", new
            {
                typ, opis, stan = "postep", etap = p.Etap, zrobione = p.Zrobione, wszystkie = p.Wszystkie,
                procent = p.Wszystkie > 0 ? (int)(100L * p.Zrobione / p.Wszystkie) : 0, tekst = p.Kontener,
            });
        }
        try
        {
            await Task.Run(() => praca(ct, Postep), ct).ConfigureAwait(false);
            zdarzenie("job", new { typ, opis, stan = "koniec" });
        }
        catch (OperationCanceledException) { zdarzenie("job", new { typ, opis, stan = "anulowano" }); }
        catch (Exception e) { zdarzenie("job", new { typ, opis, stan = "blad", blad = e.Message }); }
        finally
        {
            lock (klucz) { Zajety = false; Biezace = null; if (ReferenceEquals(cts, moj)) cts = null; }
            moj.Dispose();
        }
        return true;
    }

    /// <summary>Jak Uruchom, ale nie czeka na koniec: true = wystartowalo (w tle), false = zajety.</summary>
    public bool SprobujUruchom(string typ, string opis, Func<CancellationToken, Action<Duble.Core.Indexing.Postep>, Task> praca)
    {
        lock (klucz) { if (Zajety) return false; }
        _ = Uruchom(typ, opis, praca);   // ustawia Zajety synchronicznie (do pierwszego await)
        return Zajety;
    }

    public void Anuluj() { lock (klucz) cts?.Cancel(); }
}
