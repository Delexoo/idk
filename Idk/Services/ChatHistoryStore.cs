using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Idk.Services;

public sealed class ChatHistoryEntryDto
{
    [JsonPropertyName("utc")]
    public long UtcTicks { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("selection")]
    public string Selection { get; set; } = "";

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("preview")]
    public string Preview { get; set; } = "";

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }

    /// <summary>Ordered user/assistant turns for dashboard chat view.</summary>
    [JsonPropertyName("messages")]
    public List<ChatThreadMessageDto>? Messages { get; set; }
}

public sealed class DashboardChatRow
{
    public long UtcTicks { get; init; }
    public bool IsPinned { get; init; }
    public required string Title { get; init; }
    public required string Preview { get; init; }
    public required string WhenLabel { get; init; }

    public string TitleDisplay => IsPinned ? "↑ " + Title : Title;
}

public sealed class DashboardStats
{
    public int TotalAsks { get; init; }
    public int AsksToday { get; init; }
    public int PinnedCount { get; init; }
}

public sealed class DashboardDayBar
{
    public required string DayLabel { get; init; }
    public int Count { get; init; }
    public double BarFill { get; init; }
    public double BarHeightPx => Count == 0 ? 6 : 10 + BarFill * 58;
    public bool IsToday { get; init; }
}

public sealed class DashboardInsights
{
    public int TotalAsks { get; init; }
    public int AsksToday { get; init; }
    public int PinnedCount { get; init; }
    public int StreakDays { get; init; }
    public int AsksThisWeek { get; init; }
    public int AsksLast7Days { get; init; }
    public double AvgPerDay7 { get; init; }
    public string? BusiestDayLabel { get; init; }
    public int BusiestDayCount { get; init; }
    public IReadOnlyList<DashboardDayBar> Last7Days { get; init; } = Array.Empty<DashboardDayBar>();
    public IReadOnlyList<DashboardChatRow> PinnedHighlights { get; init; } = Array.Empty<DashboardChatRow>();
    public required string HeroSubtitle { get; init; }
}

/// <summary>Visual separator between pinned and recent chats in the sidebar.</summary>
public sealed class ChatHistorySectionDivider;

internal sealed class ChatHistoryFileDto
{
    [JsonPropertyName("items")]
    public List<ChatHistoryEntryDto> Items { get; set; } = new();
}

/// <summary>Lightweight local log of completed overlay Q&amp;A for the dashboard sidebar.</summary>
public static class ChatHistoryStore
{
    /// <summary>Fired after a new entry is appended (any subscriber may refresh UI).</summary>
    public static event Action? ChatHistoryChanged;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string FilePath => Path.Combine(SettingsStore.DataDirectory, "chat-history.json");
    private static readonly object Gate = new();
    private const int MaxItems = 200;
    private const int MaxTitleLen = 140;
    private const int MaxPreviewLen = 200;
    private const int MaxSelectionLen = 8000;
    private const int MaxQuestionLen = 500;
    private const int MaxAnswerLen = 48000;

    public static DashboardStats GetStats()
    {
        var i = GetInsights();
        return new DashboardStats
        {
            TotalAsks = i.TotalAsks,
            AsksToday = i.AsksToday,
            PinnedCount = i.PinnedCount
        };
    }

    public static DashboardInsights GetInsights() => ComputeInsights();

