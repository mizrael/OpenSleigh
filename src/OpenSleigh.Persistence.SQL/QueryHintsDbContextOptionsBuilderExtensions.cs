using Microsoft.EntityFrameworkCore;

namespace OpenSleigh.Persistence.SQL;

public static class QueryHintsDbContextOptionsBuilderExtensions
{
    public static IQueryable<T> WithHint<T>(this IQueryable<T> source, TableHints hint) =>
        source.TagWith(QueryHintInterceptor.HintTag + hint);
    public static IQueryable<T> WithHint<T>(this IQueryable<T> source, TableHints hint, string param) =>
        source.TagWith(QueryHintInterceptor.HintTag + hint + " (" + param + ")");
}
