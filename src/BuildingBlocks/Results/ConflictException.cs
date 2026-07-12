namespace BuildingBlocks.Results;

public sealed class ConflictException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
