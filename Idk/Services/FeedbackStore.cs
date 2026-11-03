using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Idk.Services;

/// <summary>Appends feature feedback to a local log under the idk data folder.</summary>
public static class FeedbackStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static string FilePath => Path.Combine(SettingsStore.DataDirectory, "feedback.jsonl");

    public static void Append(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Length < 3)
            throw new ArgumentException("Please enter a short message.");

        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var entry = new FeedbackEntry(
            DateTime.UtcNow,
            trimmed,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");
        File.AppendAllText(FilePath, JsonSerializer.Serialize(entry, JsonOpts) + Environment.NewLine);
    }
}

public record FeedbackEntry(DateTime Utc, string Message, string AppVersion);
