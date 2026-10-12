using System.Globalization;
using System.Text;

namespace TakEngine.Core.Text;

/// <summary>
/// Makes text that someone else controls (the opponent's flag-reason details, PTN, nickname, a relay's refusal reason) safe to
/// show in a terminal or copy out of a page (security review M0, finding 1). Every C0 and C1 control character, DEL and the
/// Unicode bidi controls (ALM U+061C, LRM/RLM U+200E/F, U+202A..U+202E, U+2066..U+2069) become a visible <c>\uXXXX</c>, so the
/// text can't move the cursor, clear the screen, set the window title, write the clipboard or reorder what is shown. Tab, CR, LF
/// and the Unicode line and paragraph separators (U+2028/9) become a space, so one event stays one line. Pure: no I/O.
/// </summary>
public static class DisplayText
{
    /// <summary>The text with nothing a terminal would act on; empty for null.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        int first = 0;
        while (first < text.Length && !NeedsReplacing(text[first]))
            first++;
        if (first == text.Length)
            return text;

        var sb = new StringBuilder(text.Length + 16).Append(text, 0, first);
        for (int i = first; i < text.Length; i++)
        {
            char c = text[i];
            if (IsLineBreakOrTab(c))
                sb.Append(' ');
            else if (IsUnsafe(c))
                sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>C0, DEL, C1, or a bidi control: a character a terminal acts on, or one that reorders the text around it.</summary>
    public static bool IsUnsafe(char c) =>
        char.IsControl(c) || (int)c is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    private static bool IsLineBreakOrTab(char c) => c is '\t' or '\n' or '\r' || (int)c is 0x2028 or 0x2029;

    private static bool NeedsReplacing(char c) => IsUnsafe(c) || IsLineBreakOrTab(c);
}