    private static DashboardInsights ComputeInsights()
    {
        lock (Gate)
        {
            try
            {
                var items = LoadFileOrEmpty().Items ?? new List<ChatHistoryEntryDto>();
                var today = DateTime.Today;
                var culture = System.Globalization.CultureInfo.CurrentCulture;
                var asksToday = 0;
                var byDate = new Dictionary<DateTime, int>();

                foreach (var e in items)
                {
                    var local = new DateTime(e.UtcTicks, DateTimeKind.Utc).ToLocalTime();
                    var d = local.Date;
                    if (d == today) asksToday++;
                    byDate.TryGetValue(d, out var c);
                    byDate[d] = c + 1;
                }

                var weekStart = today.AddDays(-(int)today.DayOfWeek);
                var asksThisWeek = byDate.Where(kv => kv.Key >= weekStart && kv.Key <= today).Sum(kv => kv.Value);

                var last7 = new int[7];
                for (var i = 0; i < 7; i++)
                {
                    var day = today.AddDays(-6 + i);
                    last7[i] = byDate.TryGetValue(day, out var n) ? n : 0;
                }

                var max7 = last7.Max();
                var bars = new List<DashboardDayBar>(7);
                for (var i = 0; i < 7; i++)
                {
                    var day = today.AddDays(-6 + i);
                    var count = last7[i];
                    bars.Add(new DashboardDayBar
                    {
                        DayLabel = day.ToString("ddd", culture),
                        Count = count,
                        BarFill = max7 == 0 ? 0 : (double)count / max7,
                        IsToday = day == today
                    });
                }

                var streak = 0;
                for (var d = today; ; d = d.AddDays(-1))
                {
                    if (byDate.TryGetValue(d, out var n) && n > 0)
                        streak++;
                    else
                        break;
                }

                var asksLast7 = last7.Sum();
                var avg7 = asksLast7 / 7.0;

                DateTime? busiestDate = null;
                var busiestCount = 0;
                foreach (var kv in byDate)
                {
                    if (kv.Value > busiestCount)
                    {
                        busiestCount = kv.Value;
                        busiestDate = kv.Key;
                    }
                }

                var busiestLabel = busiestDate.HasValue && busiestCount > 0
                    ? busiestDate.Value.ToString("dddd", culture)
                    : null;

                var pinned = items
                    .Where(e => e.Pinned)
                    .OrderByDescending(e => e.UtcTicks)
                    .Take(3)
                    .Select(e =>
                    {
                        var when = new DateTime(e.UtcTicks, DateTimeKind.Utc).ToLocalTime();
                        return new DashboardChatRow
                        {
                            UtcTicks = e.UtcTicks,
                            IsPinned = true,
                            Title = e.Title,
                            Preview = string.IsNullOrWhiteSpace(e.Preview) ? "—" : e.Preview,
                            WhenLabel = when.ToString("MMM d · h:mm tt", culture)
                        };
                    })
                    .ToList();

                var heroSubtitle = BuildHeroSubtitle(
                    streak,
                    asksThisWeek,
                    asksLast7,
                    busiestLabel,
                    busiestCount,
                    items.Count);

                return new DashboardInsights
                {
                    TotalAsks = items.Count,
                    AsksToday = asksToday,
                    PinnedCount = items.Count(e => e.Pinned),
                    StreakDays = streak,
                    AsksThisWeek = asksThisWeek,
                    AsksLast7Days = asksLast7,
                    AvgPerDay7 = avg7,
                    BusiestDayLabel = busiestLabel,
                    BusiestDayCount = busiestCount,
                    Last7Days = bars,
                    PinnedHighlights = pinned,
                    HeroSubtitle = heroSubtitle
                };
            }
            catch
            {
                return new DashboardInsights
                {
                    HeroSubtitle = SettingsStore.GetEmptyStateHint()
                };
            }
        }
    }

    private static string BuildHeroSubtitle(
        int streak,
        int asksThisWeek,
        int asksLast7,
        string? busiestDay,
        int busiestCount,
        int total)
    {
        if (total == 0)
            return SettingsStore.GetEmptyStateHint();

        var parts = new List<string>();
        if (streak >= 2)
            parts.Add($"{streak}-day streak");
        if (asksThisWeek > 0)
            parts.Add(asksThisWeek == 1 ? "1 ask this week" : $"{asksThisWeek} asks this week");
        else if (asksLast7 > 0 && parts.Count == 0)
            parts.Add($"{asksLast7} in the last 7 days");

        if (busiestDay != null && busiestCount >= 2)
            parts.Add($"busiest on {busiestDay} ({busiestCount})");

        return parts.Count > 0
            ? string.Join(" · ", parts)
            : "Your history is ready — select text to ask again.";
    }

    /// <summary>Pinned chats, optional divider, then unpinned chats (for sidebar list).</summary>
    public static IReadOnlyList<object> ReadRecentForSidebar(int max = 100)
    {
        var rows = ReadRecentForUi(max);
        var pinned = rows.Where(r => r.IsPinned).ToList();
        var rest = rows.Where(r => !r.IsPinned).ToList();
        if (pinned.Count == 0 || rest.Count == 0)
            return pinned.Concat<object>(rest).ToList();

        return pinned.Cast<object>().Append(new ChatHistorySectionDivider()).Concat(rest).ToList();
    }

