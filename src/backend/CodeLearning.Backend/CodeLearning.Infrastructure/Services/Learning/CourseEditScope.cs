using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodeLearning.Infrastructure.Services.Learning;

// Every structural edit, publish and unpublish of a course runs in a transaction that first locks the course
// row (SELECT ... FOR UPDATE). Concurrent edits of one course therefore run one after another, so "max + 1"
// order indexes, reindexing and the publish checks always see a stable chapter/subchapter/block tree.
// Ownership and status must be checked after BeginAsync, on data read under the lock.
internal sealed class CourseEditScope : IAsyncDisposable
{
    private readonly IDbContextTransaction? _ownedTransaction;

    private CourseEditScope(IDbContextTransaction? ownedTransaction)
    {
        _ownedTransaction = ownedTransaction;
    }

    public static async Task<CourseEditScope> BeginAsync(ApplicationDbContext context, Guid courseId)
    {
        // Join a transaction the caller already opened instead of failing on a nested BeginTransaction
        var ownedTransaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync()
            : null;

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Courses" WHERE "Id" = {courseId} FOR UPDATE""");
        }
        catch
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
            throw;
        }

        return new CourseEditScope(ownedTransaction);
    }

    public Task CommitAsync() => _ownedTransaction?.CommitAsync() ?? Task.CompletedTask;

    // Disposing without CommitAsync rolls the transaction back
    public ValueTask DisposeAsync() => _ownedTransaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
