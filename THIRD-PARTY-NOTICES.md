# Third-party notices

Duble itself is MIT licensed — see [LICENSE](LICENSE). It ships or builds against the components below, each
under its own licence. The same list is shown in the application, under **About**.

| Component | Licence | What it does here |
|---|---|---|
| [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) by dexyfex | MIT | Reads `.rpf` archives and the `.ydd` / `.ytd` resources inside them |
| [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) | MIT | Decodes BC7 textures, which CodeWalker leaves undecoded |
| [three.js](https://threejs.org) | MIT | Draws the 3D preview of a garment |
| [Microsoft Edge WebView2 SDK](https://developer.microsoft.com/microsoft-edge/webview2/) | Microsoft Software License Terms | Hosts the interface inside the desktop window |
| [.NET and WPF](https://github.com/dotnet/wpf) | MIT | The runtime and the window the application is built on |

CodeWalker.Core is a git submodule under `external/CodeWalker`; the rest arrive as NuGet packages or as
vendored files under `Duble.App/ui/vendor`.

## Why this is a separate file

It used to sit at the bottom of `LICENSE`. GitHub only recognises a licence when that file holds the licence
text and nothing else, so the extra section made the repository read as "Other" rather than MIT.
