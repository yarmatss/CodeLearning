using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodeLearning.Infrastructure.Extensions;

public static class DbUpdateExceptionExtensions
{
    extension(DbUpdateException exception)
    {
        // A unique index rejected the insert: typically two identical requests racing past the same
        // "does it already exist?" check (double-clicked submit, enroll or complete)
        public bool IsUniqueViolation() =>
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

}
