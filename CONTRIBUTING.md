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
- **Improve the translations** — `Duble.App/web/src/i18n/pl.json` and `en.json` (interface),
  `Duble.Core/i18n/*.json` (verdict reasons used by the report and the CLI).
- **Code** — see below.

## Getting the source to build

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node](https://nodejs.org)
20 or newer, git. Node is for the interface: it is a React application in `Duble.App/web`, and `Duble.App`
builds it with Vite and embeds the result in the executable.

```powershell
git clone --recurse-submodules https://github.com/Bobadu/duble
```

Then open `Duble.sln` in Visual Studio or Rider and build. There is nothing to run first: the only external
dependency, [CodeWalker](https://github.com/dexyfex/CodeWalker) (dexyfex, MIT), is a git submodule in
`external/CodeWalker` pinned to one commit, and both IDEs fetch submodules when they clone. If you cloned without
them, `git submodule update --init --recursive` fixes it - the build tells you so in one sentence.

CodeWalker is referenced as a project, not as a NuGet package (it does not publish one), and it is deliberately
not copied into this repository.

**Working on the interface.** Run its dev server and point the application at it, and an edit appears without
rebuilding anything:

```powershell
cd Duble.App\web ; npm install ; npm run dev       # http://localhost:5173
dotnet run --project Duble.App -- --dev --ui-url http://localhost:5173
```

`--dev` on its own (or the **Duble (developer mode)** profile) uses the interface built into the executable and
turns on DevTools (`F12`). `npm run typecheck` is what CI checks before it builds anything.

From the command line: `dotnet build Duble.sln -c Release` and `dotnet test Duble.Tests -c Release` are
exactly what CI runs — there is no build script to learn. `dotnet publish Duble.App -p:PublishProfile=win-x64`
makes a self-contained single-file executable for yourself; releases ship the Inno Setup installer instead,
which the release workflow compiles from `installer\Duble.iss` over a plain folder publish.

### Tests

```powershell
dotnet test Duble.Tests -c Release
```

The suite runs on a clean clone. Tests that need real clothing packs print `SKIPPED` when the data is
absent. If you have packs to test against, point `DUBLE_TEST_DATA` at the folder holding them (subfolders named
like the packs, e.g. `vrp_clothes_f_civil01`), and `GTAV_ENHANCED` at a GTA V Enhanced installation.

## Project layout

| Folder | What lives here |
|---|---|
| `Duble.Core` | the engine: indexing, fingerprints, comparison, decisions, apply/undo, HTML report, unpacking |
| `Duble.App` | WPF + WebView2 shell; the interface is a React and TypeScript application in `Duble.App/web`, talking to C# over a typed JSON bridge (`web/src/bridge/contract.ts` is that contract) |
| `Duble.Cli` | command line (`duble index / compare / report / apply / undo / calibrate`) |
| `Duble.Tests` | xunit tests for the engine, the bridge commands and the dictionaries |
| `external/CodeWalker` | submodule: dexyfex's CodeWalker, pinned - nothing in here is edited by us |

`docs/how-it-works.md` explains what the engine actually compares and why the thresholds are what they are —
worth reading before changing anything in `Duble.Core/Fingerprints` or `Duble.Core/Comparison`.

## House rules for code

- **The code is written in English** — identifiers, comments and XML docs. Comments explain *why*, not *what*.
  The Polish that remains is the user-facing text in `pl.json`, where it belongs.
- **No text hardcoded in the interface.** Every string goes through `t('key')` with entries in **both**
  `pl.json` and `en.json`. TypeScript knows the keys, and a test fails if one is used and missing, or if the
  two dictionaries drift apart.
- **Nothing crosses the bridge untyped.** Every command and event is declared in `web/src/bridge/contract.ts`;
  a field renamed on one side then fails the build on the other, which is the entire point of it.
- **Nothing is deleted, ever.** Files are moved to the bin and every operation is written to History so Undo can
  restore it. A change that deletes, overwrites or writes inside a `.rpf` archive will not be merged.
- **Match the existing style**: 4 spaces in C#, 2 in TypeScript and CSS, `.editorconfig` covers the rest. Files
  stay focused and reasonably small.
- **Tests** for engine behaviour (comparison, decisions, apply/undo, paths). Interface changes are checked by
  running the app — attach a screenshot to the pull request.

## Commits and pull requests

- One topic per pull request, please.
- **Commit messages are written in English**: a short summary line (72 characters or so) saying what changed
  from the user's point of view, then the details in the body. Commits are signed — GitHub shows them as verified.
- Build and run the tests before pushing — CI runs the same two commands.
- New verdict logic or threshold changes should come with the numbers behind them (`duble calibrate` prints the
  distributions, and Settings → Calibration shows them as charts).

## Reporting security issues

Please do not open a public issue — see [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your work is published under the [MIT license](LICENSE) of this project.
