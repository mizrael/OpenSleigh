namespace OpenSleigh.Reporting.Endpoints;

internal static class Pagination
{
    private const int MaxPageSize = 100;

    public const string InvalidMessage = "Invalid pagination parameters. Page must be >= 1, pageSize must be between 1 and 100.";

    public static bool IsValid(int page, int pageSize)
        => page >= 1 && pageSize >= 1 && pageSize <= MaxPageSize;
}
