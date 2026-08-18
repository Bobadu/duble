using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>project.new / project.recent — ksztalt tego, co ekran startowy dostaje przez mostek.</summary>
public class ProjektyKomendyTests
{
    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement;
    static async Task<JsonElement> Wywolaj(Mostek m, string cmd, string args = "null")
        => Odp(await m.Obsluz($"{{\"id\":\"1\",\"cmd\":\"{cmd}\",\"args\":{args}}}")).GetProperty("result");

    /// <summary>
    /// Karta ostatniego projektu pokazuje `o.nazwa`. Pole bralo sie ze skrotu `new { o.Name, ... }`, wiec po
    /// przemianowaniu wlasciwosci w C# przyszlo do UI jako `name` i nazwa zniknela z ekranu startowego.
    /// </summary>
    [Fact]
    public async Task Ostatnie_projekty_maja_nazwe_sciezke_i_date()
    {
        var tmp = Sciezki.Tymczasowy("projekty");
        try
        {
            var wyslane = new List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add)
            {
                PlikUstawien = Path.Combine(tmp, "settings.json"),
            };
            var s = TestSession.Create();
            Duble.App.Komendy.Projekty.Zarejestruj(m, s);

            var utworzony = await Wywolaj(m, "project.new",
                $"{{\"nazwa\":\"Test Project\",\"folder\":{JsonSerializer.Serialize(tmp)}}}");
            Assert.Equal("Test Project", utworzony.GetProperty("projekt").GetProperty("nazwa").GetString());

            var ostatnie = (await Wywolaj(m, "project.recent")).GetProperty("ostatnie");
            var wpis = Assert.Single(ostatnie.EnumerateArray());

            Assert.Equal("Test Project", wpis.GetProperty("nazwa").GetString());
            Assert.Equal(Path.Combine(tmp, "Test Project.duble"), wpis.GetProperty("sciezka").GetString());
            Assert.True(wpis.GetProperty("istnieje").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(wpis.GetProperty("ostatnio").GetString()));
        }
        finally { Directory.Delete(tmp, true); }
    }
}
