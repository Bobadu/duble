using System.IO;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class WebResourcesTests
{
    [Fact]
    public void The_interface_is_served_from_a_folder_and_nothing_outside_it()
    {
        var temp = TestPaths.Temp("web");
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "views"));
            File.WriteAllText(Path.Combine(temp, "index.html"), "<title>Duble</title>");
            File.WriteAllText(Path.Combine(temp, "views", "a.js"), "export const x = 1;");
            var resources = new WebResources(temp);

            var page = resources.Resolve("https://duble.app/index.html");
            Assert.NotNull(page);
            Assert.Equal("text/html; charset=utf-8", page.Mime);
            using (page.Content) Assert.Equal("<title>Duble</title>", new StreamReader(page.Content).ReadToEnd());

            var script = resources.Resolve("https://duble.app/views/a.js?v=3");
            Assert.NotNull(script);
            Assert.Equal("text/javascript; charset=utf-8", script.Mime);
            script.Content.Dispose();

            var root = resources.Resolve("https://duble.app/");   // = index.html
            Assert.NotNull(root);
            root.Content.Dispose();

            Assert.Null(resources.Resolve("https://duble.app/not-there.js"));
            Assert.Null(resources.Resolve("https://duble.app/../Duble.App.csproj"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void The_interface_is_served_from_the_executable_when_there_is_no_folder()
    {
        var page = new WebResources(null).Resolve("https://duble.app/index.html");

        Assert.NotNull(page);
        Assert.Contains("Duble", new StreamReader(page.Content).ReadToEnd());
    }

    [Fact]
    public void Data_goes_through_the_delegate_and_i18n_merges_Core_with_the_interface()
    {
        var resources = new WebResources(null);

        var dictionary = resources.Resolve("https://duble.data/i18n/pl.json");
        Assert.NotNull(dictionary);
        var json = new StreamReader(dictionary.Content).ReadToEnd();
        Assert.Contains("\"reason.SAME_MODEL_SAME_TEX\"", json);   // from Core
        Assert.Contains("\"app.name\"", json);                     // from ui\i18n\pl.json

        string lastQuery = null;
        resources.Data = (category, key, query) =>
        {
            lastQuery = query;
            return category == "thumb" && key == "ABC" ? new MemoryStream(new byte[] { 1, 2, 3 }) : null;
        };

        var thumbnail = resources.Resolve("https://duble.data/thumb/ABC.png?w=b");
        Assert.NotNull(thumbnail);
        Assert.Equal("image/png", thumbnail.Mime);
        Assert.Equal("w=b", lastQuery);

        Assert.Null(resources.Resolve("https://duble.data/thumb/XYZ.png"));
        Assert.Null(resources.Resolve("https://other.host/x"));
    }

    [Theory]
    [InlineData("a.html", "text/html; charset=utf-8")]
    [InlineData("a.js", "text/javascript; charset=utf-8")]
    [InlineData("a.mjs", "text/javascript; charset=utf-8")]
    [InlineData("a.css", "text/css; charset=utf-8")]
    [InlineData("a.json", "application/json; charset=utf-8")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.glb", "model/gltf-binary")]
    [InlineData("a.woff2", "font/woff2")]
    [InlineData("a.xyz", "application/octet-stream")]
    public void The_content_type_comes_from_the_extension(string file, string mime) => Assert.Equal(mime, WebResources.Mime(file));
}
