using System.Reflection;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tenant.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tenant.Tests.TestSupport;

/// <summary>Lo que confirmaron los outbox de prueba. <see cref="ConflictOnFlush"/>: en qué confirmación chocar.</summary>
public sealed class OutboxRecording
{
    public List<object> Flushed { get; } = [];
    public int Flushes { get; set; }
    public HashSet<int> ConflictOnFlush { get; } = [];
}

/// <summary>Outbox de EF con la promesa del de Wolverine: lo publicado solo sale si la unidad confirma.</summary>
public class RecordingTenantOutbox : DispatchProxy
{
    private TenantDbContext _context = null!;
    private OutboxRecording _recording = null!;
    private readonly List<object> _pending = [];

    /// <summary>Un outbox por scope sobre el DbContext del scope; todos confirman en <paramref name="recording"/>.</summary>
    internal static IServiceCollection AddTo(IServiceCollection services, OutboxRecording recording) =>
        services.AddScoped(sp =>
        {
            var outbox = Create<IDbContextOutbox<TenantDbContext>, RecordingTenantOutbox>();
            var recorder = (RecordingTenantOutbox)(object)outbox;
            recorder._context = sp.GetRequiredService<TenantDbContext>();
            recorder._recording = recording;
            return outbox;
        });

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_DbContext" => _context,
            "PublishAsync" => Enqueue(args![0]!),
            "SaveChangesAndFlushMessagesAsync" => Flush(),
            _ => throw new NotSupportedException($"El doble de outbox no implementa {targetMethod?.Name}."),
        };

    private ValueTask Enqueue(object message)
    {
        _pending.Add(message);
        return ValueTask.CompletedTask;
    }

    private Task Flush()
    {
        _recording.Flushes++;
        if (_recording.ConflictOnFlush.Contains(_recording.Flushes))
            return Task.FromException(
                new ConflictException("Persistence.ConcurrencyConflict", "The record was changed by someone else.")
            );

        _recording.Flushed.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }
}
