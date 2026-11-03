using System.IO;

namespace Idk.Services;

internal static class CrashLog
{
    private static string LogPath => Path.Combine(SettingsStore.DataDirectory, "crash.log");

    public static void Write(Exception ex, string context = "")
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            var header = string.IsNullOrWhiteSpace(context)
                ? $"[{DateTime.Now:u}]"
                : $"[{DateTime.Now:u}] {context}";
            File.AppendAllText(LogPath, header + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            /* ignore logging failures */
        }
    }
}