    public static IReadOnlyList<DashboardChatRow> ReadRecentForUi(int max = 40)
    {
        return ReadRecent(max).Select(MapRow).ToList();

        static DashboardChatRow MapRow(ChatHistoryEntryDto e)
        {
            var when = new DateTime(e.UtcTicks, DateTimeKind.Utc).ToLocalTime();
            return new DashboardChatRow
            {
                UtcTicks = e.UtcTicks,
                IsPinned = e.Pinned,
                Title = e.Title,
                Preview = string.IsNullOrWhiteSpace(e.Preview) ? "—" : e.Preview,
                WhenLabel = when.ToString("MMM d · h:mm tt", System.Globalization.CultureInfo.CurrentCulture)
            };
        }
    }

    public static IReadOnlyList<ChatHistoryEntryDto> ReadRecent(int max = 40)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return Array.Empty<ChatHistoryEntryDto>();
                var raw = File.ReadAllText(FilePath);
                var dto = JsonSerializer.Deserialize<ChatHistoryFileDto>(raw, JsonOpts);
                if (dto?.Items == null || dto.Items.Count == 0) return Array.Empty<ChatHistoryEntryDto>();
                return dto.Items
                    .OrderByDescending(e => e.Pinned)
                    .ThenByDescending(e => e.UtcTicks)
                    .Take(max)
                    .ToList();
            }
            catch
            {
                return Array.Empty<ChatHistoryEntryDto>();
            }
        }
    }

    public static bool RemoveByUtcTicks(long utcTicks)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var cur = JsonSerializer.Deserialize<ChatHistoryFileDto>(File.ReadAllText(FilePath), JsonOpts);
                if (cur?.Items == null) return false;
                var n = cur.Items.RemoveAll(e => e.UtcTicks == utcTicks);
                if (n == 0) return false;
                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool TryUpdateTitle(long utcTicks, string newTitle)
    {
        var t = ChatTitleService.NormalizeTitle(newTitle);
        if (t.Length == 0) return false;
        t = MakeTitleUnique(t, utcTicks);
        return TryRename(utcTicks, t);
    }

    public static IReadOnlyList<long> ReadAllUtcTicks()
    {
        lock (Gate)
        {
            try
            {
                var cur = LoadFileOrEmpty();
                return cur.Items?.Select(x => x.UtcTicks).ToList() ?? new List<long>();
            }
            catch
            {
                return Array.Empty<long>();
            }
        }
    }

    private static string MakeTitleUnique(string title, long excludeUtcTicks)
    {
        lock (Gate)
        {
            try
            {
                var cur = LoadFileOrEmpty();
                if (cur.Items == null) return title;

                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in cur.Items)
                {
                    if (e.UtcTicks == excludeUtcTicks) continue;
                    var n = ChatTitleService.NormalizeTitle(e.Title);
                    if (n.Length > 0) taken.Add(n);
                }

                if (!taken.Contains(title)) return title;

                for (var i = 2; i < 100; i++)
                {
                    var candidate = $"{title} ({i})";
                    if (candidate.Length > MaxTitleLen)
                        candidate = title[..Math.Max(1, MaxTitleLen - $" ({i})".Length)] + $" ({i})";
                    if (!taken.Contains(candidate)) return candidate;
                }

                return title + " ·";
            }
            catch
            {
                return title;
            }
        }
    }

    public static bool TryRename(long utcTicks, string newTitle)
    {
        var t = (newTitle ?? "").Trim().ReplaceLineEndings(" ");
        if (t.Length == 0) return false;
        if (t.Length > MaxTitleLen) t = t[..MaxTitleLen] + "…";

        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var cur = JsonSerializer.Deserialize<ChatHistoryFileDto>(File.ReadAllText(FilePath), JsonOpts);
                if (cur?.Items == null) return false;
                var e = cur.Items.FirstOrDefault(x => x.UtcTicks == utcTicks);
                if (e == null) return false;
                e.Title = t;
                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetPinned(long utcTicks, bool pinned)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var cur = JsonSerializer.Deserialize<ChatHistoryFileDto>(File.ReadAllText(FilePath), JsonOpts);
                if (cur?.Items == null) return;
                var e = cur.Items.FirstOrDefault(x => x.UtcTicks == utcTicks);
                if (e == null || e.Pinned == pinned) return;
                e.Pinned = pinned;
                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static bool TryGetEntry(long utcTicks, out ChatHistoryEntryDto? entry)
    {
        entry = null;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var cur = JsonSerializer.Deserialize<ChatHistoryFileDto>(File.ReadAllText(FilePath), JsonOpts);
                entry = cur?.Items?.FirstOrDefault(x => x.UtcTicks == utcTicks);
                return entry != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Removes all saved chat rows (dashboard “Recent chats”).</summary>
    public static void ClearAll()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                var empty = new ChatHistoryFileDto { Items = new List<ChatHistoryEntryDto>() };
                WriteFile(empty);
                ChatHistoryChanged?.Invoke();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static void Append(string selection, string question, string answer) =>
        _ = AppendAndGetTicks(selection, question, answer);

    /// <summary>Appends a chat row and returns its <see cref="ChatHistoryEntryDto.UtcTicks"/>.</summary>
    public static long? AppendAndGetTicks(string selection, string question, string answer)
    {
        var sel = TrimCap(selection, MaxSelectionLen);
        var q = TrimCap(question, MaxQuestionLen);
        var ans = TrimCap(answer, MaxAnswerLen);
        if (q.Length == 0 && sel.Length == 0) return null;
        if (ans.Length == 0) return null;

        var t = ChatTitleService.BuildHeuristicTitle(sel, q, ans);
        var p = TrimCap(ans, MaxPreviewLen);
        var ticks = DateTime.UtcNow.Ticks;

        lock (Gate)
        {
            try
            {
                var cur = LoadFileOrEmpty();
                cur.Items ??= new List<ChatHistoryEntryDto>();
                var entry = new ChatHistoryEntryDto
                {
                    UtcTicks = ticks,
                    Title = t,
                    Selection = sel,
                    Question = q,
                    Preview = p,
                    Answer = ans,
                    Messages = BuildInitialMessages(q, ans)
                };
                cur.Items.Insert(0, entry);

                while (cur.Items.Count > MaxItems)
                    cur.Items.RemoveAt(cur.Items.Count - 1);

                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
                return ticks;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool UpdateAnswer(long utcTicks, string answer)
    {
        var ans = TrimCap(answer, MaxAnswerLen);
        if (ans.Length == 0) return false;

        lock (Gate)
        {
            try
            {
                var cur = LoadFileOrEmpty();
                if (cur.Items == null) return false;
                var e = cur.Items.FirstOrDefault(x => x.UtcTicks == utcTicks);
                if (e == null) return false;
                e.Answer = ans;
                e.Preview = TrimCap(ans, MaxPreviewLen);
                SyncAssistantTail(e, ans);
                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Appends a user message and assistant reply as separate turns.</summary>
    public static bool AppendThreadTurn(long utcTicks, string userText, string assistantText)
    {
        var user = TrimCap(userText, MaxQuestionLen);
        var assistant = TrimCap(assistantText, MaxAnswerLen);
        if (user.Length == 0 || assistant.Length == 0) return false;

        lock (Gate)
        {
            try
            {
                var cur = LoadFileOrEmpty();
                if (cur.Items == null) return false;
                var e = cur.Items.FirstOrDefault(x => x.UtcTicks == utcTicks);
                if (e == null) return false;

                e.Messages ??= MigrateLegacyMessages(e);
                e.Messages.Add(new ChatThreadMessageDto { Role = "user", Text = user });
                e.Messages.Add(new ChatThreadMessageDto { Role = "assistant", Text = assistant });
                e.Answer = SerializeMessagesToLegacyAnswer(e);
                e.Preview = TrimCap(assistant, MaxPreviewLen);
                WriteFile(cur);
                ChatHistoryChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static IReadOnlyList<ChatThreadMessageDto> GetThreadMessages(ChatHistoryEntryDto e)
    {
        if (e.Messages is { Count: > 0 })
            return e.Messages;

        return MigrateLegacyMessages(e);
    }

    private static List<ChatThreadMessageDto> BuildInitialMessages(string question, string answer)
    {
        var list = new List<ChatThreadMessageDto>();
        var q = (question ?? "").Trim();
        if (q.Length > 0)
            list.Add(new ChatThreadMessageDto { Role = "user", Text = q });
        list.Add(new ChatThreadMessageDto { Role = "assistant", Text = answer });
        return list;
    }

    private static List<ChatThreadMessageDto> MigrateLegacyMessages(ChatHistoryEntryDto e)
    {
        var list = new List<ChatThreadMessageDto>();
        foreach (var m in ChatThreadParser.Parse(e.Selection ?? "", e.Question ?? "", e.Answer ?? ""))
        {
            if (m.Kind == ChatMessageKind.Context) continue;
            list.Add(new ChatThreadMessageDto
            {
                Role = m.Kind == ChatMessageKind.User ? "user" : "assistant",
                Text = m.Text
            });
        }

        if (list.Count > 0) return list;
        var (_, question, answer) = GetDisplayParts(e);
        return BuildInitialMessages(question, answer);
    }

    private static void SyncAssistantTail(ChatHistoryEntryDto e, string assistantText)
    {
        e.Messages ??= MigrateLegacyMessages(e);
        if (e.Messages.Count == 0)
        {
            e.Messages = BuildInitialMessages(e.Question ?? "", assistantText);
            return;
        }

        var last = e.Messages[^1];
        if (last.IsAssistant)
            last.Text = assistantText;
        else
            e.Messages.Add(new ChatThreadMessageDto { Role = "assistant", Text = assistantText });
    }

    private static string SerializeMessagesToLegacyAnswer(ChatHistoryEntryDto e)
    {
        if (e.Messages == null || e.Messages.Count == 0)
            return e.Answer ?? "";

        var assistants = e.Messages.Where(m => m.IsAssistant).Select(m => m.Text.Trim()).Where(t => t.Length > 0).ToList();
        if (assistants.Count == 0) return "";

        var blob = assistants[0];
        var userFollowUps = e.Messages.Where(m => m.IsUser).Skip(string.IsNullOrWhiteSpace(e.Question) ? 0 : 1).ToList();
        var assistantFollowUps = assistants.Skip(1).ToList();
        for (var i = 0; i < userFollowUps.Count && i < assistantFollowUps.Count; i++)
            blob += $"\n\n---\n\n**{userFollowUps[i].Text}**\n\n{assistantFollowUps[i]}";

        return TrimCap(blob, MaxAnswerLen);
    }

    private static ChatHistoryFileDto LoadFileOrEmpty()
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        if (!File.Exists(FilePath))
            return new ChatHistoryFileDto();

        try
        {
            return JsonSerializer.Deserialize<ChatHistoryFileDto>(File.ReadAllText(FilePath), JsonOpts)
                   ?? new ChatHistoryFileDto();
        }
        catch
        {
            return new ChatHistoryFileDto();
        }
    }

    private static void WriteFile(ChatHistoryFileDto dto)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var json = JsonSerializer.Serialize(dto, JsonOpts);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(FilePath))
            File.Replace(tmp, FilePath, null);
        else
            File.Move(tmp, FilePath);
    }

    private static string TrimCap(string? value, int maxLen)
    {
        var s = (value ?? "").Trim().ReplaceLineEndings(" ");
        if (s.Length == 0) return "";
        return s.Length <= maxLen ? s : s[..maxLen] + "…";
    }

    /// <summary>Resolves stored fields for display (handles older entries).</summary>
    public static (string Selection, string Question, string Answer) GetDisplayParts(ChatHistoryEntryDto e)
    {
        var question = (e.Question ?? "").Trim();
        if (question.Length == 0)
            question = (e.Title ?? "").Trim();

        var selection = (e.Selection ?? "").Trim();
        var answer = (e.Answer ?? "").Trim();
        if (answer.Length == 0)
            answer = (e.Preview ?? "").Trim();
        if (answer == "—") answer = "";

        return (selection, question, answer);
    }
}
