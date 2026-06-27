using System;
using System.Text;

namespace DynaCall;

/// <summary>
/// Converts DynamicWrapperX-style hex strings into raw byte arrays, stripping
/// whitespace and two comment formats before conversion.
/// </summary>
/// <remarks>
/// <para><b>Supported comment styles</b></para>
/// <list type="bullet">
///   <item>
///     <b>Parenthetical</b> — <c>4889C8 (mov rax,rcx) 48F7EA C3</c>
///   </item>
///   <item>
///     <b>End-of-line</b> — <c>4889C8 ; mov rax,rcx</c> (semicolon to next newline)
///   </item>
/// </list>
/// Spaces, tabs, CR, and LF between hex pairs are silently ignored.
/// </remarks>
internal static class HexParser
{
    /// <summary>
    /// Parses a (potentially commented, whitespace-separated) hex string into bytes.
    /// </summary>
    /// <exception cref="FormatException">
    /// The stripped string contains a non-hex character or an odd number of digits.
    /// </exception>
    public static byte[] Parse(string hexStr)
    {
        if (string.IsNullOrEmpty(hexStr)) return Array.Empty<byte>();

        var digits = ExtractDigits(hexStr);

        if (digits.Length % 2 != 0)
            throw new FormatException(
                "Hex string has an odd number of digits after stripping comments and whitespace.");

        var result = new byte[digits.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(digits.Substring(i * 2, 2), 16);

        return result;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string ExtractDigits(string input)
    {
        var sb = new StringBuilder(input.Length);
        var i  = 0;

        while (i < input.Length)
        {
            var c = input[i];

            if (c == '(')
            {
                // Parenthetical comment — skip to matching ')'
                i++;
                var depth = 1;
                while (i < input.Length && depth > 0)
                {
                    if      (input[i] == '(') depth++;
                    else if (input[i] == ')') depth--;
                    i++;
                }
            }
            else if (c == ';')
            {
                // End-of-line comment — skip to next newline
                while (i < input.Length && input[i] != '\n')
                    i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (IsHexDigit(c))
            {
                sb.Append(c);
                i++;
            }
            else
            {
                throw new FormatException(
                    "Unexpected character '" + c + "' at index " + i.ToString() +
                    " in hex string.");
            }
        }

        return sb.ToString();
    }

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9') ||
           (c >= 'a' && c <= 'f') ||
           (c >= 'A' && c <= 'F');
}
