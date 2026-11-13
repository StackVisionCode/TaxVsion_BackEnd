using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.PublicBookings;

/// <summary>
/// HMAC-SHA256 del email con una clave derivada (HKDF) de <c>Encryption:MasterKey</c>, la misma que ya exige el
/// cifrado de los secretos. Sin clave falla cerrado: no se guarda una llave reversible.
/// </summary>
public sealed class HmacRequesterKeyHasher(IOptions<RotatingSecretProtectionOptions> options) : IRequesterKeyHasher
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("taxvision.calendar.public_booking.requester.v1");

    private readonly Lazy<byte[]> _key = new(() => Derive(options.Value.MasterKey));

    public string KeyOf(WorkEmail email) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(email.Value)));

    private static byte[] Derive(string? masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException("Encryption:MasterKey must be configured for public bookings.");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromBase64String(masterKey), 32, info: Purpose);
    }
}
