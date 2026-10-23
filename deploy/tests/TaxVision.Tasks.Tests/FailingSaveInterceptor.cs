using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaxVision.Tasks.Tests;

// Hace fallar el guardado del contexto real, como lo haría la base, sin tocar el código bajo prueba.
internal sealed class FailingSaveInterceptor(Exception failure) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromException<InterceptionResult<int>>(failure);
}
