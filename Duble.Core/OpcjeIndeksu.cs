// OpcjeIndeksu.cs — parametry indeksowania dla aplikacji: postep, anulowanie, przyrostowosc, miniatury.
using System;
using System.Threading;

namespace Duble;

public sealed record Postep(string Etap, int Zrobione, int Wszystkie, string Kontener);

public class OpcjeIndeksu
{
    public Action<string> Log { get; set; } = _ => { };
    public Action<Postep> Postep { get; set; }
    public CancellationToken Anuluj { get; set; }
    /// <summary>Poprzedni katalog: pozycje/tekstury o tej samej sciezce i znaczniku sa brane z niego bez liczenia.</summary>
    public Katalog Poprzedni { get; set; }
    /// <summary>Ignoruj Poprzedni — przelicz wszystko.</summary>
    public bool Wymus { get; set; }
    /// <summary>Gdy ustawiony: przy dekodowaniu tekstury zapisujemy &lt;sha&gt;.png (128 px, alfa na szachownicy).</summary>
    public string FolderMiniatur { get; set; }
    /// <summary>Ile plikow na jedna porcje rownolegla (miedzy porcjami: postep + sprawdzenie anulowania).</summary>
    public int Porcja { get; set; } = 200;
}
