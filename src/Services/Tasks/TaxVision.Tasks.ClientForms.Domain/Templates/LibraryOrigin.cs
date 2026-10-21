using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Templates;

public sealed record LibraryOrigin(string LibraryKey, Guid SourceTemplateId, int SourceVersionNumber)
{
    public static Result<LibraryOrigin> Create(string? libraryKey, Guid sourceTemplateId, int sourceVersionNumber)
    {
        var key = libraryKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || sourceTemplateId == Guid.Empty || sourceVersionNumber < 1)
            return Result.Failure<LibraryOrigin>(FormTemplateErrors.InvalidLibraryOrigin);
        return Result.Success(new LibraryOrigin(key, sourceTemplateId, sourceVersionNumber));
    }
}
