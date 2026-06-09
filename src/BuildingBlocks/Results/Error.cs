namespace BuildingBlocks;

public sealed record Error(string code, string Message)
{

    public static readonly Error None = new(string.Empty, string.Empty);
}
