# Duble — by Bobadu

**Duplikaty ubrań do GTA V (Legacy i Enhanced) — szybko, bezpiecznie, z podglądem.**

[English version → README.md](README.md) · Strona: <https://qorion.net/duble> · Kod: <https://github.com/qorion-net/duble>

Duble przegląda paczki ubrań (foldery, archiwa `.rpf`, zasoby FiveM), znajduje ubrania, które są **tym samym
modelem z tymi samymi teksturami**, pokazuje je obok siebie w 2D i 3D i pozwala zdecydować, którą wersję zostawić.
Nic nie znika bez Twojej decyzji: odrzucone pliki są **przenoszone** do kosza obok źródła, a jedno kliknięcie
(**Cofnij**) przywraca je z powrotem.

> Zasada nr 1: **duplikat = ten sam model + te same tekstury**. Ten sam model z inną teksturą to **przemalowanie**
> — osobny ciuch, którego Duble nigdy nie proponuje do usunięcia.

## Wymagania

- Windows 10/11 (64-bit).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — na Windows 11
  jest zawsze; na Windows 10 zwykle też (instaluje go Edge/Office). Gdy go brak, Duble pokaże komunikat z linkiem.
- Pliki gry nie są potrzebne — Duble czyta paczki ubrań (`.ydd` / `.ytd`, także wewnątrz `.rpf`).

## Pobranie i uruchomienie

