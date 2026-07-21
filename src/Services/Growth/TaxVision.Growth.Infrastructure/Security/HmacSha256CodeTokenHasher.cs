using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Security;

public sealed class HmacSha256CodeTokenHasher : ICodeTokenHasher
{
    private readonly byte[] _pepper;

    public HmacSha256CodeTokenHasher(IOptions<CodeTokenHashingOptions> options)
    {
        _pepper = Encoding.UTF8.GetBytes(options.Value.Pepper);
        if (_pepper.Length < 32)
            throw new InvalidOperationException("The Codes token hashing pepper must contain at least 32 bytes.");
    }

    public Result<CodeTokenHash> Hash(string codeToken)
    {
        if (string.IsNullOrWhiteSpace(codeToken))
            return Result.Failure<CodeTokenHash>(new Error("Codes.Token.Required", "A code token is required."));

        var normalized = codeToken.Trim().ToUpperInvariant();
        var digest = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(normalized));
        return CodeTokenHash.Create(Convert.ToHexStringLower(digest));
    }
}
