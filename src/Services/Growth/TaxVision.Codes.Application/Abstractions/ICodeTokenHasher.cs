using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeTokenHasher
{
    /// <summary>
    /// Hashes the complete token inside the Codes trust boundary using the configured pepper.
    /// Implementations must never log, persist, or return the clear-text token.
    /// </summary>
    Result<CodeTokenHash> Hash(string codeToken);
}
