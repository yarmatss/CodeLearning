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

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\0')
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                builder.Append(ReplacementCharacter);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool NeedsSanitizing(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\0')
            {
                return true;
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }
}
