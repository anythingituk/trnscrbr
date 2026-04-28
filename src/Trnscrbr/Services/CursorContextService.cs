using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Trnscrbr.Services;

public sealed class CursorContextService
{
    private const int ContextCharacters = 600;

    public string TryReadFocusedContext()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return string.Empty;
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject)
                && textPatternObject is TextPattern textPattern)
            {
                return ReadTextPatternContext(textPattern);
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject)
                && valuePatternObject is ValuePattern valuePattern)
            {
                return TrimContext(valuePattern.Current.Value);
            }
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static string ReadTextPatternContext(TextPattern textPattern)
    {
        var selections = textPattern.GetSelection();
        if (selections.Length == 0)
        {
            return TrimContext(textPattern.DocumentRange.GetText(ContextCharacters));
        }

        var caretRange = selections[0].Clone();
        var before = caretRange.Clone();
        var after = caretRange.Clone();

        before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -ContextCharacters);
        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, caretRange, TextPatternRangeEndpoint.Start);

        after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caretRange, TextPatternRangeEndpoint.End);
        after.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, ContextCharacters);

        var beforeText = before.GetText(ContextCharacters).Trim();
        var selectedText = caretRange.GetText(ContextCharacters).Trim();
        var afterText = after.GetText(ContextCharacters).Trim();

        return TrimContext(string.Join(
            Environment.NewLine,
            new[] { beforeText, selectedText, afterText }.Where(part => !string.IsNullOrWhiteSpace(part))));
    }

    private static string TrimContext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = string.Join(" ", text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length <= ContextCharacters)
        {
            return compact;
        }

        return compact[^ContextCharacters..];
    }
}
