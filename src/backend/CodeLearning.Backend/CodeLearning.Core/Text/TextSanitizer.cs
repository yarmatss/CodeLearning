using System.Text;

namespace CodeLearning.Core.Text;

public static class TextSanitizer
{
    private const char ReplacementCharacter = '�';

    // PostgreSQL text rejects U+0000 (SQLSTATE 22021) and a lone surrogate cannot be encoded as UTF-8,
    // so either one in program output would fail the whole save. NULs are dropped, lone surrogates replaced
    public static string? ForDatabase(string? value)
    {
        if (string.IsNullOrEmpty(value) || !NeedsSanitizing(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var i = 0;

        while (i < value.Length)
        {
            var c = value[i];

            if (IsSurrogatePairAt(value, i))
            {
                builder.Append(c).Append(value[i + 1]);
                i += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                builder.Append(ReplacementCharacter);
            }
            else if (c != '\0')
            {
                builder.Append(c);
            }

            i++;
        }

        return builder.ToString();
    }

    private static bool NeedsSanitizing(string value)
    {
        var i = 0;

        while (i < value.Length)
        {
            if (IsSurrogatePairAt(value, i))
            {
                i += 2;
                continue;
            }

            if (value[i] == '\0' || char.IsSurrogate(value[i]))
            {
                return true;
            }

            i++;
        }

        return false;
    }

    private static bool IsSurrogatePairAt(string value, int index) =>
        char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
}
