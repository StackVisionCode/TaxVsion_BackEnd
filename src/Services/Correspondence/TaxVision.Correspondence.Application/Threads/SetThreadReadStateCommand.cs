namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Marca TODOS los correos inbound de un hilo como leídos/no-leídos de una vez ("mark all as read"
/// / "all as unread"), estado compartido por el tenant. Un solo comando con <see cref="IsRead"/>,
/// mismo criterio que <see cref="Messages.SetMessageReadStateCommand"/>.
/// </summary>
public sealed record SetThreadReadStateCommand(Guid TenantId, Guid ThreadId, bool IsRead);
