using System.Security.Cryptography;
using System.Text;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

public sealed class InvitationTokenService : IInvitationTokenService
{
    public InvitationToken Generate()
    {
        var rawToken = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        return new InvitationToken(rawToken, Hash(rawToken));
    }

    public string Hash(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return string.Empty;

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
