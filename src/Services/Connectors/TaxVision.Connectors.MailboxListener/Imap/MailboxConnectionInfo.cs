namespace TaxVision.Connectors.MailboxListener.Imap;

/// <summary>
/// Datos para conectarse a un buzón. La contraseña ya viene descifrada: vive solo lo que dura el login
/// y nunca se guarda en un campo de la sesión.
/// </summary>
public sealed record MailboxConnectionInfo(string Host, int Port, bool UseSsl, string Username, string Password)
{
    /// <summary>Para logs: nunca el usuario ni la contraseña.</summary>
    public override string ToString() => $"{Host}:{Port}";
}
