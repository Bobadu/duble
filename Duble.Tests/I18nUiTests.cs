using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The dictionaries of the interface (ui\i18n\pl.json and en.json): the same keys on both sides, none of them
/// empty, and every key the interface asks for actually there. Core's own dictionary is merged into these, so
/// some of these tests reach across to it.
/// </summary>
public class I18nUiTests
{
    static string Ui => TestPaths.Ui;

    static Dictionary<string, string> Translations(string language)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Ui, "i18n", language + ".json")));

    static IEnumerable<string> InterfaceFiles(params string[] extensions)
        => Directory.EnumerateFiles(Ui, "*.*", SearchOption.AllDirectories).Where(file => extensions.Any(file.EndsWith));

    [Fact]
    public void Both_languages_have_the_same_keys_and_none_of_them_is_empty()
    {
        var pl = Translations("pl");
        var en = Translations("en");

        Assert.Empty(pl.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(pl.Keys));
        Assert.All(pl.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(en.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    /// <summary>
    /// TypeScript catches a mistyped key at build time — t() takes the keys of pl.json — but only where the
    /// key is written out. This finds the ones that are not in the dictionary at all, whichever way they got
    /// there, and it is also what keeps the engine's own keys (reason., verdict., slot.) from being expected
    /// here: those come from Duble.Core over the bridge.
    /// </summary>
    [Fact]
    public void Every_key_the_interface_asks_for_exists()
    {
        var pl = Translations("pl");
        var pattern = new Regex(@"\bt\(\s*'([a-zA-Z0-9_.]+)'\s*[,)]");

        var missing = new List<string>();
        foreach (var file in InterfaceFiles(".ts", ".tsx"))
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (!pl.ContainsKey(key) && !key.StartsWith("reason.") && !key.StartsWith("verdict.") && !key.StartsWith("slot."))
                    missing.Add(Path.GetFileName(file) + ": " + key);
            }

        Assert.Empty(missing);
    }

    /// <summary>
    /// The interface builds a verdict label as t('verdict.' + key), where the key comes from the Verdict enum
    /// over the bridge. Both halves have to agree, and only Core's dictionary can answer for the second one.
    /// </summary>
    [Fact]
    public void Every_verdict_has_a_label_in_both_languages()
    {
        foreach (var verdict in new[] { Verdict.Duplicate, Verdict.Superset, Verdict.NeedsReview, Verdict.Retexture })
            foreach (var language in Texts.Languages)
            {
                var label = Texts.Verdict(verdict, language);
                Assert.False(label.StartsWith("["), $"{language}: no label for verdict.{verdict.ToKey()}");
            }
    }

    /// <summary>
    /// A long job reports its stage as a key — "models", "textures", "apply" — and the status bar shows
    /// t('stage.' + key). A stage without a label would put an English word in the middle of a Polish
    /// sentence, which is exactly what happened before these keys existed.
    /// </summary>
    [Fact]
    public void Every_progress_stage_has_a_label_in_both_languages()
    {
        var pl = Translations("pl");
        var en = Translations("en");

        foreach (var stage in new[] { "start", "models", "textures", "compare", "apply", "undo", "unpack", "report", "calibration" })
        {
            Assert.True(pl.ContainsKey("stage." + stage), "pl stage." + stage);
            Assert.True(en.ContainsKey("stage." + stage), "en stage." + stage);
        }
    }

    /// <summary>
    /// The bin folder's name belongs to Core, but the interface tells the user what it is — in two i18n values
    /// per language and in two placeholder paths in the JS. Nothing makes those follow the constant, so this
    /// does: renaming it in Core without the interface would leave the settings screen promising a folder that
    /// is never created.
    /// </summary>
    [Fact]
    public void Wherever_the_interface_names_the_bin_folder_it_names_the_current_one()
    {
        foreach (var language in new[] { "pl", "en" })
        {
            var dictionary = Translations(language);
            foreach (var key in new[] { "apply.besideSource", "settings.binBeside" })
                Assert.Contains(BinFolder.Name, dictionary[key]);
        }

        var stale = new List<string>();
        foreach (var file in InterfaceFiles(".ts", ".tsx", ".json"))
        {
            var text = File.ReadAllText(file);
            // any underscore-prefixed folder that looks like a bin but is not the one Core writes
            foreach (Match match in Regex.Matches(text, @"_[a-z]{6,12}(?=\\\\|\\\\<|\s|\)|<)"))
                if (match.Value is "_odrzucone" or "_rejected" && match.Value != BinFolder.Name)
                    stale.Add($"{Path.GetFileName(file)}: {match.Value}");
        }

        Assert.Empty(stale);
    }

    [Fact]
    public void Every_slot_has_a_name_in_both_languages()
    {
        var pl = Translations("pl");
        var en = Translations("en");

        foreach (var slot in new[]
                 {
                     "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib",
                     "p_head", "p_eyes", "p_ears", "p_mouth", "p_lhand", "p_rhand", "p_lwrist", "p_rwrist", "p_hip",
                 })
        {
            Assert.True(pl.ContainsKey("slot." + slot), "pl slot." + slot);
            Assert.True(en.ContainsKey("slot." + slot), "en slot." + slot);
        }
    }

    /// <summary>
    /// The placeholders of a sentence are part of its meaning: `{name}` filled from a `name` given at the call
    /// site. A translation that spells one differently drops the value silently and prints the braces, and this
    /// whole stage renamed every one of them, so the two dictionaries are compared placeholder by placeholder.
    /// </summary>
    [Fact]
    public void Both_languages_use_the_same_placeholders_in_every_sentence()
    {
        var pl = Translations("pl");
        var en = Translations("en");
        // {name} writes the value out; {name|form|form|form} only chooses the form of the noun it counts
        var placeholder = new Regex(@"\{([a-zA-Z][a-zA-Z0-9]*)[|}]");

        SortedSet<string> Placeholders(string text)
            => new(placeholder.Matches(text).Select(match => match.Groups[1].Value));

        var different = pl.Keys
            .Where(key => !Placeholders(pl[key]).SetEquals(Placeholders(en[key])))
            .Select(key => $"{key}: pl {string.Join(",", Placeholders(pl[key]))} / en {string.Join(",", Placeholders(en[key]))}")
            .ToList();

        Assert.Empty(different);
    }

    /// <summary>
    /// A counted noun is written `{n|pozycja|pozycje|pozycji}`: one form per plural category, and the count of
    /// forms is what the interface asks the language for. Polish needs three, English two — a Polish sentence
    /// written with two forms would silently print "2 pozycja".
    /// </summary>
    [Fact]
    public void Every_counted_noun_has_a_form_for_each_category_of_its_language()
    {
        var forms = new Regex(@"\{[a-zA-Z][a-zA-Z0-9]*\|([^{}]*)\}");
        var expected = new Dictionary<string, int> { ["pl"] = 3, ["en"] = 2 };

        var wrong = new List<string>();
        foreach (var (language, count) in expected)
            foreach (var (key, text) in Translations(language))
                foreach (Match match in forms.Matches(text))
                    if (match.Groups[1].Value.Split('|').Length != count)
                        wrong.Add($"{language} {key}: {match.Value}");

        Assert.Empty(wrong);
    }
}
