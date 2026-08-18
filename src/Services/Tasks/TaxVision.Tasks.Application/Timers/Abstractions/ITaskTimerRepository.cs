namespace TaxVision.Tasks.Application.Timers.Abstractions;

/// <summary>
/// Sólo el reporte. Abrir y cerrar un timer pasa siempre por el agregado, nunca por acá.
/// </summary>
public interface ITaskTimerRepository
{
    /// <param name="userId">Sin valor, el reporte cubre a toda la firma.</param>
    Task<IReadOnlyList<TaskTimerReportRow>> ListReportAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? userId,
        CancellationToken ct = default
    );
}

/// <param name="Hours">Suma de los tramos cerrados. Los que siguen corriendo no entran.</param>
public sealed record TaskTimerReportRow(
    Guid TaskId,
    string Title,
    Guid UserId,
    bool IsBillable,
    decimal Hours,
    int Entries
);
