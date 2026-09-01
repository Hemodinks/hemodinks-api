namespace HemodinksAPI.Application.Data;

/// <summary>
/// Provider-neutral application boundary for full-text predicates.
/// The database provider maps this method to its native implementation.
/// </summary>
public static class FullTextSearch
{
    public static bool Contains(string propertyReference, string searchCondition)
    {
        throw new InvalidOperationException(
            $"{nameof(Contains)} can only be used in a database query.");
    }
}
