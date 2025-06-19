using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace OpenSleigh.Persistence.SQLServer;

public class QueryHintInterceptor : DbCommandInterceptor
{
    private static readonly Regex _hintsRegex = new Regex(@"(?:(?:-- Use hint: )(\S+)\s)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex _tableAliasRegex = new Regex(@"(FROM[\s\r\n]+\S+(?:[\s\r\n]+AS[\s\r\n]+[^\s\r\n]+)?)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public const string HintTag = "Use hint: ";

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        PatchCommandtext(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        PatchCommandtext(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        PatchCommandtext(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        PatchCommandtext(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void PatchCommandtext(DbCommand command)
    {
        var hints = ExtractHints(command.CommandText);
        if (!hints.Any())
            return;

        var hint = hints.Aggregate((a, b) => a + ", " + b).ToUpperInvariant();
        command.CommandText = _tableAliasRegex
            .Replace(command.CommandText, "${0} WITH (" + hint + ")");
    }

    private static IEnumerable<string> ExtractHints(string input)
    {
        var matches = _hintsRegex.Matches(input);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                yield return match.Groups[1].Value;
            }
        }
    }
}