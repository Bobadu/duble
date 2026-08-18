using System.IO;
using System.Text;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class ZasobyTests
{
    [Fact]
    public void Serwuje_pliki_ui_z_folderu_i_odmawia_wyjscia_poza_folder()
    {
        var tmp = Sciezki.Tymczasowy("zasoby");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "views"));
            File.WriteAllText(Path.Combine(tmp, "index.html"), "<title>Duble</title>");
            File.WriteAllText(Path.Combine(tmp, "views", "a.js"), "export const x = 1;");
            var z = new Zasoby(tmp);
            Assert.True(z.Rozwiaz("https://duble.app/index.html", out var s, out var mime, out int status));
            Assert.Equal(200, status); Assert.Equal("text/html; charset=utf-8", mime);
            using (s) Assert.Equal("<title>Duble</title>", new StreamReader(s).ReadToEnd());
            Assert.True(z.Rozwiaz("https://duble.app/views/a.js?v=3", out var s2, out mime, out status)); s2.Dispose(); Assert.Equal("text/javascript; charset=utf-8", mime);
            Assert.True(z.Rozwiaz("https://duble.app/", out var s3, out mime, out status)); s3.Dispose(); Assert.Equal(200, status);   // = index.html
            Assert.False(z.Rozwiaz("https://duble.app/nie-ma.js", out _, out _, out status)); Assert.Equal(404, status);
            Assert.False(z.Rozwiaz("https://duble.app/../Duble.App.csproj", out _, out _, out status));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Serwuje_ui_z_zasobow_osadzonych()
    {
        var z = new Zasoby(null);   // osadzone w Duble.dll
        Assert.True(z.Rozwiaz("https://duble.app/index.html", out var s, out var mime, out int status));
        Assert.Equal(200, status);
        var html = new StreamReader(s).ReadToEnd();
        Assert.Contains("Duble", html);
    }

    [Fact]
    public void Dane_ida_przez_delegat_i_i18n_laczy_ui_z_core()
    {
        var z = new Zasoby(null);
        Assert.True(z.Rozwiaz("https://duble.data/i18n/pl.json", out var s, out var mime, out int status));
        var json = new StreamReader(s).ReadToEnd();
        Assert.Contains("\"reason.SAME_MODEL_SAME_TEX\"", json);   // z Core
        Assert.Contains("\"app.name\"", json);                     // z ui\i18n\pl.json
        string ostatniQuery = null;
        z.Dane = (kategoria, klucz, query) => { ostatniQuery = query; return kategoria == "thumb" && klucz == "ABC" ? new MemoryStream(new byte[] { 1, 2, 3 }) : null; };
        Assert.True(z.Rozwiaz("https://duble.data/thumb/ABC.png?w=b", out s, out mime, out status)); Assert.Equal("image/png", mime); Assert.Equal("w=b", ostatniQuery);
        Assert.False(z.Rozwiaz("https://duble.data/thumb/XYZ.png", out _, out _, out status)); Assert.Equal(404, status);
        Assert.False(z.Rozwiaz("https://inna.domena/x", out _, out _, out status)); Assert.Equal(404, status);
    }

    [Theory]
    [InlineData("a.html", "text/html; charset=utf-8")] [InlineData("a.js", "text/javascript; charset=utf-8")] [InlineData("a.mjs", "text/javascript; charset=utf-8")]
    [InlineData("a.css", "text/css; charset=utf-8")] [InlineData("a.json", "application/json; charset=utf-8")] [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.png", "image/png")] [InlineData("a.glb", "model/gltf-binary")] [InlineData("a.woff2", "font/woff2")] [InlineData("a.xyz", "application/octet-stream")]
    public void Mime_po_rozszerzeniu(string plik, string mime) => Assert.Equal(mime, Zasoby.Mime(plik));
}
