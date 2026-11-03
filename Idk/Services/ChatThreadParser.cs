using System.Text.RegularExpressions;

namespace Idk.Services;

public enum ChatMessageKind
{
    Context,
    User,
    Assistant
}

public sealed class ChatMessageVm
{
    public ChatMessageKind Kind { get; }
    public string Text { get; }
    public bool IsStreaming { get; }

    public ChatMessageVm(ChatMessageKind kind, string text, bool isStreaming = false)
    {
        Kind = kind;
        Text = text;
        IsStreaming = isStreaming;
    }
}

/// <summary>Parses stored selection / question / answer into a chat-style message list.</summary>
public static class ChatThreadParser
{
    private static readonly Regex FollowUpBlockRx = new(
        @"^\*\*(.+?)\*\*\s*\n\n([\s\S]*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<ChatMessageVm> Parse(string selection, string question, string answerBlob)
    {
        var messages = new List<ChatMessageVm>();

        var sel = (selection ?? "").Trim();
        if (sel.Length > 0)
            messages.Add(new ChatMessageVm(ChatMessageKind.Context, sel));

        var q = (question ?? "").Trim();
        if (q.Length > 0)
            messages.Add(new ChatMessageVm(ChatMessageKind.User, q));

        var blob = (answerBlob ?? "").Trim();
        if (blob.Length == 0)
            return messages;

        var segments = blob.Split("\n\n---\n\n", StringSplitOptions.None);
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (seg.Length == 0) continue;

            if (i == 0)
            {
                messages.Add(new ChatMessageVm(ChatMessageKind.Assistant, seg));
                continue;
            }

            var m = FollowUpBlockRx.Match(seg);
            if (m.Success)
            {
                var followUp = m.Groups[1].Value.Trim();
                var reply = m.Groups[2].Value.Trim();
                if (followUp.Length > 0)
                    messages.Add(new ChatMessageVm(ChatMessageKind.User, followUp));
                if (reply.Length > 0)
                    messages.Add(new ChatMessageVm(ChatMessageKind.Assistant, reply));
            }
            else
            {
                messages.Add(new ChatMessageVm(ChatMessageKind.Assistant, seg));
            }
        }

        return messages;
    }

    public static IReadOnlyList<ChatMessageVm> ParseEntry(ChatHistoryEntryDto e)
    {
        var messages = new List<ChatMessageVm>();
        var sel = (e.Selection ?? "").Trim();
        if (sel.Length > 0)
            messages.Add(new ChatMessageVm(ChatMessageKind.Context, sel));

        foreach (var turn in ChatHistoryStore.GetThreadMessages(e))
        {
            if (turn.IsUser)
                messages.Add(new ChatMessageVm(ChatMessageKind.User, turn.Text));
            else if (turn.IsAssistant)
                messages.Add(new ChatMessageVm(ChatMessageKind.Assistant, turn.Text));
        }

        return messages;
    }
}
