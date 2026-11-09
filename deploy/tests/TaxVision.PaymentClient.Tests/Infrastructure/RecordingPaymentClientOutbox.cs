using System.Reflection;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Lo que confirmaron los outbox de prueba: mensajes publicados y número de unidades confirmadas.</summary>
public sealed class OutboxRecording
{
    public List<object> Flushed { get; } = [];
    public int Commits { get; set; }
}

/// <summary>
/// Outbox de EF con la misma promesa que el de Wolverine: lo publicado queda pendiente y solo sale si el
/// <c>SaveChanges</c> confirma. Confirma la transacción abierta de la unidad (si no, contra SQL real lo guardado se
/// revierte al cerrar el scope). Lo demás lanza.
/// </summary>
public class RecordingPaymentClientOutbox : DispatchProxy
{
    private PaymentClientDbContext _context = null!;
    private OutboxRecording _recording = null!;
    private readonly List<object> _pending = [];

    /// <summary>Base en memoria compartida con las unidades (misma raíz); la transacción de la unidad se ignora.</summary>
    internal static PaymentClientDbContext CreateDb(string databaseName, InMemoryDatabaseRoot root) =>
        new(
            (DbContextOptions<PaymentClientDbContext>)
                Options(new DbContextOptionsBuilder<PaymentClientDbContext>(), databaseName, root).Options,
            new NoTenantContext()
        );

    /// <summary>Scopes de unidad sobre esa base: DbContext, repositorios y este outbox.</summary>
    internal static IServiceScopeFactory InMemoryUnits(
        string databaseName,
        InMemoryDatabaseRoot root,
        OutboxRecording recording
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, NoTenantContext>();
        services.AddDbContext<PaymentClientDbContext>(options => Options(options, databaseName, root));
        services.AddScoped<
            TaxVision.PaymentClient.Application.Abstractions.ITenantPaymentConfigRepository,
            TaxVision.PaymentClient.Infrastructure.Persistence.Repositories.TenantPaymentConfigRepository
        >();
        services.AddScoped(sp =>
        {
            var outbox = Create<IDbContextOutbox<PaymentClientDbContext>, RecordingPaymentClientOutbox>();
            var recorder = (RecordingPaymentClientOutbox)(object)outbox;
            recorder._context = sp.GetRequiredService<PaymentClientDbContext>();
            recorder._recording = recording;
            return outbox;
        });
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static DbContextOptionsBuilder Options(
        DbContextOptionsBuilder options,
        string databaseName,
        InMemoryDatabaseRoot root
    ) =>
        options
            .UseInMemoryDatabase(databaseName, root)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_DbContext" => _context,
            // El tenant que estampa el job no cambia lo que se registra.
            "set_TenantId" or "get_TenantId" => null,
            "PublishAsync" => Enqueue(args![0]!),
            "SaveChangesAndFlushMessagesAsync" => SaveAndFlushAsync(args!.OfType<CancellationToken>().FirstOrDefault()),
            _ => throw new NotSupportedException($"El doble de outbox no implementa {targetMethod?.Name}."),
        };

    private ValueTask Enqueue(object message)
    {
        _pending.Add(message);
        return ValueTask.CompletedTask;
    }

    private async Task SaveAndFlushAsync(CancellationToken ct)
    {
        await _context.SaveChangesAsync(ct);
        if (_context.Database.CurrentTransaction is { } transaction)
            await transaction.CommitAsync(ct);

        _recording.Flushed.AddRange(_pending);
        _recording.Commits++;
        _pending.Clear();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
