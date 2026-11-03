namespace TaxVision.Correspondence.Domain.Delivery;

/// <summary>
/// Códigos estables del motivo para la UI (contrato con el CRM): uno por categoría de Postmaster, en
/// snake_case. Una categoría desconocida cae en <see cref="Other"/>; sin categoría, Delayed es
/// <see cref="Delayed"/> y el resto <see cref="Other"/>.
/// </summary>
public static class DeliveryReasonCodes
{
    public const string InvalidAddress = "invalid_address";
    public const string DomainNotFound = "domain_not_found";
    public const string MailboxFull = "mailbox_full";
    public const string Rejected = "rejected";
    public const string TooLarge = "too_large";
    public const string Delayed = "delayed";
    public const string Other = "other";

    public static string From(DeliveryOutcome outcome, string? category) =>
        category switch
        {
            "InvalidAddress" => InvalidAddress,
            "DomainNotFound" => DomainNotFound,
            "MailboxFull" => MailboxFull,
            "Rejected" => Rejected,
            "TooLarge" => TooLarge,
            "Delayed" => Delayed,
            null when outcome == DeliveryOutcome.Delayed => Delayed,
            _ => Other,
        };
}