1. Pobierz `Duble.exe` (jeden plik, ok. 90 MB — zawiera .NET) z zakładki **Releases** repozytorium.
2. Uruchom. Windows SmartScreen może ostrzec, że aplikacja jest niepodpisana: „Więcej informacji → Uruchom mimo to".
3. Ustawienia programu trafiają do `%AppData%\Bobadu\Duble\`, projekty domyślnie do `Dokumenty\Duble\`.

## Jak używać (krok po kroku)

1. **Start → Nowy projekt.** Projekt to Twój zestaw paczek + wyniki porównania + decyzje w jednym pliku `.duble`
   (obok leży folder `.duble.cache` z miniaturami i podglądami — można go skasować, odbuduje się).
2. **Źródła.** Dodaj folder z paczkami, plik `.rpf` albo folder zasobu FiveM (można przeciągnąć z Eksploratora;
   „Znajdź gry" proponuje foldery modów z zainstalowanej GTA V). Kliknij **Indeksuj wszystko** — Duble odczyta
   modele i tekstury, policzy odciski (kształt modelu, wygląd i kolor tekstur) i od razu porówna wszystko ze wszystkim.
   Ponowne indeksowanie jest przyrostowe (tylko zmienione pliki).
3. **Duplikaty.** Lista **grup** — każda to ubrania uznane za te same. Werdykty:
   - **Duplikat** — ten sam model i te same tekstury (Duble proponuje zostawić lepszą wersję: wyższa rozdzielczość,
     mipmapy, więcej wariantów kolorów, poprawny format, więcej LOD-ów — to „ocena jakości" 0–100),
   - **Duplikat-nadzbiór** — ten sam model, jeden zestaw tekstur zawiera drugi,
   - **Do wglądu** — model podobny, ale nie identyczny (obejrzyj sam),
   - **Przemalowanie** — ten sam model, inne tekstury (nic nie jest proponowane do odrzucenia).
   Kliknij grupę → **karta porównania**: tekstury obok siebie (pary „ta sama grafika" połączone; kliknięcie = duży
   podgląd z suwakiem A↔B), zakładka **Model (3D)** (obrót zsynchronizowany, „nałóż A na B", warianty, siatka).
   Decyzje: **Zostaw tę**, **Odrzuć / Zachowaj** per pozycja, **To nie duplikat**, notatka. Decyzje zapisują się w
   projekcie — nic jeszcze nie dzieje się na dysku.
4. **Zastosuj** (pasek na dole Duplikatów). Dialog pokazuje dokładnie, które pliki i dokąd zostaną **przeniesione**
   (kosz `_odrzucone` obok źródła albo wskazany folder). Pliki współdzielone z ubraniem, które zostaje, oraz pliki
   wewnątrz archiwów `.rpf` są pomijane. Po zastosowaniu Duble sam ponownie indeksuje i porównuje.
5. **Historia.** Każde Zastosuj to wpis: **Cofnij wszystko** albo pojedynczą pozycję. Tu też eksport **raportu HTML**
   (samowystarczalny plik z miniaturami) i **CSV**.
6. **Katalog.** Wszystkie zaindeksowane ubrania jako siatka miniatur z filtrami (źródło, slot, Legacy/Enhanced,
   „z problemami": brak mipmap, BC1 z alfą; „w grupach duplikatów"). Kliknięcie → karta pozycji z teksturami i 3D.
7. **Ustawienia.** Język (PL/EN), motyw, kosz, **progi porównania** (z opisem skąd się wzięły; zmiana = ponowne
   porównanie), **Kalibracja** (rozkłady odległości na Twoim katalogu jako wykresy — czy progi pasują do Twoich
   paczek), pamięć podręczna.

### Legacy, Enhanced, FiveM, archiwa

- Duble czyta oba formaty (Legacy `.ydd` v165 / `.ytd` v13, Enhanced v159 / v5) i pokazuje znaczek przy każdej pozycji.
- Zasoby FiveM: pliki ze `stream\` (także `nazwa^plik.ydd`) i archiwa `.rpf` w środku.
- **Archiwa `.rpf` są tylko do podglądu** — Duble do nich nie pisze. Żeby porządkować paczkę z archiwum, użyj
  **Źródła → „…" → Rozpakuj do folderu**: powstaje kopia z archiwami rozłożonymi na foldery (`nazwa.rpf\`, pliki RSC7
  jak z OpenIV/CodeWalker), którą można dodać jako źródło i porządkować (Zastosuj/Cofnij), a potem spakować własnym
  narzędziem.

### Co po usunięciu ubrań? (numeracja)

Gra numeruje ubrania w slocie po kolei (`jbib_000`, `jbib_001`…). Usunięcie zostawia „dziurę" — pod tym numerem nic
się nie wyświetla, reszta działa. W paczkach FiveM/DLC lista ubrań siedzi też w `.ymt`/`.meta` — najbezpieczniej
odbudować go narzędziem, którym paczka była robiona. Szczegóły: dialog Zastosuj → „Co to znaczy?".

## Budowanie ze źródeł

Wymagania: [.NET 10 SDK](https://dotnet.microsoft.com/download), git, Windows.

```powershell
git clone https://github.com/qorion-net/duble
cd duble
.\build.ps1            # klonuje CodeWalker (dexyfex, przypięty commit) do ..\CodeWalker, buduje, uruchamia testy
.\build.ps1 -Publish   # jeden plik publish\Duble.exe (self-contained, win-x64)
.\build.ps1 -Uruchom   # buduje i uruchamia aplikację w trybie deweloperskim (UI z folderu ui\, DevTools)
```

Struktura:

| Folder | Co |
|---|---|
| `Duble.Core` | silnik: indeksowanie, odciski, porównanie, decyzje, zastosowanie/cofanie, raport, rozpakowanie |
| `Duble.App` | aplikacja WPF + WebView2; interfejs w `ui\` (HTML/CSS/JS, three.js), i18n `ui\i18n\pl.json` / `en.json` |
| `Duble.Cli` | narzędzie wiersza poleceń (`duble indeks / porownaj / raport / zastosuj / cofnij / kalibruj …`) |
| `Duble.Tests` | testy xunit (silnik, mostek, komendy, i18n; testy na prawdziwych paczkach pomijane, gdy brak danych) |

Silnik korzysta z [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) (odczyt `.rpf`/`.ydd`/`.ytd`),
[BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) (BC7) i [three.js](https://threejs.org) (3D).

## Licencja

MIT — patrz [LICENSE](LICENSE). Aplikację zaprojektowała i wydaje **Bobadu**.
