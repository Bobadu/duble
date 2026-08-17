using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Duble.Tests;

/// <summary>Slowniki UI (ui\i18n\pl.json, en.json): te same klucze, brak pustych, kazdy klucz uzyty w JS/HTML istnieje.</summary>
public class I18nUiTests
{
    static string Ui => Sciezki.Ui;
    static Dictionary<string, string> Slownik(string j) => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Ui, "i18n", j + ".json")));

    [Fact]
    public void Pl_i_en_maja_te_same_klucze_i_zadnych_pustych()
    {
        var pl = Slownik("pl"); var en = Slownik("en");
        Assert.Empty(pl.Keys.Except(en.Keys)); Assert.Empty(en.Keys.Except(pl.Keys));
        Assert.All(pl.Values, v => Assert.False(string.IsNullOrWhiteSpace(v))); Assert.All(en.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Kazdy_klucz_uzyty_w_ui_istnieje_w_slowniku()
    {
        var pl = Slownik("pl");
        // t('klucz') albo t('klucz', {...}); klucze skladane dynamicznie (t('nav.' + id)) sprawdzaja testy slotow/nazw
        var re = new Regex(@"(?:\bt\(\s*'([a-zA-Z0-9_.]+)'\s*[,)]|data-i18n(?:-title|-placeholder|-aria)?=""([a-zA-Z0-9_.]+)"")");
        var brak = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Ui, "*.*", SearchOption.AllDirectories).Where(f => (f.EndsWith(".js") || f.EndsWith(".html")) && !f.Contains(Path.DirectorySeparatorChar + "vendor" + Path.DirectorySeparatorChar)))
            foreach (Match m in re.Matches(File.ReadAllText(f)))
            {
                var k = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (!pl.ContainsKey(k) && !k.StartsWith("powod.") && !k.StartsWith("werdykt.") && !k.StartsWith("slot.")) brak.Add(Path.GetFileName(f) + ": " + k);
            }
        Assert.Empty(brak);
    }

    [Fact]
    public void Sloty_maja_tlumaczenia()
    {
        var pl = Slownik("pl"); var en = Slownik("en");
        foreach (var typ in new[] { "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib", "p_head", "p_eyes", "p_ears", "p_mouth", "p_lhand", "p_rhand", "p_lwrist", "p_rwrist", "p_hip" })
        {
            Assert.True(pl.ContainsKey("slot." + typ), "pl slot." + typ);
            Assert.True(en.ContainsKey("slot." + typ), "en slot." + typ);
        }
    }
}
