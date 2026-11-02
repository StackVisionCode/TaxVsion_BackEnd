using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// El tail de la cadena de auditoría incluye lo agregado en la misma unidad de trabajo: un handler que
/// registra dos eventos antes de guardar (p. ej. la copia parcial + su entrega) encadena 16 → 17 en vez
/// de repetir la 16 y chocar con el índice único (la copia nunca salía).
/// </summary>
public sealed class SignatureAuditRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly string Hash1 = new('a', 64);
    private static readonly string Hash2 = new('b', 64);

    [Fact]
    public async Task The_tail_includes_events_added_in_the_same_unit_of_work()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(databaseName))
        {
            seed.SignatureAuditEvents.Add(Event(1, SignatureAuditEvent.GenesisMarker, Hash1));
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new SignatureAuditRepository(db);
        Assert.Equal(1, (await repository.GetTailAsync(TenantId, RequestId))!.LastSequence);

        await repository.AddAsync(Event(2, Hash1, Hash2));
        var tail = await repository.GetTailAsync(TenantId, RequestId);

        Assert.Equal(2, tail!.LastSequence);
        Assert.Equal(Hash2, tail.LastChainHash);
    }

    [Fact]
    public async Task Pending_events_of_another_request_do_not_move_the_tail()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var repository = new SignatureAuditRepository(db);

        await repository.AddAsync(Event(1, SignatureAuditEvent.GenesisMarker, Hash1, requestId: Guid.NewGuid()));

        Assert.Null(await repository.GetTailAsync(TenantId, RequestId));
    }

    private static SignatureAuditEvent Event(long sequence, string previous, string hash, Guid? requestId = null) =>
        SignatureAuditEvent
            .Create(
                TenantId,
                requestId ?? RequestId,
                sequence,
                SignatureAuditEventKind.RequestCreated,
                DateTime.UtcNow,
                "{}",
                previous,
                hash
            )
            .Value;

    private static SignatureDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>().UseInMemoryDatabase(databaseName).Options,
            new StubTenantContext(TenantId)
        );

    private sealed class StubTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
