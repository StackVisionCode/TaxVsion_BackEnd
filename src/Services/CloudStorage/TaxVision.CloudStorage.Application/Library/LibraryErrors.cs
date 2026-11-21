using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Application.Library;

public static class LibraryErrors
{
    public const string InvalidQueryCode = "CloudStorage.Library.InvalidQuery";

    public static readonly Error SourceUnavailable = new(
        "CloudStorage.Library.SourceUnavailable",
        "The service that owns this file did not respond. Try again."
    );

    public static Error InvalidQuery(string message) => new(InvalidQueryCode, message);
}
