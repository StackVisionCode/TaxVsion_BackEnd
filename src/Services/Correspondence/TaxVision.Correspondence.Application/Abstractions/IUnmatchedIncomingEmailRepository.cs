using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Escrita por <see cref="Ingest.RawMessageReceivedConsumer"/> (Fase 4). Solo-escritura por ahora — nada la lee todavía (el job de purga/consulta de debug es futuro).</summary>
public interface IUnmatchedIncomingEmailRepository
{
    Task AddAsync(UnmatchedIncomingEmail entity, CancellationToken ct = default);
}
