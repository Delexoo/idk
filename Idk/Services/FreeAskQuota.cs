using System.IO;
using System.Text.Json;

namespace Idk.Services;

/// <summary>
/// Unpaid computers get five successful asks. idk Pro does not use this count.
/// </summary>
public static class FreeAskQuota
{
    public const int Limit = 5;
    public const string BlockedMessage = "You have used your 5 free asks. Upgrade to idk Pro to keep asking.";

    private static readonly object Gate = new();
    private static int _used = -1;

    public static event Action? Changed;

    public static int Used
    {
        get
        {
            lock (Gate)
            {
                EnsureLoaded();
                return _used;
            }
        }
    }

    public static bool CanAsk => LicenseService.IsLicensed || Used < Limit;

    public static int Remaining => LicenseService.IsLicensed ? Limit : Math.Max(0, Limit - Used);

    public static string Summary =>
        LicenseService.IsLicensed
            ? "idk Pro is active. Asking is included."
            : Remaining == 0
                ? "No free asks left. Upgrade to idk Pro to keep asking."
                : Remaining == 1
                    ? "1 free ask left. Then idk Pro is required."
                    : $"{Remaining} free asks left. Then idk Pro is required.";

    /// <summary>Count one finished answer. Failed requests are not counted.</summary>
    public static void RecordSuccess()
    {
        if (LicenseService.IsLicensed) return;
        lock (Gate)
        {
            EnsureLoaded();
            if (_used >= Limit) return;
            _used++;
            try
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                File.WriteAllText(Path.Combine(SettingsStore.DataDirectory, "free-asks.json"),
                    JsonSerializer.Serialize(new Store { Used = _used }));
            }
            catch
            {
                /* the in-memory count still applies this session */
            }
        }

        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (_used >= 0) return;
        _used = 0;
        try
        {
            var path = Path.Combine(SettingsStore.DataDirectory, "free-asks.json");
            if (!File.Exists(path)) return;
            var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path));
            if (store != null)
                _used = Math.Clamp(store.Used, 0, Limit);
        }
        catch
        {
            _used = 0;
        }
    }

    private sealed class Store
    {
        public int Used { get; set; }
    }
}
