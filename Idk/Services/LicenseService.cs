using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Idk.Services;

/// <summary>
/// Monthly subscription for this computer. The license file is signed for this machine and lasts until the paid period ends.
/// </summary>
public static class LicenseService
{
    public const int PriceCents = 300;
    public const string PriceMessage = "idk Pro is $3 a month for this computer.";
    public const string PaidMessage = "idk Pro is active. It is $3 a month for this computer.";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string _secret = "";
    private static string _billingOrigin = "";
    private static string _deviceId = "";
    private static TcpListener? _listener;
    private static CancellationTokenSource? _callbackCts;

    public static bool IsLicensed { get; private set; }
    public static bool IsBusy { get; private set; }
    public static string StatusMessage { get; private set; } = PriceMessage;
    private static string _customerId = "";
    public static event Action? Changed;

    public static void Load()
    {
        LoadEnv();
        if (TryReadCache() != null)
        {
            IsLicensed = true;
            StatusMessage = PaidMessage;
        }
        else
        {
            IsLicensed = false;
            StatusMessage = PriceMessage;
        }

        Changed?.Invoke();
        _ = CheckCoreAsync(userInitiated: false);
    }

    public static Task BuyAsync() => BuyCoreAsync();

    public static Task CheckAsync() => CheckCoreAsync(userInitiated: true);

    private static async Task BuyCoreAsync()
    {
        await Gate.WaitAsync();
        try
        {
            IsBusy = true;
            StatusMessage = "Opening checkout for this computer.";
            Changed?.Invoke();

            var device = DeviceId();
            if (device.Length == 0)
                throw new InvalidOperationException("This computer could not be identified.");

            var port = StartCallback();
            var success = $"http://127.0.0.1:{port}/paid?session_id={{CHECKOUT_SESSION_ID}}";
            var cancel = $"http://127.0.0.1:{port}/cancel";
            var url = _secret.Length > 0
                ? await CreateCheckoutAsync(device, success, cancel)
                : await HostedCheckoutAsync(device, success, cancel);

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusMessage = "Finish subscribing in the browser. idk updates when the subscription is active.";
        }
        catch (Exception ex)
        {
            StatusMessage = Clean(ex.Message, "Billing could not start.");
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
            Gate.Release();
        }
    }

    private static async Task CheckCoreAsync(bool userInitiated)
    {
        await Gate.WaitAsync();
        try
        {
            if (userInitiated)
            {
                IsBusy = true;
                StatusMessage = "Checking the license for this computer.";
                Changed?.Invoke();
            }

            var cache = TryReadCache();
            if (cache != null)
            {
                IsLicensed = true;
                StatusMessage = PaidMessage;
            }

            if (_secret.Length == 0 && _billingOrigin.Length == 0)
            {
                if (!IsLicensed)
                    StatusMessage = "Billing is not configured yet.";
                return;
            }

            var device = DeviceId();
            if (device.Length == 0)
                throw new InvalidOperationException("This computer could not be identified.");

            var found = _secret.Length > 0
                ? await FindPaidAsync(device)
                : await HostedLicenseAsync(device, null);

            if (found is not { Active: true })
            {
                ClearLocal();
                _customerId = "";
                IsLicensed = false;
                StatusMessage = "This computer does not have an active subscription.";
                return;
            }

            Remember(device, found);
        }
        catch (Exception ex)
        {
            if (TryReadCache() == null)
            {
                IsLicensed = false;
                StatusMessage = Clean(ex.Message, "Billing could not be checked.");
            }
        }
        finally
        {
            if (userInitiated)
                IsBusy = false;
            Changed?.Invoke();
            Gate.Release();
        }
    }

    public static async Task ManageAsync()
    {
        await Gate.WaitAsync();
        try
        {
            IsBusy = true;
            StatusMessage = "Opening subscription settings.";
            Changed?.Invoke();

            var device = DeviceId();
            if (device.Length == 0)
                throw new InvalidOperationException("This computer could not be identified.");

            var url = _secret.Length > 0
                ? await CreatePortalAsync(device)
                : await HostedPortalAsync(device);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusMessage = PaidMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = Clean(ex.Message, "Billing could not open.");
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
            Gate.Release();
        }
    }

