namespace TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;

public sealed record GetProviderStatusQuery(Guid TenantId);

/// <summary>El buzón de la oficina, tal como lo conectó en Connectors. Nunca expone secretos.</summary>
public sealed record ConnectedMailboxSummary(string EmailAddress, string ProviderCode);

/// <summary>
/// De dónde va a salir el correo de esta oficina, para una pantalla de configuración.
///
/// <para>Antes informaba sobre <c>TenantEmailProvider</c> — una tabla de SMTP por oficina que se
/// retiró porque era redundante con el buzón de Connectors y no se podía rellenar. Ahora responde la
/// pregunta que de verdad importa: <b>¿tiene la oficina un buzón conectado?</b> Si lo tiene, el
/// correo sale desde su dirección; si no, sale por el remitente del sistema en su nombre.</para>
/// </summary>
public sealed record ProviderStatusDto(
    bool HasSystemProvider,
    bool HasConnectedMailbox,
    ConnectedMailboxSummary? ConnectedMailbox
);
