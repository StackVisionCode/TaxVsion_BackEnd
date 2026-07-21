using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class SpyCodeTokenHasher : ICodeTokenHasher
{
    private readonly List<string> _receivedTokens = [];

    internal IReadOnlyList<string> ReceivedTokens => _receivedTokens;

    public Result<CodeTokenHash> Hash(string codeToken)
    {
        _receivedTokens.Add(codeToken);
        if (string.IsNullOrWhiteSpace(codeToken))
            return Result.Failure<CodeTokenHash>(new Error("TestHasher.InvalidToken", "Code token is required."));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(codeToken));
        return CodeTokenHash.Create(Convert.ToHexStringLower(digest));
    }

    internal static CodeTokenHash HashWithoutObservation(string codeToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(codeToken));
        return CodeTokenHash.Create(Convert.ToHexStringLower(digest)).Value;
    }
}
