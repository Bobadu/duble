#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Duble.Core.Comparison;

/// <summary>
/// Why a verdict came out the way it did, as a code plus parameters rather than a finished sentence — the app
/// is bilingual, so the engine must not decide which language to speak.
///
/// A parameter value starting with '@' is itself a key to translate, for example "@geo.identyczna". Numbers
/// arrive already formatted with the invariant culture.
/// </summary>
public class Reason
{
    public string? Code { get; set; }

    /// <summary>Parameters by name, substituted into the template.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    public Reason() { }

    public Reason(string code, params (string key, object value)[] parameters)
    {
        Code = code;
        foreach (var (key, value) in parameters)
            Parameters[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    public override string ToString() => Texts.Reason(this, "pl");
}

/// <summary>Turns a reason into a sentence in one of the languages Duble speaks.</summary>
public interface IReasonFormatter
{
    string Format(Reason? reason, string language);
}

/// <inheritdoc />
public sealed class ReasonFormatter : IReasonFormatter
{
    public string Format(Reason? reason, string language) => Texts.Reason(reason, language);
}

/// <summary>
/// The engine's own dictionaries: verdict names, reason sentences and the quality breakdown, in Polish and
/// English. The Polish text has to reproduce what Duble printed before the rewrite character for character —
/// the golden master test checks exactly that.
/// </summary>
public static class Texts
{
    public static readonly string[] Languages = { "pl", "en" };

    static readonly ConcurrentDictionary<string, Dictionary<string, string>> Dictionaries = new();
    static readonly Regex Parameter = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public static Dictionary<string, string> Dictionary(string language)
        => Dictionaries.GetOrAdd(Normalise(language), Load);

    static string Normalise(string? language)
        => string.IsNullOrEmpty(language) ? "pl" : language.ToLowerInvariant().StartsWith("pl") ? "pl" : "en";

    static Dictionary<string, string> Load(string language)
    {
        var name = $"Duble.Core.i18n.{language}.json";
        using var stream = typeof(Texts).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException("missing resource " + name);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }

    /// <summary>The text under a key: missing in English falls back to Polish, missing in Polish gives "[key]".</summary>
    public static string T(string language, string key, IReadOnlyDictionary<string, string>? parameters = null)
    {
        language = Normalise(language);
        if (!Dictionary(language).TryGetValue(key, out var template) && !Dictionary("pl").TryGetValue(key, out template))
            template = "[" + key + "]";

        if (parameters == null || parameters.Count == 0) return template;

        return Parameter.Replace(template, match =>
        {
            if (!parameters.TryGetValue(match.Groups[1].Value, out var value) || value == null) return match.Value;
            return value.StartsWith("@") ? T(language, value[1..]) : value;
        });
    }

    public static string Reason(Reason? reason, string language)
        => reason?.Code == null ? "" : T(language, "powod." + reason.Code, reason.Parameters);

    /// <summary>The name of a verdict, for the interface and the report.</summary>
    public static string Verdict(Verdict verdict, string language) => T(language, "verdict." + verdict.ToKey());
}
