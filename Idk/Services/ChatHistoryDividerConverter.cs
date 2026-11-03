using System.Globalization;
using System.Windows.Data;

namespace Idk.Services;

public sealed class ChatHistoryDividerConverter : IValueConverter
{
    public static readonly ChatHistoryDividerConverter Instance = new();

    private ChatHistoryDividerConverter() { }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ChatHistorySectionDivider;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
