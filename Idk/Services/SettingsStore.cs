using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Idk.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public const string DefaultTextModel = "minimax/minimax-m2.7";
    public const string DefaultVisionModel = "meta-llama/llama-3.2-11b-vision-instruct";

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "idk");
    private static string FilePath => Path.Combine(Dir, "idk-settings.json");

    /// <summary>Per-user idk folder under %AppData% (settings, chat history, etc.).</summary>
    public static string DataDirectory => Dir;

    /// <summary>When false, requests use text-only context (no display screenshot).</summary>
    public static bool IsScreenCaptureEnabled { get; private set; }

    /// <summary>When true, overlay pill/card stay opaque; when false, idle overlay is more transparent.</summary>
    public static bool IsOverlaySolidOnly { get; private set; }

    /// <summary>When true, dashboard / intro windows use <c>ShowInTaskbar=false</c> (use tray “Open dashboard” while idk is on).</summary>
    public static bool HideFromTaskbar { get; private set; }

    /// <summary>Legacy flag: true when <see cref="SelectionMode"/> is <see cref="SelectionActivationMode.Instant"/>.</summary>
    public static bool ActivateOnAnySelection => SelectionMode == SelectionActivationMode.Instant;

    /// <summary>Dashboard and shell color theme.</summary>
    public static AppTheme AppTheme { get; private set; } = AppTheme.Dark;

    /// <summary>How text selection triggers the overlay. Default is Shift + select.</summary>
    public static SelectionActivationMode SelectionMode { get; private set; } = SelectionActivationMode.ShiftSelect;

    public static int SelectionHotkeyVk { get; private set; }

    public static int SelectionHotkeyModifiers { get; private set; }

    private class FileDto
    {
        [JsonPropertyName("openrouterApiKey")]
        public string? OpenrouterApiKey { get; set; }
        [JsonPropertyName("idkTextModel")]
        public string? IdkTextModel { get; set; }
        [JsonPropertyName("idkVisionModel")]
        public string? IdkVisionModel { get; set; }
        [JsonPropertyName("screenCaptureEnabled")]
        public bool? ScreenCaptureEnabled { get; set; }
        [JsonPropertyName("overlaySolidOnly")]
        public bool? OverlaySolidOnly { get; set; }
        [JsonPropertyName("hideFromTaskbar")]
        public bool? HideFromTaskbar { get; set; }
        [JsonPropertyName("activateOnAnySelection")]
        public bool? ActivateOnAnySelection { get; set; }
        [JsonPropertyName("selectionActivationMode")]
        public string? SelectionActivationMode { get; set; }
        [JsonPropertyName("selectionHotkeyVk")]
        public int? SelectionHotkeyVk { get; set; }
        [JsonPropertyName("selectionHotkeyModifiers")]
        public int? SelectionHotkeyModifiers { get; set; }
        [JsonPropertyName("theme")]
        public string? Theme { get; set; }
    }

    private static void SyncScreenCaptureFrom(FileDto cur) =>
        IsScreenCaptureEnabled = cur.ScreenCaptureEnabled ?? false;

    private static void SyncOverlaySolidFrom(FileDto cur) =>
        IsOverlaySolidOnly = cur.OverlaySolidOnly == true;

    private static void SyncHideFromTaskbarFrom(FileDto cur) =>
        HideFromTaskbar = cur.HideFromTaskbar == true;

    private static void SyncThemeFrom(FileDto cur)
    {
        if (!string.IsNullOrWhiteSpace(cur.Theme)
            && Enum.TryParse<AppTheme>(cur.Theme, true, out var parsed))
        {
            AppTheme = parsed;
            return;
        }

        AppTheme = AppTheme.Dark;
    }

    private static void SyncSelectionModeFrom(FileDto cur)
    {
        if (!string.IsNullOrWhiteSpace(cur.SelectionActivationMode)
            && Enum.TryParse<SelectionActivationMode>(cur.SelectionActivationMode, true, out var parsed))
        {
            SelectionMode = parsed;
        }
        else
        {
            SelectionMode = cur.ActivateOnAnySelection == true
                ? SelectionActivationMode.Instant
                : SelectionActivationMode.ShiftSelect;
        }

        SelectionHotkeyVk = cur.SelectionHotkeyVk ?? 0;
        SelectionHotkeyModifiers = cur.SelectionHotkeyModifiers ?? 0;
    }

    public static string GetSelectionModeDisplayName() =>
        SelectionMode switch
        {
            SelectionActivationMode.Instant => "Instant Ask",
            SelectionActivationMode.ShiftSelect => "Shift + select",
            SelectionActivationMode.CustomHotkey when SelectionHotkeyVk > 0 =>
                FormatHotkey(SelectionHotkeyVk, SelectionHotkeyModifiers),
            SelectionActivationMode.CustomHotkey => "Custom hotkey",
            _ => "Shift + select"
        };

    public const string AskBarPlaceholder = "Ask Anything!";

    public const string DefaultQueryPlaceholder = "What does this mean?";

    /// <summary>Ask-bar placeholder (shown in the overlay query field).</summary>
    public static string GetQueryPlaceholder() => AskBarPlaceholder;

    /// <summary>Empty-state / hero hint when the user has no asks yet.</summary>
    public static string GetEmptyStateHint() =>
        SelectionMode == SelectionActivationMode.Instant
            ? AskBarPlaceholder
            : "Highlight text with Shift + select to get started.";

    public static string GetSelectionModeDescription() =>
        SelectionMode switch
        {
            SelectionActivationMode.Instant => "Highlight text to open the ask bar instantly.",
            SelectionActivationMode.ShiftSelect =>
                "Hold Shift while you drag to highlight text.",
            SelectionActivationMode.CustomHotkey when SelectionHotkeyVk > 0 =>
                $"Hold {FormatHotkey(SelectionHotkeyVk, SelectionHotkeyModifiers)} while selecting.",
            SelectionActivationMode.CustomHotkey =>
                "Set a key combination in Change.",
            _ => "Hold Shift while you drag to highlight text."
        };

    public static string FormatHotkey(int vk, int modifiersInt)
    {
        var parts = new List<string>();
        var mods = (System.Windows.Input.ModifierKeys)modifiersInt;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(VkToDisplayName(vk));
        return string.Join(" + ", parts);
    }

    private static string VkToDisplayName(int vk)
    {
        try
        {
            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk);
            if (key != System.Windows.Input.Key.None)
                return key.ToString();
        }
        catch { /* ignore */ }

        return $"Key {vk}";
    }

    /// <summary>True when the current selection mode allows showing the overlay for this highlight.</summary>
    public static bool IsSelectionActivationSatisfied() =>
        SelectionMode switch
        {
            SelectionActivationMode.Instant => true,
            SelectionActivationMode.ShiftSelect => IsVirtualKeyDown(0x10),
            SelectionActivationMode.CustomHotkey => IsCustomHotkeyHeld(),
            _ => IsVirtualKeyDown(0x10)
        };

    private static bool IsCustomHotkeyHeld()
    {
        if (SelectionHotkeyVk <= 0) return false;

        var mods = (System.Windows.Input.ModifierKeys)SelectionHotkeyModifiers;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control) && !IsVirtualKeyDown(0x11))
            return false;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt) && !IsVirtualKeyDown(0x12))
            return false;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift) && !IsVirtualKeyDown(0x10))
            return false;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Windows) && !IsVirtualKeyDown(0x5B) && !IsVirtualKeyDown(0x5C))
            return false;

        return IsVirtualKeyDown(SelectionHotkeyVk);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsVirtualKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True when the user has saved an OpenRouter key in idk-settings.json.</summary>
    public static bool HasStoredApiKey()
    {
        var key = ReadFileDtoOrDefault().OpenrouterApiKey?.Trim() ?? "";
        return key.Length > 0;
    }

    /// <summary>OpenRouter key from environment (after load) or settings file.</summary>
    public static string? GetOpenRouterApiKeyOrNull()
    {
        var env = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")?.Trim();
        if (env is { Length: > 0 }) return env;
        var file = ReadFileDtoOrDefault().OpenrouterApiKey?.Trim();
        return file is { Length: > 0 } ? file : null;
    }

    /// <summary>Masked preview of the saved key (last four characters), or empty.</summary>
    public static string GetStoredApiKeyPreview()
    {
        var key = ReadFileDtoOrDefault().OpenrouterApiKey?.Trim() ?? "";
        if (key.Length == 0) return "";
        return key.Length <= 4 ? "••••" : "••••••••" + key[^4..];
    }

    private static FileDto ReadFileDtoOrDefault()
    {
        try
        {
            if (!File.Exists(FilePath)) return new FileDto();
            return JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), JsonOpts) ?? new FileDto();
        }
        catch
        {
            return new FileDto();
        }
    }

    public static void ApplyToProcess()
    {
        IsScreenCaptureEnabled = false;
        IsOverlaySolidOnly = false;
        HideFromTaskbar = false;
        AppTheme = AppTheme.Dark;
        SelectionMode = SelectionActivationMode.ShiftSelect;
        SelectionHotkeyVk = 0;
        SelectionHotkeyModifiers = 0;
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        try
        {
            var dto = ReadFileDtoOrDefault();
            SyncScreenCaptureFrom(dto);
            SyncOverlaySolidFrom(dto);
            SyncHideFromTaskbarFrom(dto);
            SyncThemeFrom(dto);
            SyncSelectionModeFrom(dto);
            if (!string.IsNullOrWhiteSpace(dto.OpenrouterApiKey))
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", dto.OpenrouterApiKey.Trim());
            if (!string.IsNullOrWhiteSpace(dto.IdkTextModel))
                Environment.SetEnvironmentVariable("IDK_TEXT_MODEL", dto.IdkTextModel.Trim());
            if (!string.IsNullOrWhiteSpace(dto.IdkVisionModel))
                Environment.SetEnvironmentVariable("IDK_VISION_MODEL", dto.IdkVisionModel.Trim());
        }
        catch { /* ignore */ }
    }

    public static DashboardDto ReadDashboard()
    {
        var file = ReadFileDtoOrDefault();
        var key = file.OpenrouterApiKey?.Trim() ?? "";
        var preview = key.Length == 0 ? "" : key.Length <= 4 ? "••••" : "••••••••" + key[^4..];
        return new DashboardDto(
            key.Length > 0,
            preview,
            Environment.GetEnvironmentVariable("IDK_TEXT_MODEL")?.Trim() ?? "",
            Environment.GetEnvironmentVariable("IDK_VISION_MODEL")?.Trim() ?? "",
            IsLaunchAtLoginEnabled(),
            IsScreenCaptureEnabled,
            IsOverlaySolidOnly,
            HideFromTaskbar,
            SelectionMode,
            SelectionHotkeyVk,
            SelectionHotkeyModifiers,
            AppTheme
        );
    }

    public static void SaveDashboard(DashboardSaveDto patch)
    {
        Directory.CreateDirectory(Dir);
        var cur = new FileDto();
        if (File.Exists(FilePath))
        {
            try
            {
                cur = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), JsonOpts) ?? cur;
            }
            catch { /* keep defaults */ }
        }

        if (patch.ClearOpenrouterApiKey == true)
            cur.OpenrouterApiKey = null;
        else if (!string.IsNullOrWhiteSpace(patch.OpenrouterApiKey))
            cur.OpenrouterApiKey = patch.OpenrouterApiKey.Trim();

        if (patch.IdkTextModel is not null)
            cur.IdkTextModel = string.IsNullOrWhiteSpace(patch.IdkTextModel) ? null : patch.IdkTextModel.Trim();
        if (patch.IdkVisionModel is not null)
            cur.IdkVisionModel = string.IsNullOrWhiteSpace(patch.IdkVisionModel) ? null : patch.IdkVisionModel.Trim();
        if (patch.ScreenCaptureEnabled is bool sc)
            cur.ScreenCaptureEnabled = sc;
        if (patch.OverlaySolidOnly is bool oo)
            cur.OverlaySolidOnly = oo;
        if (patch.HideFromTaskbar is bool hf)
            cur.HideFromTaskbar = hf;
        if (patch.ActivateOnAnySelection is bool aos)
            cur.ActivateOnAnySelection = aos;
        if (patch.SelectionMode is SelectionActivationMode sm)
        {
            cur.SelectionActivationMode = sm.ToString();
            cur.ActivateOnAnySelection = sm == SelectionActivationMode.Instant;
        }
        if (patch.SelectionHotkeyVk is int vk)
            cur.SelectionHotkeyVk = vk > 0 ? vk : null;
        if (patch.SelectionHotkeyModifiers is int hm)
            cur.SelectionHotkeyModifiers = hm;
        if (patch.Theme is AppTheme th)
            cur.Theme = th.ToString();

        File.WriteAllText(FilePath, JsonSerializer.Serialize(cur, JsonOpts));
        SyncScreenCaptureFrom(cur);
        SyncOverlaySolidFrom(cur);
        SyncHideFromTaskbarFrom(cur);
        SyncThemeFrom(cur);
        SyncSelectionModeFrom(cur);

        if (patch.ClearOpenrouterApiKey == true)
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        else if (!string.IsNullOrWhiteSpace(patch.OpenrouterApiKey))
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", patch.OpenrouterApiKey.Trim());

        if (patch.IdkTextModel is not null)
            Environment.SetEnvironmentVariable("IDK_TEXT_MODEL", string.IsNullOrWhiteSpace(patch.IdkTextModel) ? null : patch.IdkTextModel.Trim());
        if (patch.IdkVisionModel is not null)
            Environment.SetEnvironmentVariable("IDK_VISION_MODEL", string.IsNullOrWhiteSpace(patch.IdkVisionModel) ? null : patch.IdkVisionModel.Trim());

        if (patch.OpenAtLogin is bool b)
            SetLaunchAtLogin(b);
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunName = "idk";

    public static bool IsLaunchAtLoginEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(AppRunName) is string s && !string.IsNullOrEmpty(s);
        }
        catch { return false; }
    }

    public static void SetLaunchAtLogin(bool enable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true)
                    ?? throw new InvalidOperationException("Registry unavailable");
        if (enable)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");
            k.SetValue(AppRunName, $"\"{exe}\"");
        }
        else
            k.DeleteValue(AppRunName, false);
    }

    /// <summary>Applies <see cref="HideFromTaskbar"/> without resizing or resetting the dashboard.</summary>
    public static void ApplyMainWindowTaskbarVisibility(System.Windows.Window window)
    {
        var showInTaskbar = !HideFromTaskbar;
        if (window.ShowInTaskbar == showInTaskbar)
            return;

        void Apply()
        {
            var top = window.Top;
            var left = window.Left;
            var width = window.Width;
            var height = window.Height;
            var state = window.WindowState;

            window.ShowInTaskbar = showInTaskbar;

            if (state == System.Windows.WindowState.Normal)
            {
                window.WindowState = System.Windows.WindowState.Normal;
                window.Top = top;
                window.Left = left;
                window.Width = width;
                window.Height = height;
            }
            else
            {
                window.WindowState = state;
            }
        }

        if (window.Dispatcher.CheckAccess())
            Apply();
        else
            window.Dispatcher.Invoke(Apply);
    }
}

public record DashboardDto(
    bool HasApiKey,
    string KeyPreview,
    string TextModel,
    string VisionModel,
    bool OpenAtLogin,
    bool ScreenCaptureEnabled,
    bool OverlaySolidOnly,
    bool HideFromTaskbar,
    SelectionActivationMode SelectionMode,
    int SelectionHotkeyVk,
    int SelectionHotkeyModifiers,
    AppTheme Theme);

public class DashboardSaveDto
{
    public string? OpenrouterApiKey { get; set; }
    public bool? ClearOpenrouterApiKey { get; set; }
    public string? IdkTextModel { get; set; }
    public string? IdkVisionModel { get; set; }
    public bool? OpenAtLogin { get; set; }
    public bool? ScreenCaptureEnabled { get; set; }
    public bool? OverlaySolidOnly { get; set; }
    public bool? HideFromTaskbar { get; set; }
    public bool? ActivateOnAnySelection { get; set; }
    public SelectionActivationMode? SelectionMode { get; set; }
    public int? SelectionHotkeyVk { get; set; }
    public int? SelectionHotkeyModifiers { get; set; }
    public AppTheme? Theme { get; set; }
}
