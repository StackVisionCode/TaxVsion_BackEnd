namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>Rol de un destinatario en un correo (entrante o, más adelante, en un <c>Draft</c>).</summary>
public enum EmailRecipientType
{
    To = 0,
    Cc = 1,
    Bcc = 2,
}
