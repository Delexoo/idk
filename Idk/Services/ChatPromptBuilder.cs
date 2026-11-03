using System.Text;

namespace Idk.Services;

/// <summary>Compact API prompts — highlight + question only, no meta setup lines.</summary>
public static class ChatPromptBuilder
{
    public static string BuildInitialUserMessage(
        string highlight,
        string question,
        bool includeScreenCapture)
    {
        var sb = new StringBuilder();
        AppendHighlightBlock(sb, highlight);
        sb.Append(question.Trim());

        if (includeScreenCapture)
            sb.Append("\n\n[Screenshot attached.]");

        return sb.ToString().Trim();
    }

    public static string BuildFollowUpUserMessage(
        string selection,
        IReadOnlyList<ChatThreadMessageDto> turns,
        string followUp)
    {
        var sb = new StringBuilder();
        AppendHighlightBlock(sb, selection);

        foreach (var turn in turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Text)) continue;
            sb.AppendLine(turn.IsUser ? "User:" : "Assistant:");
            sb.AppendLine(turn.Text.Trim());
            sb.AppendLine();
        }

        sb.Append(followUp.Trim());
        return sb.ToString().Trim();
    }

    private static void AppendHighlightBlock(StringBuilder sb, string highlight)
    {
        var h = (highlight ?? "").Trim();
        if (h.Length == 0) return;

        foreach (var line in h.Split('\n'))
            sb.AppendLine("> " + line.TrimEnd());
        sb.AppendLine();
    }
}
