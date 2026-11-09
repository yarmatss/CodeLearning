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

    // Moves an item to newOrderIndex and shifts the siblings between its old and new place by one, keeping
    // the indexes contiguous. Shared by chapters, subchapters and blocks
    public static void Move<T>(
        T item,
        IEnumerable<T> otherSiblings,
        Func<T, int> getIndex,
        Action<T, int> setIndex,
        int newOrderIndex)
    {
        var oldOrderIndex = getIndex(item);
        if (oldOrderIndex == newOrderIndex)
        {
            return;
        }

        foreach (var sibling in otherSiblings)
        {
            var index = getIndex(sibling);

            if (newOrderIndex < oldOrderIndex && index >= newOrderIndex && index < oldOrderIndex)
            {
                setIndex(sibling, index + 1);
            }
            else if (newOrderIndex > oldOrderIndex && index > oldOrderIndex && index <= newOrderIndex)
            {
                setIndex(sibling, index - 1);
            }
        }

        setIndex(item, newOrderIndex);
    }
}
