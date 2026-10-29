namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>Quién cambia la foto: el propio cliente desde el portal o el staff desde el CRM.</summary>
public enum CustomerAvatarCaller
{
    PortalSelf,
    Staff,
}

/// <summary>
/// <paramref name="Content"/> son los bytes tal como los subió el usuario; el handler los normaliza.
/// <paramref name="CallerCanViewAllCustomers"/> solo aplica al staff (bypass de asignación).
/// </summary>
public sealed record UploadCustomerAvatarCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    CustomerAvatarCaller Caller,
    bool CallerCanViewAllCustomers,
    byte[] Content
);

/// <param name="Status">Siempre "processing": la foto se muestra cuando el antivirus la aprueba.</param>
public sealed record UploadCustomerAvatarResponse(Guid FileId, string Status);

public sealed record RemoveCustomerAvatarCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    CustomerAvatarCaller Caller,
    bool CallerCanViewAllCustomers
);
