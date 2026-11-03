using System.Text.RegularExpressions;

namespace Idk.Services;

/// <summary>Unique, topic-specific sidebar titles (not raw user questions).</summary>
public static class ChatTitleService
{
    private const int MaxWords = 6;
    private const int MaxChars = 52;

    private static readonly Regex QuestionLeadRx = new(
        @"^\s*(what|who|why|how|when|where|is|are|does|do|did|can|could|would|should|idk)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string BuildHeuristicTitle(string? selection, string question, string? answer = null)
    {
        var topic = ExtractTopicPhrase(selection);
        var intent = ClassifyIntent(question);

        if (topic.Length > 0)
        {
            var fromTopic = intent switch
            {
                "synonym" => $"{topic} synonyms",
                "meaning" => $"{topic} explained",
                "summary" => $"{topic} overview",
                "compare" => $"{topic} compared",
                _ => $"{topic} notes"
            };
            return CapTitle(fromTopic);
        }

        var fromAnswer = ExtractTopicFromAnswer(answer);
        if (fromAnswer.Length > 0)
            return CapTitle(fromAnswer);

        var core = StripQuestionBoilerplate(question);
        return string.IsNullOrWhiteSpace(core) ? "New chat" : CapTitle(core);
    }

    public static async Task RefreshTitleForEntryAsync(long utcTicks, CancellationToken ct = default)
    {
        if (!ChatHistoryStore.TryGetEntry(utcTicks, out var entry) || entry == null)
            return;

        var messages = ChatHistoryStore.GetThreadMessages(entry);
        var question = messages.FirstOrDefault(m => m.IsUser)?.Text ?? entry.Question;
        var answer = messages.LastOrDefault(m => m.IsAssistant)?.Text ?? entry.Answer;

        var title = await GenerateTitleAsync(entry.Selection, question, answer, ct);
        if (title.Length > 0)
            ChatHistoryStore.TryUpdateTitle(utcTicks, title);
    }

    /// <summary>Re-title chats that still look like the raw question (e.g. after an upgrade).</summary>
    public static async Task RefreshStaleTitlesAsync(CancellationToken ct = default)
    {
        if (!OpenRouterService.IsReady) return;

        foreach (var t in ChatHistoryStore.ReadAllUtcTicks())
        {
            if (ct.IsCancellationRequested) break;
            if (!ChatHistoryStore.TryGetEntry(t, out var e) || e == null) continue;
            if (!LooksLikeStaleTitle(e.Title, e.Question)) continue;
            await RefreshTitleForEntryAsync(t, ct);
            await Task.Delay(120, ct);
        }
    }

    public static async Task<string> GenerateTitleAsync(
        string? selection,
        string question,
        string answer,
        CancellationToken ct = default)
    {
        var heuristic = BuildHeuristicTitle(selection, question, answer);
        if (!OpenRouterService.IsReady)
            return heuristic;

        var sel = TrimForPrompt(SelectionSnippetFormatter.SanitizeForDisplay(selection, 280), 280);
        var q = TrimForPrompt(question, 200);
        var a = TrimForPrompt(MarkdownAnswerFormatter.SanitizeForDisplay(answer), 360);

        var userPrompt =
            "Create ONE sidebar chat title (3–6 words).\n" +
            "Must name the SPECIFIC subject using words from the highlighted text when possible.\n" +
            "FORBIDDEN: copying the user's question; generic titles like \"what does this mean\", \"question about text\".\n" +
            "GOOD: \"Canvas hack ransomware\", \"Optogenetics definition\", \"Synonym for elegy\".\n\n" +
            (sel.Length > 0 ? $"Highlighted text:\n{sel}\n\n" : "") +
            $"User question:\n{q}\n\n" +
            $"Assistant reply:\n{a}";

        try
        {
            var raw = await OpenRouterService.CompleteAsync(
                "You output only a short, unique, specific chat title. No quotes. No punctuation at the end.",
                userPrompt,
                maxTokens: 28,
                ct);
            var normalized = NormalizeTitle(raw);
            if (normalized.Length > 0 && !IsStaleTitle(normalized, question))
                return normalized;
        }
        catch
        {
            /* use heuristic */
        }

        return heuristic;
    }

    public static string NormalizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var t = raw.Trim().Trim('"', '\'', '`', '“', '”');
        t = t.Replace('\n', ' ').Replace('\r', ' ');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        t = Regex.Replace(t, @"^(title:\s*)", "", RegexOptions.IgnoreCase);
        t = t.TrimEnd('?', '!', '.', '…');

        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(MaxWords);
        t = string.Join(' ', words);
        return CapTitle(t);
    }

    internal static bool LooksLikeStaleTitle(string title, string question) =>
        IsStaleTitle(NormalizeTitle(title), question);

    private static bool IsStaleTitle(string title, string question)
    {
        if (title.Length == 0) return true;

        var t = title.ToLowerInvariant();
        var q = (question ?? "").Trim().ToLowerInvariant().TrimEnd('?', '!', '.', ' ');

        if (q.Length > 0 && (t == q || q.StartsWith(t) || t.StartsWith(q)))
            return true;

        return QuestionLeadRx.IsMatch(t);
    }

    private static string ExtractTopicPhrase(string? selection)
    {
        var s = SelectionSnippetFormatter.SanitizeForDisplay(selection, 160);
        if (s.Length == 0) return "";

        var line = s.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        line = Regex.Replace(line, @"[^\w\s\-'’.]", " ");
        line = Regex.Replace(line, @"\s+", " ").Trim();

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        return words.Length <= 5 ? string.Join(' ', words) : string.Join(' ', words.Take(4));
    }

    private static string ExtractTopicFromAnswer(string? answer)
    {
        var a = MarkdownAnswerFormatter.SanitizeForDisplay(answer);
        foreach (var line in a.Split('\n'))
        {
            var t = line.Trim().Trim('*', '#', '•', ' ');
            if (t.Length < 4 || t.Length > 48) continue;
            if (QuestionLeadRx.IsMatch(t)) continue;
            if (t.StartsWith("Here", StringComparison.OrdinalIgnoreCase)) continue;
            return t;
        }

        return "";
    }

    private static string StripQuestionBoilerplate(string question)
    {
        var q = (question ?? "").Trim().TrimEnd('?', '!', '.');
        q = Regex.Replace(q,
            @"^(what is|what are|what's|what does|what do|who is|who are|how do i|how to|explain|tell me about|define)\s+",
            "",
            RegexOptions.IgnoreCase);
        return string.Join(' ', Regex.Split(q, @"\s+").Where(w => w.Length > 0).Take(MaxWords));
    }

    private static string ClassifyIntent(string question)
    {
        var q = (question ?? "").ToLowerInvariant();
        if (q.Contains("synonym") || q.Contains("similar word")) return "synonym";
        if (q.Contains("mean") || q.Contains("definition")) return "meaning";
        if (q.Contains("summar") || q.Contains("about") || q.Contains("article")) return "summary";
        if (q.Contains("compare") || q.Contains("difference") || q.Contains(" vs ")) return "compare";
        return "general";
    }

    private static string CapTitle(string title)
    {
        if (title.Length <= MaxChars) return title;
        var cut = title[..MaxChars];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > MaxChars / 2)
            cut = cut[..lastSpace];
        return cut.TrimEnd() + "…";
    }

    private static string TrimForPrompt(string text, int max)
    {
        var t = (text ?? "").Trim();
        if (t.Length <= max) return t;
        return t[..max].TrimEnd() + "…";
    }
}
