using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Sharing;

public enum ShareRecipientKind
{
    User,
    Customer,
    Email,
}

/// <summary>
/// Autorizacion adicional bajo un ShareLink con visibilidad SpecificUsers,
/// TenantCustomers (restringido) o ExternalRecipients. Solo se instancia via los
/// factory internos de <see cref="ShareLink"/> (AddUserRecipient/AddCustomerRecipient/
/// AddExternalRecipient) — cada uno llena exactamente un campo segun Kind.
/// </summary>
public sealed class ShareRecipient : BaseEntity
{
    private ShareRecipient() { }

    public Guid ShareLinkId { get; private set; }
    public ShareRecipientKind Kind { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public Guid? RecipientCustomerId { get; private set; }
    public string? RecipientEmail { get; private set; }

    internal static ShareRecipient ForUser(Guid shareLinkId, Guid userId) =>
        new()
        {
            ShareLinkId = shareLinkId,
            Kind = ShareRecipientKind.User,
            RecipientUserId = userId,
        };

    internal static ShareRecipient ForCustomer(Guid shareLinkId, Guid customerId) =>
        new()
        {
            ShareLinkId = shareLinkId,
            Kind = ShareRecipientKind.Customer,
            RecipientCustomerId = customerId,
        };

    internal static ShareRecipient ForEmail(Guid shareLinkId, string email) =>
        new()
        {
            ShareLinkId = shareLinkId,
            Kind = ShareRecipientKind.Email,
            RecipientEmail = email.Trim().ToLowerInvariant(),
        };
}
