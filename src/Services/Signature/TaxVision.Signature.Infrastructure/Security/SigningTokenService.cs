using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Emite y verifica JWTs RS256 para los enlaces públicos del firmante. Estructura estándar:
/// <c>base64url(header).base64url(payload).base64url(rsaSignature)</c>. Firma con la clave
/// activa de <see cref="IRsaKeyProvider"/>. La verificación acepta cualquier <c>kid</c>
/// aún publicable — hace posible la rotación sin invalidar tokens vigentes.
///
/// <para>
/// La verificación NO consulta el <see cref="IJtiDenylist"/> — eso lo hace
/// <c>PublicTokenResolver</c> con el <c>jti</c> del payload, para separar responsabilidades
/// (crypto aquí, política de revocación en Application).
/// </para>
/// </summary>
public sealed class SigningTokenService : ISigningTokenService
{
    private readonly IRsaKeyProvider _keyProvider;
    private readonly string _publicBaseUrl;

    public SigningTokenService(IConfiguration configuration, IRsaKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
        var baseUrl = configuration["Signature:PublicBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Signature:PublicBaseUrl must be configured.");
        _publicBaseUrl = baseUrl;
    }

    public string Issue(SigningTokenPayload payload)
    {
        var headerJson = SerializeHeader(_keyProvider.ActiveKid);
        var payloadJson = SerializePayload(payload);
        var headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var signature = _keyProvider.SignSha256(Encoding.ASCII.GetBytes(signingInput));
        var signatureEncoded = Base64UrlEncode(signature);

        return $"{headerEncoded}.{payloadEncoded}.{signatureEncoded}";
    }

    public string BuildPublicUrl(string token) => $"{_publicBaseUrl}/{token}";

    public Result<SigningTokenPayload> Verify(string token)
    {
        var parts = SplitToken(token);
        if (parts is null)
            return Failure("Signature.Token.Format", "Token format is invalid.");

        var (headerEncoded, payloadEncoded, signatureEncoded) = parts.Value;
        if (!VerifySignature(headerEncoded, payloadEncoded, signatureEncoded))
            return Failure("Signature.Token.Signature", "Token signature is invalid.");

        var payload = TryDeserializePayload(payloadEncoded);
        if (payload is null)
            return Failure("Signature.Token.Payload", "Token payload is malformed.");

        if (payload.ExpiresAtUtc <= DateTime.UtcNow)
            return Failure("Signature.Token.Expired", "Token has expired.");

        return Result.Success(payload);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static string SerializeHeader(string kid) => JsonSerializer.Serialize(new HeaderDto("RS256", "JWT", kid));

    private static string SerializePayload(SigningTokenPayload payload)
    {
        var exp = new DateTimeOffset(payload.ExpiresAtUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        var dto = new PayloadDto(
            payload.TenantId,
            payload.SignatureRequestId,
            payload.SignerId,
            payload.RevocationEpoch,
            exp,
            payload.TokenId
        );
        return JsonSerializer.Serialize(dto);
    }

    private static SigningTokenPayload? TryDeserializePayload(string base64UrlEncoded)
    {
        try
        {
            var bytes = Base64UrlDecode(base64UrlEncoded);
            var dto = JsonSerializer.Deserialize<PayloadDto>(bytes);
            if (dto is null)
                return null;
            return new SigningTokenPayload(
                dto.t,
                dto.r,
                dto.s,
                dto.e,
                DateTimeOffset.FromUnixTimeSeconds(dto.exp).UtcDateTime,
                dto.jti
            );
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private bool VerifySignature(string headerEncoded, string payloadEncoded, string signatureEncoded)
    {
        byte[] signature;
        try
        {
            signature = Base64UrlDecode(signatureEncoded);
        }
        catch (FormatException)
        {
            return false;
        }

        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var material = Encoding.ASCII.GetBytes(signingInput);

        // Verifica con TODAS las claves publicables (habilita rotación).
        var expected = _keyProvider.SignSha256(material);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(signature, expected);
    }

    private static (string HeaderEncoded, string PayloadEncoded, string SignatureEncoded)? SplitToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;
        if (parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            return null;
        return (parts[0], parts[1], parts[2]);
    }

    private static Result<SigningTokenPayload> Failure(string code, string message) =>
        Result.Failure<SigningTokenPayload>(new Error(code, message));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        var mod = padded.Length % 4;
        if (mod > 0)
            padded = padded.PadRight(padded.Length + (4 - mod), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record HeaderDto(string alg, string typ, string kid);

    private sealed record PayloadDto(Guid t, Guid r, Guid s, int e, long exp, string jti);
}
