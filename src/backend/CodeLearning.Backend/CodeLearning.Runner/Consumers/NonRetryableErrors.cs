using Npgsql;
using System.Text;

namespace CodeLearning.Runner.Consumers;

// Failures caused by the data itself: retrying the same message would fail the same way,
// so the message goes straight to the fault path instead
public static class NonRetryableErrors
{
    private static readonly HashSet<string> DataErrorSqlStates =
    [
        PostgresErrorCodes.CharacterNotInRepertoire, // 22021, e.g. a NUL in text
        PostgresErrorCodes.UntranslatableCharacter, // 22P05
        PostgresErrorCodes.StringDataRightTruncation, // 22001
        PostgresErrorCodes.NumericValueOutOfRange, // 22003
        PostgresErrorCodes.NotNullViolation, // 23502
        PostgresErrorCodes.CheckViolation // 23514
    ];

    public static bool IsNonRetryable(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException postgres when DataErrorSqlStates.Contains(postgres.SqlState):
                case EncoderFallbackException:
                case System.ComponentModel.DataAnnotations.ValidationException:
                    return true;
            }
        }

        return false;
    }
}
