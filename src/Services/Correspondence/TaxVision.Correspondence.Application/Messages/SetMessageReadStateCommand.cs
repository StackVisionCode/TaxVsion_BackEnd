namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Fija el estado leído/no-leído de UN correo inbound (compartido por el tenant). Un solo comando
/// con <see cref="IsRead"/> en vez de dos (read/unread): la responsabilidad es la misma, los dos
/// endpoints HTTP solo cambian el booleano.
/// </summary>
public sealed record SetMessageReadStateCommand(Guid TenantId, Guid IncomingEmailId, bool IsRead);
