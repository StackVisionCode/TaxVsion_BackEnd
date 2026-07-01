namespace BuildingBlocks.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, int TotalCount)
{
    public int TotalPages => Size <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / Size);
    public bool HasMore => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
