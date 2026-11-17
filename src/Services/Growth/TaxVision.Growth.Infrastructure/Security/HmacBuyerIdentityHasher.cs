using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TaxVision.Growth.SharedKernel.Buyers;

namespace TaxVision.Growth.Infrastructure.Security;

/// <summary>
/// Comprador = HMAC del email normalizado con el pepper de códigos (sin secreto nuevo). El prefijo de versión separa
/// este uso del hash de los códigos y permite cambiar las reglas sin mezclar ids.
/// </summary>
public sealed class HmacBuyerIdentityHasher : IBuyerIdentityHasher
{
    private readonly byte[] _pepper;

    public HmacBuyerIdentityHasher(IOptions<CodeTokenHashingOptions> options)
    {
        _pepper = Encoding.UTF8.GetBytes(options.Value.Pepper);
        if (_pepper.Length < 32)
            throw new InvalidOperationException("The Codes token hashing pepper must contain at least 32 bytes.");
    }

    public BuyerIdentity Hash(NormalizedEmail email)
    {
        var input = $"buyer-identity:v{BuyerIdentity.CurrentVersion}:{email.Value}";
        var digest = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(input));
        return new BuyerIdentity(BuyerIdentity.CurrentVersion, new Guid(digest.AsSpan(0, 16)));
    }
}
