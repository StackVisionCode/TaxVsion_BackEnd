namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Desconectar y purgar los buzones personales del empleado que se retira. Va a una cola local durable.</summary>
public sealed record PurgeOffboardedMailboxesCommand(Guid TenantId, Guid UserId, string CorrelationId);
