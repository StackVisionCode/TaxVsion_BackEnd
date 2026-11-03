namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// Por qué no se entregó (o se demora) un correo, en categorías estables para el usuario. La decide
/// <see cref="ValueObjects.DeliveryFailureClassification"/>.
/// </summary>
public enum DeliveryFailureCategory
{
    InvalidAddress,
    DomainNotFound,
    MailboxFull,
    Rejected,
    TooLarge,
    Delayed,
    Other,
}
