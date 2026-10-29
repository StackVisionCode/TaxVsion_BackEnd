using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Infrastructure.Security;

namespace TaxVision.Customer.Tests.Application;

// Candidatos de blind index por versión de clave y versión actual expuesta (B-9/X-9). El BI con la clave
// actual no cambia (retrocompat); durante la ventana de rotación se suma el de la clave anterior.
public sealed class AesGcmSensitiveDataProtectorTests
{
    private const string Ssn = "123456789";
    private static readonly byte[] KeyV1 = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] KeyV2 = RandomNumberGenerator.GetBytes(32);

    private static AesGcmSensitiveDataProtector Protector(
        byte[] currentKey,
        short currentVersion,
        byte[]? previousKey = null,
        short? previousVersion = null
    ) =>
        new(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(currentKey),
                    MasterKeyVersion = currentVersion,
                    PreviousMasterKey = previousKey is null ? null : Convert.ToBase64String(previousKey),
                    PreviousMasterKeyVersion = previousVersion,
                }
            )
        );

    [Fact]
    public void Exposes_the_current_key_version()
    {
        Assert.Equal((short)2, Protector(KeyV2, 2, KeyV1, 1).CurrentKeyVersion);
    }

    [Fact]
    public void Without_a_previous_key_candidates_is_only_the_current_blind_index()
    {
        var tenantId = Guid.NewGuid();
        var protector = Protector(KeyV1, 1);

        var candidates = protector.ComputeBlindIndexCandidates(Ssn, tenantId);

        Assert.Equal(new[] { protector.ComputeBlindIndex(Ssn, tenantId) }, candidates);
    }

    [Fact]
    public void With_a_previous_key_candidates_are_current_first_then_previous()
    {
        var tenantId = Guid.NewGuid();
        var rotating = Protector(KeyV2, 2, KeyV1, 1);
        var previousOnly = Protector(KeyV1, 1);

        var candidates = rotating.ComputeBlindIndexCandidates(Ssn, tenantId).ToList();

        Assert.Equal(2, candidates.Count);
        // El actual va primero y es idéntico a ComputeBlindIndex (retrocompat).
        Assert.Equal(rotating.ComputeBlindIndex(Ssn, tenantId), candidates[0]);
        // El segundo es el BI que la clave anterior habría producido (lo que hay almacenado antes del sweep).
        Assert.Equal(previousOnly.ComputeBlindIndex(Ssn, tenantId), candidates[1]);
        Assert.NotEqual(candidates[0], candidates[1]);
    }
}
