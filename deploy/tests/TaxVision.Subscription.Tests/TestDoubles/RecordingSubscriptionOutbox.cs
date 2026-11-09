using System.Reflection;
using BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Lo que confirmaron los outbox de prueba: mensajes publicados y eventos de dominio raspados.</summary>
public sealed class OutboxRecording
{
    public List<object> Flushed { get; } = [];
    public List<IDomainEvent> DomainEvents { get; } = [];
    public int Commits { get; set; }
}

/// <summary>
/// Outbox de EF con la misma promesa que el de Wolverine: lo publicado queda pendiente y solo sale si el
/// <c>SaveChanges</c> confirma. Saca los eventos de dominio de los agregados rastreados y confirma la transacción
/// abierta de la unidad (si no, contra SQL real lo guardado se revierte al cerrar el scope). Lo demás lanza.
/// </summary>
public class RecordingSubscriptionOutbox : DispatchProxy
{
    private SubscriptionDbContext _context = null!;
    private OutboxRecording _recording = null!;
    private readonly List<object> _pending = [];

    /// <summary>Un outbox por scope sobre el DbContext del scope; todos confirman en <paramref name="recording"/>.</summary>
    internal static IServiceCollection AddTo(IServiceCollection services, OutboxRecording recording) =>
        services.AddScoped(sp =>
        {
            var outbox = Create<IDbContextOutbox<SubscriptionDbContext>, RecordingSubscriptionOutbox>();
            var recorder = (RecordingSubscriptionOutbox)(object)outbox;
            recorder._context = sp.GetRequiredService<SubscriptionDbContext>();
            recorder._recording = recording;
            return outbox;
        });

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_DbContext" => _context,
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
        var raised = _context.ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.DomainEvents).ToList();
        await _context.SaveChangesAsync(ct);
        if (_context.Database.CurrentTransaction is { } transaction)
            await transaction.CommitAsync(ct);

        _recording.DomainEvents.AddRange(raised);
        _recording.Flushed.AddRange(_pending);
        _recording.Commits++;
        _pending.Clear();
    }
}
