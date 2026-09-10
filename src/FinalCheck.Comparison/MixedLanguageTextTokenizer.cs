using System.Globalization;

namespace FinalCheck.Comparison;

public sealed class MixedLanguageTextTokenizer : ITextTokenizer
{
    public IReadOnlyList<TextToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<TextToken>();
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var character = text[index];
            TextTokenKind kind;
            if (IsCjk(character))
            {
                kind = TextTokenKind.CjkCharacter;
                index++;
            }
            else if (char.IsLetter(character))
            {
                kind = TextTokenKind.Word;
                index++;
                while (index < text.Length && IsWordContinuation(text[index]))
                {
                    index++;
                }
            }
            else if (char.IsDigit(character))
            {
                kind = TextTokenKind.Number;
                index++;
                while (index < text.Length && IsNumberContinuation(text[index]))
                {
                    index++;
                }
            }
            else if (char.IsWhiteSpace(character))
            {
                kind = TextTokenKind.Whitespace;
                index++;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
            }
            else
            {
                kind = TextTokenKind.Punctuation;
                index++;
            }

            tokens.Add(new TextToken(text[start..index], start, index - start, kind));
        }

        return tokens;
    }

    private static bool IsWordContinuation(char character) =>
        char.IsLetter(character) ||
        char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static bool IsNumberContinuation(char character) =>
        char.IsDigit(character) || character is '.' or ',' or '%' or '％';

    private static bool IsCjk(char character) => character is
        >= '\u3400' and <= '\u4DBF' or
        >= '\u4E00' and <= '\u9FFF' or
        >= '\uF900' and <= '\uFAFF';
}
