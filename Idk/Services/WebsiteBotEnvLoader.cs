using System.Collections.Generic;
using System.IO;

namespace Idk.Services;

/// <summary>
/// Loads <c>Desktop\website bot\.env</c> (same layout as Website Bot) into process environment
/// before <see cref="SettingsStore"/> so idk picks up OpenRouter key, MiniMax text model, and vision model.
/// </summary>
public static class WebsiteBotEnvLoader
{
    /// <summary>User Desktop, folder name matches Website Bot repo.</summary>
    public static string DefaultEnvPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "website bot", ".env");

    /// <summary>Applies variables from the file if it exists. Does not throw.</summary>
    public static void ApplyIfPresent(string? envFilePath = null)
    {
        var path = string.IsNullOrWhiteSpace(envFilePath) ? DefaultEnvPath() : envFilePath;
        try
        {
            if (!File.Exists(path)) return;
            var vars = ParseEnvFile(path);
            ApplyMappings(vars);
        }
        catch
        {
            /* ignore malformed .env */
        }
    }

    private static Dictionary<string, string> ParseEnvFile(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            var val = Unquote(line[(eq + 1)..].Trim());
            d[key] = val;
        }

        return d;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2)
        {
            if ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))
                return s[1..^1].Trim();
        }

        return s.Trim();
    }

    private static void SetEnv(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Environment.SetEnvironmentVariable(name, value.Trim());
    }

    private static void ApplyMappings(Dictionary<string, string> v)
    {
        // API keys are entered in the idk dashboard only (not loaded from .env).

        // Text / MiniMax scan model
        if (v.TryGetValue("OPENROUTER_MINIMAX_MODEL", out var mini) && !string.IsNullOrWhiteSpace(mini))
        {
            SetEnv("OPENROUTER_MINIMAX_MODEL", mini);
            SetEnv("IDK_TEXT_MODEL", mini);
        }

        if (v.TryGetValue("IDK_TEXT_MODEL", out var idkText) && !string.IsNullOrWhiteSpace(idkText))
            SetEnv("IDK_TEXT_MODEL", idkText);

        // Vision — SiteDrop / env.example uses OPENROUTER_SITE_DROP_VISION_MODEL; also OPENROUTER_VISION_MODEL
        if (v.TryGetValue("OPENROUTER_SITE_DROP_VISION_MODEL", out var vis) && !string.IsNullOrWhiteSpace(vis))
        {
            SetEnv("OPENROUTER_SITE_DROP_VISION_MODEL", vis);
            SetEnv("IDK_VISION_MODEL", vis);
        }
        else if (v.TryGetValue("OPENROUTER_VISION_MODEL", out var vis2) && !string.IsNullOrWhiteSpace(vis2))
        {
            SetEnv("OPENROUTER_VISION_MODEL", vis2);
            SetEnv("IDK_VISION_MODEL", vis2);
        }

        if (v.TryGetValue("IDK_VISION_MODEL", out var idkVis) && !string.IsNullOrWhiteSpace(idkVis))
            SetEnv("IDK_VISION_MODEL", idkVis);
    }
}
