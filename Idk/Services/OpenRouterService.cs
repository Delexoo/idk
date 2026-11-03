using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Idk.Services;

public static class OpenRouterService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string TextModel =>
        Environment.GetEnvironmentVariable("IDK_TEXT_MODEL")?.Trim()
        ?? Environment.GetEnvironmentVariable("OPENROUTER_MINIMAX_MODEL")?.Trim()
        ?? SettingsStore.DefaultTextModel;

    public static string VisionModel =>
        Environment.GetEnvironmentVariable("IDK_VISION_MODEL")?.Trim()
        ?? Environment.GetEnvironmentVariable("OPENROUTER_SITE_DROP_VISION_MODEL")?.Trim()
        ?? Environment.GetEnvironmentVariable("OPENROUTER_VISION_MODEL")?.Trim()
        ?? SettingsStore.DefaultVisionModel;

    private const string SystemPrompt =
        "You are idk, a friendly inline assistant. The user message quotes text they selected (lines starting with >) and asks a question. " +
        "Reply with only the final answer the user should read — no planning, strategy, reasoning steps, outlines, or meta commentary. " +
        "Never write lines like \"Strategy:\", \"Plan:\", \"Approach:\", \"Thinking:\", or \"Let me think\". Do not describe what the user did or that they highlighted text. " +
        "Begin immediately with the answer (definition, explanation, or example). Be clear and concise; use bullets only when they help. " +
        "Add about 1–2 emojis naturally when it fits. If a screenshot is attached, use it with the quoted text for context.";

    public static bool IsReady => SealedApi.IsReady;

    private static readonly Uri Endpoint = new("https://openrouter.ai/api/v1/chat/completions");

    private static string? OpenKey() => SealedApi.Open();

    public static async IAsyncEnumerable<string> StreamChatAsync(
        string userMessage,
        string? visionPngBase64,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!FreeAskQuota.CanAsk)
            throw new InvalidOperationException(FreeAskQuota.BlockedMessage);

        var key = OpenKey();
        if (key == null)
        {
            yield return "[Model access is not built into this copy.]";
            yield break;
        }

        var useVision = !string.IsNullOrEmpty(visionPngBase64);
        var model = useVision ? VisionModel : TextModel;

        object[] messages;
        if (useVision)
        {
            messages =
            [
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = (object[])[
                        new { type = "text", text = userMessage },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = "data:image/png;base64," + visionPngBase64,
                                detail = "low"
                            }
                        }
                    ]
                }
            ];
        }
        else
        {
            messages =
            [
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userMessage }
            ];
        }

        var payload = new { model, stream = true, max_tokens = 8192, messages };
        var json = JsonSerializer.Serialize(payload);

        using var req = CreateRequest(key, json);
        var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (useVision && resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            resp.Dispose();
            var textOnly = new
            {
                model = TextModel,
                stream = true,
                max_tokens = 8192,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userMessage }
                }
            };
            using var retry = CreateRequest(key, JsonSerializer.Serialize(textOnly));
            resp = await Http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        using (resp)
        {
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errEl))
            {
                yield return errEl.TryGetProperty("message", out var m) ? m.GetString() ?? "Error" : "Error";
                yield break;
            }

            if (root.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                var t = content.GetString();
                if (!string.IsNullOrEmpty(t))
                    yield return t;
            }
        }
        }
    }

    /// <summary>Non-streaming completion for short tasks (e.g. chat titles).</summary>
    public static async Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 32,
        CancellationToken ct = default)
    {
        var key = OpenKey();
        if (key == null) return null;

        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var payload = new { model = TextModel, stream = false, max_tokens = maxTokens, messages };
        var json = JsonSerializer.Serialize(payload);

        using var req = CreateRequest(key, json);
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errEl))
            return errEl.TryGetProperty("message", out var m) ? m.GetString() : null;

        if (root.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
            return content.GetString();

        return null;
    }

    private static HttpRequestMessage CreateRequest(string key, string json)
    {
        if (!string.Equals(Endpoint.Host, "openrouter.ai", StringComparison.OrdinalIgnoreCase)
            || Endpoint.Scheme != "https")
            throw new InvalidOperationException("Model access could not start.");

        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://local.idk");
        req.Headers.TryAddWithoutValidation("X-Title", "idk");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }
}
