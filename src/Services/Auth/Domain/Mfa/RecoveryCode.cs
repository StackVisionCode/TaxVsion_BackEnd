using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Mfa;

public sealed class RecoveryCode : TenantEntity
{
    private RecoveryCode() { }

    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public bool IsUsable => UsedAtUtc is null;

    public static RecoveryCode Create(Guid tenantId, Guid userId, string codeHash)
    {
        var code = new RecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = codeHash,
            CreatedAtUtc = DateTime.UtcNow
        };
        code.SetTenant(tenantId);
        return code;
    }

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
