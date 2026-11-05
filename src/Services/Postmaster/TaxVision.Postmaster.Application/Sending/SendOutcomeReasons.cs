namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Motivos de fallo cuando no se sabe si el correo salió. Nunca se reenvía en esos casos.</summary>
public static class SendOutcomeReasons
{
    /// <summary>Se llamó al proveedor y no quedó registrado el resultado (caída, fallo al confirmar).</summary>
    public const string SendOutcomeUnknown = "send.outcomeUnknown";

    /// <summary>Connectors no contestó a tiempo.</summary>
    public const string MailboxOutcomeUnknown = "mailbox.outcomeUnknown";
}
