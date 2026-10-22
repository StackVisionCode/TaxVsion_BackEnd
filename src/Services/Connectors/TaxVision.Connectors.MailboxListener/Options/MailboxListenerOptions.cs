namespace TaxVision.Connectors.MailboxListener.Options;

/// <summary>
/// Cómo escucha el listener. Apagado por defecto: la reconciliación cada 15 min sigue cubriendo IMAP,
/// así que prenderlo o apagarlo nunca rompe nada.
/// </summary>
public sealed class MailboxListenerOptions
{
    public const string SectionName = "Connectors:MailboxListener";

    public bool Enabled { get; set; }

    /// <summary>Cuentas que escucha esta instancia. Se ajusta con la prueba de carga.</summary>
    public int MaxAccountsPerInstance { get; set; } = 500;

    /// <summary>Cada IDLE se corta y se vuelve a pedir antes de que un NAT o balanceador cierre la conexión.</summary>
    public int IdleMinutes { get; set; } = 4;

    /// <summary>Servidores sin IDLE: cada cuánto se pregunta si cambió la bandeja.</summary>
    public int PollMinutes { get; set; } = 3;

    /// <summary>Espera tras un cambio para juntar una ráfaga de correos en un solo aviso.</summary>
    public int ChangeDebounceSeconds { get; set; } = 3;

    public int MaxConcurrentConnects { get; set; } = 10;

    /// <summary>Logins simultáneos por servidor IMAP: muchos juntos disparan "too many login attempts".</summary>
    public int MaxConcurrentConnectsPerHost { get; set; } = 3;

    public int ConnectDelayMilliseconds { get; set; } = 500;

    public int LeaseSeconds { get; set; } = 60;

    /// <summary>Cada cuánto se renuevan las cuentas tomadas y se buscan nuevas.</summary>
    public int TickSeconds { get; set; } = 15;

    public int MaxBackoffMinutes { get; set; } = 10;
}
