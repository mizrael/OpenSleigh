using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.Persistence.SQLServer;

[Obsolete("No longer used by OpenSleigh, which states its table hints in the statement that reads the outbox. It will be removed in a future version.")]
public static class QueryHintsDbContextOptionsBuilderExtensions
{
    public static IQueryable<T> WithHint<T>(this IQueryable<T> source, TableHints hint) =>
        source.TagWith(QueryHintInterceptor.HintTag + hint);
    public static IQueryable<T> WithHint<T>(this IQueryable<T> source, TableHints hint, string param) =>
        source.TagWith(QueryHintInterceptor.HintTag + hint + " (" + param + ")");
}
