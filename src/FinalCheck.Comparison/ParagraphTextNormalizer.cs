using System.Globalization;
using System.Text;

namespace FinalCheck.Comparison;

public static class ParagraphTextNormalizer
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Normalize(NormalizationForm.FormKC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ')
            .Replace('\u3000', ' ');
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        var pendingBreak = false;
        foreach (var character in normalized)
        {
            if (character == '\n')
            {
                pendingSpace = false;
                pendingBreak = builder.Length > 0;
                continue;
            }

            if (character == '\t' || char.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator)
            {
                pendingSpace = builder.Length > 0 && !pendingBreak;
                continue;
            }

            if (pendingBreak)
            {
                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.Append('\n');
                }

                pendingBreak = false;
            }
            else if (pendingSpace && builder.Length > 0 && builder[^1] is not (' ' or '\n'))
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    internal static IReadOnlySet<string> Tokenize(string normalizedText)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();
        foreach (var character in normalizedText)
        {
            if (IsCjk(character))
            {
                FlushWord(word, tokens);
                tokens.Add(character.ToString());
            }
            else if (char.IsLetterOrDigit(character))
            {
                word.Append(char.ToLowerInvariant(character));
            }
            else
            {
                FlushWord(word, tokens);
                if (!char.IsWhiteSpace(character))
                {
                    tokens.Add(character.ToString());
                }
            }
        }

        FlushWord(word, tokens);
        return tokens;
    }

    internal static string? GetHeadingKey(string normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return null;
        }

        if (normalizedText[0] == '第')
        {
            var end = normalizedText.IndexOf('条', 1);
            if (end is > 1 and <= 12)
            {
                return normalizedText[..(end + 1)];
            }
        }

        var prefixLength = 0;
        while (prefixLength < normalizedText.Length && prefixLength < 12 &&
               (char.IsDigit(normalizedText[prefixLength]) || normalizedText[prefixLength] == '.'))
        {
            prefixLength++;
        }

        if (prefixLength > 0 && prefixLength < normalizedText.Length &&
            normalizedText[prefixLength] is ' ' or '、' or '．')
        {
            return normalizedText[..prefixLength].TrimEnd('.');
        }

        const string chineseNumbers = "一二三四五六七八九十百千万零〇";
        prefixLength = 0;
        while (prefixLength < normalizedText.Length && prefixLength < 12 &&
               chineseNumbers.Contains(normalizedText[prefixLength]))
        {
            prefixLength++;
        }

        if (prefixLength > 0 && prefixLength < normalizedText.Length && normalizedText[prefixLength] == '、')
        {
            return normalizedText[..prefixLength];
        }

        return null;
    }

    private static bool IsCjk(char character) => character is
        >= '\u3400' and <= '\u4DBF' or
        >= '\u4E00' and <= '\u9FFF' or
        >= '\uF900' and <= '\uFAFF';

    private static void FlushWord(StringBuilder word, HashSet<string> tokens)
    {
        if (word.Length == 0)
        {
            return;
        }

        tokens.Add(word.ToString());
        word.Clear();
    }
}
