using TaxVision.Notes.Application.Notes.Abstractions;
using Wolverine.Attributes;

namespace TaxVision.Notes.Application.Notes.Commands;

/// <summary>Reposición de plataforma: una corrida por oficina. Idempotente.</summary>
public sealed record RedeclareFileSourcesCommand(Guid TenantId, bool DryRun);

/// <summary>Sin transacción envolvente: cada página publica y confirma en su propia transacción con outbox.</summary>
public static class RedeclareFileSourcesHandler
{
    [NonTransactional]
    public static Task<RedeclareFileSourcesReport> Handle(
        RedeclareFileSourcesCommand command,
        INoteFileSourceRedeclarer redeclarer,
        CancellationToken ct
    ) => redeclarer.RedeclareAsync(command.TenantId, command.DryRun, ct);
}
