using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>El correo del Owner de la oficina, desde el directorio local (sin llamadas a otros servicios).</summary>
public sealed class OfficeReplyToResolver(IRecipientResolver recipients, IUserEmailResolver emails)
    : IOfficeReplyToResolver
{
    public async Task<string?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var owner = await recipients.ResolveAsync(new TenantOwnerAudience(tenantId), RecipientValidation.None, ct);
        if (owner.IsFailure || owner.Value.Count == 0)
            return null;
        var email = await emails.ResolveAsync(tenantId, owner.Value[0], ct);
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }
}
