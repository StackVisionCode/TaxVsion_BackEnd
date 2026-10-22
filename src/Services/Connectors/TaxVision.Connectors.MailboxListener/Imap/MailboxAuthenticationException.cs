namespace TaxVision.Connectors.MailboxListener.Imap;

/// <summary>El servidor rechazó el usuario o la contraseña: reintentar no lo arregla.</summary>
public sealed class MailboxAuthenticationException(Exception innerException)
    : Exception("The IMAP server rejected the credentials.", innerException);
