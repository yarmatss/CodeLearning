using CodeLearning.Application.Exceptions;

namespace CodeLearning.Infrastructure.Services;

internal static class OrderIndexRules
{
    // Order indexes are 1-based and contiguous; moving outside that range would leave gaps or duplicates
    public static void EnsureWithinRange(int newOrderIndex, int siblingCount)
    {
        if (newOrderIndex < 1 || newOrderIndex > siblingCount)
        {
            throw new BusinessRuleException($"Order index must be between 1 and {siblingCount}");
        }
    }
}
