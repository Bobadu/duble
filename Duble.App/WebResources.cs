// WebResources.cs — the answers to https://duble.app/* (the interface itself) and https://duble.data/*
// (dictionaries, thumbnails, textures, meshes). WebView2 asks; this decides.
//
// The interface is served from the files embedded in the executable, or, in developer mode, from a folder on
// disk so that a reload shows the edit. Data comes from the session through the Data delegate, except for
// i18n, which is Core's dictionary merged with the interface's own.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Duble.App;

/// <summary>One answer: the bytes and what they are. No response means 404.</summary>
public sealed record WebResource(Stream Content, string Mime);

public sealed class WebResources
{
    readonly string? uiFolder;

    /// <summary>"index.html" -> the logical name of the embedded resource.</summary>
    readonly Dictionary<string, string> embedded = new(StringComparer.OrdinalIgnoreCase);

    public WebResources(string? uiFolder)
    {
        this.uiFolder = uiFolder == null ? null
            : Path.GetFullPath(uiFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var name in typeof(WebResources).Assembly.GetManifestResourceNames())
            if (name.StartsWith("ui/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ui\\", StringComparison.OrdinalIgnoreCase))
                embedded[name.Substring(3).Replace('\\', '/')] = name;
    }

    /// <summary>(category, key, query without '?') -> bytes, or null. Categories: thumb, tex, mesh. The
    /// session sets this; without it duble.data serves nothing but dictionaries.</summary>
    public Func<string, string, string?, Stream?>? Data { get; set; }

    /// <summary>Whether the interface is being read from a folder rather than from the executable.</summary>
    public bool FromFolder => uiFolder != null;

    public WebResource? Resolve(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        if (uri.Host.Equals("duble.app", StringComparison.OrdinalIgnoreCase)) return Interface(path);
        if (uri.Host.Equals("duble.data", StringComparison.OrdinalIgnoreCase)) return Asset(path, uri.Query.TrimStart('?'));
        return null;
    }

    WebResource? Interface(string path)
    {
        if (path.Length == 0) path = "index.html";
        if (path.Contains("..")) return null;
        var mime = Mime(path);

        if (uiFolder != null)
        {
            var full = Path.GetFullPath(Path.Combine(uiFolder, path));
            if (!full.StartsWith(uiFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            return new WebResource(new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), mime);
        }

        if (!embedded.TryGetValue(path, out var name)) return null;
        var content = typeof(WebResources).Assembly.GetManifestResourceStream(name);
        return content == null ? null : new WebResource(content, mime);
    }

    WebResource? Asset(string path, string query)
    {
        var parts = path.Split('/', 2);
        if (parts.Length < 2 || parts[1].Length == 0) return null;

        var category = parts[0];
        var key = Path.GetFileNameWithoutExtension(parts[1]);
        if (category == "i18n")
            return new WebResource(new MemoryStream(Encoding.UTF8.GetBytes(Translations(key))), "application/json; charset=utf-8");

        var content = Data?.Invoke(category, key, query);
        return content == null ? null : new WebResource(content, Mime(parts[1]));
    }

    /// <summary>Core's dictionary merged with the interface's, the interface winning where both have a key.</summary>
    public string Translations(string language)
    {
        var merged = new Dictionary<string, string>(Duble.Core.Comparison.Texts.Dictionary(language));
        if (Resolve($"https://duble.app/i18n/{language}.json") is { } file)
            using (file.Content)
            {
                var ui = JsonSerializer.Deserialize<Dictionary<string, string>>(file.Content) ?? new Dictionary<string, string>();
                foreach (var entry in ui) merged[entry.Key] = entry.Value;
            }
        return JsonSerializer.Serialize(merged, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static string Mime(string path) => (Path.GetExtension(path) ?? "").ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".ico" => "image/x-icon",
        ".glb" => "model/gltf-binary",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream",
    };
}
