namespace TaxVision.Correspondence.Api.Requests;

/// <summary><c>POST /correspondence/delivery-notices/{id}/resend</c> — <c>Recipient</c> = dirección corregida; vacío = la misma del aviso.</summary>
public sealed record ResendDeliveryNoticeBody(string? Recipient);
