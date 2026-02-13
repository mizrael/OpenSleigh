namespace OpenSleigh.Queries;

public record SagaQueryFilter
{
    public string? SagaType { get; init; }
    public bool? IsCompleted { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