    private static void Remember(string device, PaidCheckout found)
    {
        if (found.CustomerId.Length > 0)
            _customerId = found.CustomerId;
        if (_secret.Length > 0 && found.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            SaveLocal(device, found.SessionId, found.SubscriptionId, found.Email, found.PeriodEnd);
        IsLicensed = true;
        StatusMessage = PaidMessage;
    }

    private static async Task ConfirmSessionAsync(string? sessionId)
    {
        await Gate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !IsSessionId(sessionId))
                throw new InvalidOperationException("Unknown checkout.");

            var device = DeviceId();
            PaidCheckout found;
            if (_secret.Length > 0)
            {
                using var doc = await StripeAsync(HttpMethod.Get, $"/v1/checkout/sessions/{sessionId}", null);
                found = await ReadActiveSubscriptionAsync(doc.RootElement, device) ?? throw new InvalidOperationException("This payment belongs to a different computer.");
                if (!found.Active)
                    throw new InvalidOperationException("This subscription is not active.");
            }
            else
            {
                found = await HostedLicenseAsync(device, sessionId);
                if (!found.Active)
                    throw new InvalidOperationException("Checkout could not be confirmed.");
            }

            Remember(device, found);
        }
        finally
        {
            Changed?.Invoke();
            Gate.Release();
        }
    }

    private static void LoadEnv()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadEnvFile(Path.Combine(SettingsStore.DataDirectory, "billing.env"), map);
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installed = baseDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase);
        if (!installed)
        {
            var dir = new DirectoryInfo(baseDir);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (!File.Exists(envPath)) continue;
                ReadEnvFile(envPath, map);
                break;
            }
        }
        else
        {
            ReadEnvFile(Path.Combine(baseDir, ".env"), map);
        }

        _secret = Value(map, "STRIPE_SECRET_KEY");
        _billingOrigin = Value(map, "IDK_BILLING_ORIGIN").TrimEnd('/');
    }

    private static void ReadEnvFile(string path, Dictionary<string, string> map)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 1) continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
                value = value[1..^1];
            if (value.Length > 0)
                map[key] = value;
        }
    }

    private static string Value(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var value) ? value : "";

    private static string DeviceId()
    {
        if (_deviceId.Length > 0) return _deviceId;
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var guid = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(guid)) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(guid.Trim().ToLowerInvariant()));
        _deviceId = Convert.ToHexString(hash).ToLowerInvariant();
        return _deviceId;
    }

    private static string LicensePath => Path.Combine(SettingsStore.DataDirectory, "license.json");

    private static LicenseFile? TryReadCache()
    {
        if (_secret.Length == 0 || DeviceId().Length == 0 || !File.Exists(LicensePath)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(LicensePath), JsonOpts);
            if (record == null || !Matches(_secret, _deviceId, record)) return null;
            if (record.PeriodEnd <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return record;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearLocal()
    {
        try
        {
            if (File.Exists(LicensePath))
                File.Delete(LicensePath);
        }
        catch
        {
            /* The next check asks Stripe again. */
        }
    }

    private static void SaveLocal(string device, string sessionId, string subscriptionId, string email, long periodEnd)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var record = new LicenseFile
        {
            V = 2,
            Device = device,
            SessionId = sessionId,
            SubscriptionId = subscriptionId,
            Email = email,
            PeriodEnd = periodEnd,
            Sig = Sign(_secret, device, sessionId, subscriptionId, email, periodEnd)
        };
        File.WriteAllText(LicensePath, JsonSerializer.Serialize(record, JsonOpts));
    }

    private static string Sign(string secret, string device, string sessionId, string subscriptionId, string email, long periodEnd)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{device}\n{sessionId}\n{subscriptionId}\n{email}\n{periodEnd}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool Matches(string secret, string device, LicenseFile record)
    {
        if (record.V != 2 || record.Device != device) return false;
        if (string.IsNullOrEmpty(record.SessionId) || string.IsNullOrEmpty(record.SubscriptionId) || record.Email == null || string.IsNullOrEmpty(record.Sig))
            return false;
        if (record.Sig.Length != 64) return false;
        var expected = Sign(secret, device, record.SessionId, record.SubscriptionId, record.Email, record.PeriodEnd);
        var left = Encoding.ASCII.GetBytes(expected);
        var right = Encoding.ASCII.GetBytes(record.Sig.ToLowerInvariant());
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static async Task<string> CreateCheckoutAsync(string device, string success, string cancel)
    {
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = "usd",
            ["line_items[0][price_data][unit_amount]"] = PriceCents.ToString(),
            ["line_items[0][price_data][recurring][interval]"] = "month",
            ["line_items[0][price_data][product_data][name]"] = "idk Pro",
            ["line_items[0][price_data][product_data][description]"] = "$3 a month for this computer.",
            ["success_url"] = success,
            ["cancel_url"] = cancel,
            ["metadata[device]"] = device,
            ["subscription_data[metadata][device]"] = device,
            ["client_reference_id"] = device,
            ["allow_promotion_codes"] = "true"
        };

        using var doc = await StripeAsync(HttpMethod.Post, "/v1/checkout/sessions", form);
        var url = doc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (url == null || !url.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("Billing could not start.");
        return url;
    }

    private static async Task<PaidCheckout?> FindPaidAsync(string device)
    {
        var query = Uri.EscapeDataString($"client_reference_id:'{device}' AND status:'complete' AND mode:'subscription'");
        try
        {
            using var doc = await StripeAsync(HttpMethod.Get, $"/v1/checkout/sessions/search?query={query}&limit=5", null);
            var found = await FirstActiveAsync(doc.RootElement, device);
            if (found != null) return found;
        }
        catch
        {
            return await FindPaidInListAsync(device);
        }

        return null;
    }

    private static async Task<PaidCheckout?> FindPaidInListAsync(string device)
    {
        var startingAfter = "";
        for (var page = 0; page < 10; page++)
        {
            var path = "/v1/checkout/sessions?limit=100&status=complete";
            if (startingAfter.Length > 0)
                path += "&starting_after=" + Uri.EscapeDataString(startingAfter);
            JsonDocument doc;
            try
            {
                doc = await StripeAsync(HttpMethod.Get, path, null);
            }
            catch
            {
                return null;
            }

            using (doc)
            {
                var found = await FirstActiveAsync(doc.RootElement, device);
                if (found != null) return found;
                if (!doc.RootElement.TryGetProperty("data", out var rows) || rows.GetArrayLength() == 0)
                    return null;
                var last = rows[rows.GetArrayLength() - 1];
                var more = doc.RootElement.TryGetProperty("has_more", out var moreEl) && moreEl.ValueKind == JsonValueKind.True;
                var lastId = last.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (!more || lastId.Length == 0) return null;
                startingAfter = lastId;
            }
        }

        return null;
    }

    private static async Task<PaidCheckout?> FirstActiveAsync(JsonElement payload, string device)
    {
        if (!payload.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
        foreach (var row in rows.EnumerateArray())
        {
            var paid = await ReadActiveSubscriptionAsync(row, device);
            if (paid is { Active: true }) return paid;
        }

        return null;
    }

    private static async Task<PaidCheckout?> ReadActiveSubscriptionAsync(JsonElement record, string device)
    {
        if (StringProp(record, "mode") != "subscription") return null;
        var reference = StringProp(record, "client_reference_id");
        var tagged = "";
        if (record.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            tagged = StringProp(metadata, "device");
        if (reference != device && tagged != device) return null;
        var status = StringProp(record, "status");
        var payment = StringProp(record, "payment_status");
        var sessionId = StringProp(record, "id");
        var subscriptionId = StringProp(record, "subscription");
        var paid = status == "complete" && (payment == "paid" || payment == "no_payment_required");
        if (!paid || !IsSessionId(sessionId) || !IsSubscriptionId(subscriptionId)) return null;
        var email = StringProp(record, "customer_email");
        if (email.Length == 0 && record.TryGetProperty("customer_details", out var details))
            email = StringProp(details, "email");

        using var subDoc = await StripeAsync(HttpMethod.Get, "/v1/subscriptions/" + subscriptionId, null);
        var sub = subDoc.RootElement;
        var subDevice = "";
        if (sub.TryGetProperty("metadata", out var subMeta) && subMeta.ValueKind == JsonValueKind.Object)
            subDevice = StringProp(subMeta, "device");
        if (subDevice.Length > 0 && subDevice != device) return null;
        var subStatus = StringProp(sub, "status");
        var active = subStatus is "active" or "trialing";
        var customerId = StringProp(sub, "customer");
        return new PaidCheckout(active, sessionId, email, subscriptionId, PeriodEnd(sub), customerId);
    }

    private static long PeriodEnd(JsonElement sub)
    {
        if (sub.TryGetProperty("current_period_end", out var end) && end.TryGetInt64(out var unix) && unix > 0)
            return unix;
        if (sub.TryGetProperty("items", out var items)
            && items.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("current_period_end", out var itemEnd)
            && itemEnd.TryGetInt64(out var itemUnix))
            return itemUnix;
        return 0;
    }

    private static async Task<string> CreatePortalAsync(string device)
    {
        var customer = _customerId;
        if (!IsCustomerId(customer))
        {
            var found = await FindPaidAsync(device);
            customer = found?.CustomerId ?? "";
        }

        if (!IsCustomerId(customer))
            throw new InvalidOperationException("This computer does not have an active subscription.");

        var origin = _billingOrigin.StartsWith("https://", StringComparison.Ordinal) ? _billingOrigin : "https://stripe.com";
        using var doc = await StripeAsync(HttpMethod.Post, "/v1/billing_portal/sessions", new Dictionary<string, string>
        {
            ["customer"] = customer,
            ["return_url"] = origin
        });
        var url = doc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (url == null || !url.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("Billing could not open.");
        return url;
    }

    private static async Task<string> HostedPortalAsync(string device)
    {
        if (_billingOrigin.Length == 0)
            throw new InvalidOperationException("Billing is not configured yet.");
        using var doc = await PostJsonAsync(_billingOrigin + "/api/license", new { device, portal = true });
        var url = doc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (url == null || !url.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("Billing could not open.");
        return url;
    }

    private static async Task<string> HostedCheckoutAsync(string device, string success, string cancel)
    {
        if (_billingOrigin.Length == 0)
            throw new InvalidOperationException("Billing is not configured yet.");
        using var doc = await PostJsonAsync(_billingOrigin + "/api/checkout", new
        {
            device,
            successUrl = success,
            cancelUrl = cancel
        });
        var url = doc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (url == null || !url.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("Billing could not start.");
        return url;
    }

    private static async Task<PaidCheckout> HostedLicenseAsync(string device, string? sessionId)
    {
        if (_billingOrigin.Length == 0)
            throw new InvalidOperationException("Billing is not configured yet.");
        using var doc = await PostJsonAsync(_billingOrigin + "/api/license", new { device, sessionId });
        var active = doc.RootElement.TryGetProperty("active", out var activeEl) && activeEl.ValueKind == JsonValueKind.True;
        var email = doc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? "" : "";
        var id = doc.RootElement.TryGetProperty("sessionId", out var idEl) ? idEl.GetString() ?? "" : "";
        var subscriptionId = doc.RootElement.TryGetProperty("subscriptionId", out var subEl) ? subEl.GetString() ?? "" : "";
        var customerId = doc.RootElement.TryGetProperty("customerId", out var customerEl) ? customerEl.GetString() ?? "" : "";
        long periodEnd = 0;
        if (doc.RootElement.TryGetProperty("periodEnd", out var endEl))
            endEl.TryGetInt64(out periodEnd);
        return new PaidCheckout(active, id, email, subscriptionId, periodEnd, customerId);
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch
        {
            throw new InvalidOperationException("Billing could not be reached.");
        }

        if (!res.IsSuccessStatusCode)
        {
            var message = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : null;
            doc.Dispose();
            throw new InvalidOperationException(Clean(message, "Billing could not start."));
        }

        return doc;
    }

    private static async Task<JsonDocument> StripeAsync(HttpMethod method, string pathAndQuery, Dictionary<string, string>? form)
    {
        if (_secret.Length == 0)
            throw new InvalidOperationException("Billing is not configured yet.");
        using var req = new HttpRequestMessage(method, "https://api.stripe.com" + pathAndQuery);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        if (form != null)
        {
            var encoded = string.Join("&", form.Select(pair =>
            {
                var value = Uri.EscapeDataString(pair.Value)
                    .Replace("%7BCHECKOUT_SESSION_ID%7D", "{CHECKOUT_SESSION_ID}", StringComparison.OrdinalIgnoreCase);
                return Uri.EscapeDataString(pair.Key) + "=" + value;
            }));
            req.Content = new StringContent(encoded, Encoding.ASCII, "application/x-www-form-urlencoded");
        }

        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch
        {
            throw new InvalidOperationException("Billing could not start.");
        }

        if (!res.IsSuccessStatusCode)
        {
            var message = "Billing could not start.";
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var messageEl)
                && messageEl.GetString() is { Length: > 0 } stripeMessage)
                message = stripeMessage;
            doc.Dispose();
            throw new InvalidOperationException(Clean(message, "Billing could not start."));
        }

        return doc;
    }

    private static int StartCallback()
    {
        StopCallback();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _callbackCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        var token = _callbackCts.Token;
        _ = Task.Run(() => AcceptLoop(token), token);
        return port;
    }

    private static void StopCallback()
    {
        try { _callbackCts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
    }

    private static async Task AcceptLoop(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch
            {
                break;
            }

            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 4000;
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync() ?? "";
                for (var i = 0; i < 40; i++)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) break;
                }

                var target = request.Split(' ').ElementAtOrDefault(1) ?? "/";
                string html;
                if (target.StartsWith("/paid", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await ConfirmSessionAsync(ReadQuery(target, "session_id"));
                        html = "<html><body style=\"font-family:Segoe UI,sans-serif\"><h1>This computer is subscribed.</h1><p>You can close this tab and return to idk.</p></body></html>";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = Clean(ex.Message, "Checkout could not be confirmed.");
                        Changed?.Invoke();
                        html = "<html><body style=\"font-family:Segoe UI,sans-serif\"><h1>Payment was not applied.</h1><p>"
                            + WebUtility.HtmlEncode(StatusMessage) + "</p></body></html>";
                    }
                }
                else
                {
                    StatusMessage = "Payment was canceled.";
                    Changed?.Invoke();
                    html = "<html><body style=\"font-family:Segoe UI,sans-serif\"><h1>Payment canceled.</h1><p>You can close this tab and return to idk.</p></body></html>";
                }

                var bytes = Encoding.UTF8.GetBytes(html);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                var head = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(head);
                await stream.WriteAsync(bytes);
            }
        }
        catch
        {
            /* The browser can close the tab. */
        }
    }

    private static string ReadQuery(string target, string name)
    {
        var q = target.IndexOf('?');
        if (q < 0) return "";
        foreach (var part in target[(q + 1)..].Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name)
                return Uri.UnescapeDataString(kv[1]);
        }

        return "";
    }

    private static bool IsSessionId(string? value) =>
        !string.IsNullOrEmpty(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^cs_[A-Za-z0-9_]+$");

    private static bool IsSubscriptionId(string? value) =>
        !string.IsNullOrEmpty(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^sub_[A-Za-z0-9]+$");

    private static bool IsCustomerId(string? value) =>
        !string.IsNullOrEmpty(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^cus_[A-Za-z0-9]+$");

    private static string StringProp(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Clean(string? message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message)) return fallback;
        var text = message.Trim();
        return text.Length > 180 ? text[..180] : text;
    }

    private sealed class LicenseFile
    {
        public int V { get; set; }
        public string? Device { get; set; }
        public string? SessionId { get; set; }
        public string? SubscriptionId { get; set; }
        public string? Email { get; set; }
        public long PeriodEnd { get; set; }
        public string? Sig { get; set; }
    }

    private sealed record PaidCheckout(bool Active, string SessionId, string Email, string SubscriptionId, long PeriodEnd, string CustomerId);
}
