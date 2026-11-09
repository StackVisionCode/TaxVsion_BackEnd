namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Copia de un correo en un buzón (COR-03). El mismo mensaje puede llegar a varios buzones del tenant: se guarda
/// una vez y cada buzón deja aquí su copia, para que la visibilidad por buzón no dependa del primero que llegó.
/// </summary>
public sealed class IncomingEmailMailbox
{
    private IncomingEmailMailbox() { }

    public Guid Id { get; private set; }
    public Guid IncomingEmailId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderMessageId { get; private set; } = default!;

    internal static IncomingEmailMailbox Create(
        Guid tenantId,
        Guid incomingEmailId,
        Guid accountId,
        string providerMessageId
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IncomingEmailId = incomingEmailId,
            AccountId = accountId,
            ProviderMessageId = providerMessageId,
        };
}
