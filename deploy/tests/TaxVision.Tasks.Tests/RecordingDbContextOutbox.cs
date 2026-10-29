using System.Reflection;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Tests;

/// <summary>
/// Outbox de EF en memoria con la misma promesa que el de Wolverine: lo publicado queda pendiente y solo
/// sale si el <c>SaveChanges</c> del contexto confirma. Lo que no usa el código bajo prueba, lanza.
/// </summary>
public class RecordingDbContextOutbox : DispatchProxy
{
    internal TasksDbContext? Context { get; private set; }
    internal List<object> Pending { get; } = [];
    internal List<object> Flushed { get; } = [];
    internal int Saves { get; private set; }

    internal static (IDbContextOutbox<TasksDbContext> Outbox, RecordingDbContextOutbox Recorder) Create(
        TasksDbContext? context = null
    )
    {
        var outbox = Create<IDbContextOutbox<TasksDbContext>, RecordingDbContextOutbox>();
        var recorder = (RecordingDbContextOutbox)(object)outbox;
        recorder.Context = context;
        return (outbox, recorder);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_DbContext" => Context,
            "PublishAsync" => Enqueue(args![0]!),
            "SaveChangesAndFlushMessagesAsync" => SaveAndFlushAsync(args!.OfType<CancellationToken>().FirstOrDefault()),
            _ => throw new NotSupportedException($"El doble de outbox no implementa {targetMethod?.Name}."),
        };

    private ValueTask Enqueue(object message)
    {
        Pending.Add(message);
        return ValueTask.CompletedTask;
    }

    private async Task SaveAndFlushAsync(CancellationToken ct)
    {
        if (Context is not null)
            await Context.SaveChangesAsync(ct);
        Saves++;
        Flushed.AddRange(Pending);
        Pending.Clear();
    }
}
