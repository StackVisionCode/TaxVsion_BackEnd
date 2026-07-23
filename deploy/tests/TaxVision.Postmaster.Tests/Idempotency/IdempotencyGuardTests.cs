using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Domain.Idempotency;
using TaxVision.Postmaster.Infrastructure.Idempotency;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Tests.Idempotency;

public sealed class IdempotencyGuardTests
{
    // EmailIdempotency no implementa ITenantOwned (ver PostmasterDbContext) — el filtro
    // global de RBAC Fase 5 no lo alcanza, así que un tenant vacío acá es inofensivo.
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static PostmasterDbContext CreateContext(string? databaseName = null) =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options,
            new NoTenantContext()
        );

    private static SqlIdempotencyGuard CreateGuard(PostmasterDbContext dbContext) =>
        new(dbContext, NullLogger<SqlIdempotencyGuard>.Instance);

    [Fact]
    public async Task TryReserveAsync_inserts_new_reservation_and_returns_Reserved_for_a_fresh_key()
    {
        await using var db = CreateContext();
        var guard = CreateGuard(db);
        var tenantId = Guid.NewGuid();

        var result = await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);

        Assert.Equal(IdempotencyReservationOutcome.Reserved, result.Outcome);
        Assert.Null(result.ExistingSentMessageId);
        var stored = await db.EmailIdempotencies.SingleAsync();
        Assert.Equal(tenantId, stored.TenantId);
        Assert.Null(stored.CompletedAtUtc);
    }

    [Fact]
    public async Task TryReserveAsync_returns_InProgress_when_reservation_is_in_progress_within_retry_window()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var guard = CreateGuard(db);
        await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);

        var result = await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);

        Assert.Equal(IdempotencyReservationOutcome.InProgress, result.Outcome);
        Assert.Null(result.ExistingSentMessageId);
        Assert.Equal(1, await db.EmailIdempotencies.CountAsync());
    }

    [Fact]
    public async Task TryReserveAsync_returns_AlreadyCompleted_with_sent_message_id_for_a_completed_reservation()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var sentMessageId = Guid.NewGuid();
        var reservation = EmailIdempotency.Reserve(tenantId, "key-1", DateTime.UtcNow, TimeSpan.FromDays(7)).Value;
        reservation.Complete(sentMessageId, DateTime.UtcNow);
        await db.EmailIdempotencies.AddAsync(reservation);
        await db.SaveChangesAsync();

        var guard = CreateGuard(db);
        var result = await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);

        Assert.Equal(IdempotencyReservationOutcome.AlreadyCompleted, result.Outcome);
        Assert.Equal(sentMessageId, result.ExistingSentMessageId);
    }

    [Fact]
    public async Task TryReserveAsync_replaces_an_abandoned_reservation_outside_the_retry_window()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var staleReservation = EmailIdempotency
            .Reserve(tenantId, "key-1", DateTime.UtcNow.AddSeconds(-60), TimeSpan.FromDays(7))
            .Value;
        await db.EmailIdempotencies.AddAsync(staleReservation);
        await db.SaveChangesAsync();

        var guard = CreateGuard(db);
        var result = await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);

        Assert.Equal(IdempotencyReservationOutcome.Reserved, result.Outcome);
        var stored = await db.EmailIdempotencies.SingleAsync();
        Assert.True(DateTime.UtcNow - stored.CreatedAtUtc < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CompleteAsync_marks_reservation_as_completed()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var guard = CreateGuard(db);
        await guard.TryReserveAsync(tenantId, "key-1", CancellationToken.None);
        var sentMessageId = Guid.NewGuid();

        await guard.CompleteAsync(tenantId, "key-1", sentMessageId, CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.EmailIdempotencies.SingleAsync();
        Assert.Equal(sentMessageId, stored.SentMessageId);
        Assert.NotNull(stored.CompletedAtUtc);
    }

    /// <summary>
    /// El test de concurrencia real del plan §Fase 11: dos reservas para la MISMA clave, disparadas al
    /// mismo tiempo desde dos <see cref="PostmasterDbContext"/> separados contra la misma base
    /// InMemory (simula dos instancias del servicio, o dos entregas at-least-once del mismo evento) —
    /// antes del fix ambas hubieran recibido <c>null</c> y ambos callers habrían procedido a crear un
    /// <c>SentMessage</c>. La invariante real que importa (plan §Fase 11): nunca más de una reserva
    /// termina persistida para la misma clave, sin importar el timing exacto.
    /// <para>
    /// Nota sobre el proveedor InMemory: a diferencia de SQL Server real (que siempre lanza
    /// <c>SqlException</c> 2601/2627 ante una violación de PK — la traducción ya existente en
    /// <see cref="PostmasterDbContext.SaveChangesAsync"/>), el proveedor InMemory de EF Core no
    /// garantiza envolver una colisión de PK compuesta ocurrida en el instante exacto en un
    /// <c>DbUpdateException</c> traducible — puede burbujear una excepción interna cruda del storage.
    /// Por eso este test tolera esa excepción cruda en el perdedor (documentando por qué, no
    /// ignorándolo) mientras verifica la invariante real de negocio: como máximo una reserva
    /// persistida. La traducción real de <c>ConflictException</c> → <see cref="IdempotencyReservationOutcome.InProgress"/>
    /// está probada de forma determinística en <see cref="TryReserveAsync_returns_InProgress_when_reservation_is_in_progress_within_retry_window"/>
    /// y, del lado de los callers, en los tests de <c>ThrowConflictOnSaveChangesCall</c> de
    /// <c>NotificationsEmailSendRequestedConsumerTests</c>/<c>SendCorrespondenceMessageHandlerTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TryReserveAsync_concurrent_calls_for_the_same_key_never_persist_more_than_one_reservation()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        const string idempotencyKey = "concurrent-key";

        await using var dbA = CreateContext(databaseName);
        await using var dbB = CreateContext(databaseName);
        var guardA = CreateGuard(dbA);
        var guardB = CreateGuard(dbB);

        var barrier = new Barrier(2);
        Task<IdempotencyReservationResult?> RunAsync(SqlIdempotencyGuard guard) =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                try
                {
                    return await guard.TryReserveAsync(tenantId, idempotencyKey, CancellationToken.None);
                }
                catch
                {
                    // Ver nota de clase: el storage InMemory puede burbujear una excepción cruda para
                    // la colisión de PK en el instante exacto en vez de la traducción limpia que SQL
                    // Server sí garantiza. Se trata como "perdió la carrera" para esta aserción — la
                    // invariante de negocio (nunca 2 reservas) es lo que este test protege.
                    return null;
                }
            });

        var resultA = RunAsync(guardA);
        var resultB = RunAsync(guardB);
        var results = await Task.WhenAll(resultA, resultB);

        Assert.Single(results, r => r?.Outcome == IdempotencyReservationOutcome.Reserved);
        Assert.All(
            results,
            r =>
                Assert.True(
                    r
                        is null
                            or {
                                Outcome: IdempotencyReservationOutcome.Reserved
                                    or IdempotencyReservationOutcome.InProgress
                            }
                )
        );

        await using var verifyDb = CreateContext(databaseName);
        Assert.Equal(1, await verifyDb.EmailIdempotencies.CountAsync());
    }
}
