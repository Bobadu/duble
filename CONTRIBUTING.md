# Contributing to Duble

Thanks for wanting to help. Duble is a small, focused tool: it finds clothing packs that contain the **same
garment twice** and lets you clean that up **without ever deleting a file**. Every contribution is measured
against those two promises.

## Ways to help

- **Report a bug** — [open an issue](https://github.com/Bobadu/duble/issues/new/choose). A screenshot of
  the screen where it went wrong and the name of the pack are worth more than a long description.
- **Report a wrong verdict** — the interesting bugs live here: two garments called duplicates that are not,
  or a real duplicate that Duble missed. Please include both item names, the source packs and a screenshot of
  the comparison card (it shows the reason and the numbers behind the verdict).
- **Improve the translations** — `Duble.App/ui/i18n/pl.json` and `en.json` (interface),
  `Duble.Core/i18n/*.json` (verdict reasons used by the report and the CLI).
- **Code** — see below.

## Getting the source to build

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), git.

```powershell
git clone --recurse-submodules https://github.com/Bobadu/duble
```

Then open `Duble.sln` in Visual Studio or Rider and build. There is nothing to run first: the only external
dependency, [CodeWalker](https://github.com/dexyfex/CodeWalker) (dexyfex, MIT), is a git submodule in
`external/CodeWalker` pinned to one commit, and both IDEs fetch submodules when they clone. If you cloned without
them, `git submodule update --init --recursive` fixes it - the build tells you so in one sentence.

CodeWalker is referenced as a project, not as a NuGet package (it does not publish one), and it is deliberately
not copied into this repository.

**Developer mode** - run `Duble.App` with the **Duble (developer mode)** profile (or `Duble.exe --dev`): the
interface is read from `Duble.App/ui` instead of the resources embedded in the executable, so HTML/CSS/JS changes
only need a page reload (`Ctrl+R`), and DevTools (`F12`) are available.

From the command line: `dotnet build Duble.sln -c Release`, `dotnet test Duble.Tests -c Release` and
`dotnet publish Duble.App -p:PublishProfile=win-x64` for the shipping single-file executable. That is exactly
what CI runs — there is no build script to learn.

### Tests

```powershell
dotnet test Duble.Tests -c Release
```

The suite runs on a clean clone. Tests that need real clothing packs print `POMINIETY` (skipped) when the data is
absent. If you have packs to test against, point `DUBLE_TEST_DATA` at the folder holding them (subfolders named
like the packs, e.g. `vrp_clothes_f_civil01`), and `GTAV_ENHANCED` at a GTA V Enhanced installation.

## Project layout

| Folder | What lives here |
|---|---|
| `Duble.Core` | the engine: indexing, fingerprints, comparison, decisions, apply/undo, HTML report, unpacking |
| `Duble.App` | WPF + WebView2 shell; the whole interface is `Duble.App/ui` (HTML/CSS/JS + three.js) talking to C# over a small JSON bridge |
| `Duble.Cli` | command line (`duble indeks / porownaj / raport / zastosuj / cofnij / kalibruj`) |
| `Duble.Tests` | xunit tests for the engine, the bridge commands and the dictionaries |
| `external/CodeWalker` | submodule: dexyfex's CodeWalker, pinned - nothing in here is edited by us |

`docs/how-it-works.md` explains what the engine actually compares and why the thresholds are what they are —
worth reading before changing anything in `Odciski.cs` or `Porownanie.cs`.

## House rules for code

- **The language of the code is Polish.** Types, methods and variables are named in Polish
  (`Rozstrzygniecie`, `zaladujDo`, `warianty`), comments explain *why*, not *what*. Identifiers avoid Polish
  diacritics; comments and user-facing text use them normally. Please keep that consistent — a half-translated
  codebase is worse than either choice.
- **No text hardcoded in the interface.** Every string goes through `t('key')` with entries in **both**
  `pl.json` and `en.json`. A test fails if a key is used and missing, or if the two dictionaries drift apart.
- **Nothing is deleted, ever.** Files are moved to the bin and every operation is written to History so Undo can
  restore it. A change that deletes, overwrites or writes inside a `.rpf` archive will not be merged.
- **Match the existing style**: 4 spaces in C#, 2 in JS/CSS, `.editorconfig` covers the rest. Files stay focused
  and reasonably small; the interface is plain ES modules without a build step or a framework.
- **Tests** for engine behaviour (comparison, decisions, apply/undo, paths). Interface changes are checked by
  running the app — attach a screenshot to the pull request.

## Commits and pull requests

- One topic per pull request, please.
- **Commit messages are written in English**: a short summary line (72 characters or so) saying what changed
  from the user's point of view, then the details in the body. Commits are signed — GitHub shows them as verified.
- Build and run the tests before pushing — CI runs the same two commands.
- New verdict logic or threshold changes should come with the numbers behind them (`duble kalibruj` prints the
  distributions, and Settings → Calibration shows them as charts).

## Reporting security issues

Please do not open a public issue — see [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your work is published under the [MIT license](LICENSE) of this project.
