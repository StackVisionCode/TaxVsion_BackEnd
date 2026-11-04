using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Único lugar donde un código RFC 3463 se vuelve motivo para el usuario, y donde "buzón lleno" deja de
/// ser rebote duro. Vale para todos los orígenes (DSN del buzón, webhook y consulta de SMTP2GO).
/// </summary>
public static class DeliveryFailureClassification
{
    /// <summary>
    /// Buzón lleno (x.2.2) es temporal aunque el servidor diga 5.2.2 (Outlook lo hace): rebote blando,
    /// sin supresión, para poder reenviar más tarde (D-5).
    /// </summary>
    public static DeliveryOutcome Normalize(DeliveryOutcome outcome, EnhancedStatusCode? code) =>
        outcome == DeliveryOutcome.HardBounced && IsMailboxFull(code) ? DeliveryOutcome.SoftBounced : outcome;

    /// <summary>Null cuando no hay nada que explicar: entregado, aceptado o queja (sí llegó).</summary>
    public static DeliveryFailureCategory? Classify(DeliveryOutcome outcome, EnhancedStatusCode? code) =>
        outcome switch
        {
            DeliveryOutcome.Deferred => IsMailboxFull(code)
                ? DeliveryFailureCategory.MailboxFull
                : DeliveryFailureCategory.Delayed,
            DeliveryOutcome.SoftBounced or DeliveryOutcome.HardBounced => OfFailure(code),
            // Rechazo del propio proveedor (SMTP2GO "reject") sin código: es un rechazo.
            DeliveryOutcome.Rejected => code is null ? DeliveryFailureCategory.Rejected : OfFailure(code),
            _ => null,
        };

    private static bool IsMailboxFull(EnhancedStatusCode? code) => code is { Subject: 2, Detail: 2 };

    private static DeliveryFailureCategory OfFailure(EnhancedStatusCode? code)
    {
        if (code is null)
            return DeliveryFailureCategory.Other;
        if (IsMailboxFull(code))
            return DeliveryFailureCategory.MailboxFull;
        // Falló con 4.x: el servidor se rindió tras reintentar. No es "sigue intentando" (Delayed) ni
        // prueba que la dirección esté mal.
        if (!code.IsPermanent)
            return DeliveryFailureCategory.Other;

        return (code.Subject, code.Detail) switch
        {
            // 5.1.0 es genérico ("otro problema de dirección"), pero en la práctica es destinatario
            // desconocido. 5.1.10 es el "RecipientNotFound" de Exchange Online.
            (1, 0 or 1 or 3 or 6 or 10) => DeliveryFailureCategory.InvalidAddress,
            // 5.4.310 = "el dominio no existe" de Exchange. 5.4.3 no entra: es un DNS caído, no
            // prueba que el dominio no exista.
            (1, 2) or (4, 4) or (4, 310) => DeliveryFailureCategory.DomainNotFound,
            (2, 3) or (3, 4) => DeliveryFailureCategory.TooLarge,
            (7, _) => DeliveryFailureCategory.Rejected,
            _ => DeliveryFailureCategory.Other,
        };
    }
}
