using System.Reflection;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Auth.Tests;

/// <summary>
/// Outbox de EF en memoria con la misma promesa que el de Wolverine: lo publicado queda pendiente y solo sale
/// (a <see cref="Flushed"/>) si el <c>SaveChanges</c> confirma; confirma la transacción abierta, si hay una. Lo
/// que no usa el código bajo prueba, lanza.
/// </summary>
public class RecordingAuthDbContextOutbox : DispatchProxy
{
    private readonly List<object> _pending = [];
    private AuthDbContext? _context;
    private List<object> _flushed = [];

    internal static IDbContextOutbox<AuthDbContext> Create(AuthDbContext context, List<object> flushed)
    {
        var outbox = Create<IDbContextOutbox<AuthDbContext>, RecordingAuthDbContextOutbox>();
        var recorder = (RecordingAuthDbContextOutbox)(object)outbox;
        recorder._context = context;
        recorder._flushed = flushed;
        return outbox;
    }

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
        await _context!.SaveChangesAsync(ct);
        if (_context.Database.CurrentTransaction is { } transaction)
            await transaction.CommitAsync(ct);
        _flushed.AddRange(_pending);
        _pending.Clear();
    }
}
