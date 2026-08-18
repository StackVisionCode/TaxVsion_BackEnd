namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>
/// Puerto de observabilidad del motor de tareas. Es puerto y no clase estática porque los puntos de
/// medición viven en handlers de Application, que no puede depender de Infrastructure.
///
/// <para>Un método por hecho de negocio: nada de <c>Record(string metric, ...)</c>.</para>
/// </summary>
public interface ITaskMetrics
{
    /// <summary>Tag <c>has_customer</c>: distingue el trabajo de cliente del interno de la firma.</summary>
    void RecordCreated(bool hasCustomer);

    void RecordCompleted(bool hasCustomer);

    /// <summary>Cuántas nacen o quedan esperando a otra. Si sube mucho, el grafo está mal armado.</summary>
    void RecordBlocked();

    /// <summary>Un ciclo rechazado es un intento de armar un grafo imposible, no un error de sistema.</summary>
    void RecordDependencyCycleRejected();

    /// <summary>
    /// <b>El termómetro del motor.</b> Los contadores de bloqueadores y subtareas se llevan en la
    /// fila; si la reconciliación corrige alguno, es que un evento se perdió o llegó dos veces.
    /// Cero para siempre es la única lectura sana.
    /// </summary>
    void RecordReconciliationCorrections(int count);

    /// <summary>Desde que se creó hasta que se cerró. Mide el encargo, no el clic.</summary>
    void RecordTimeToCompleteSeconds(double seconds);

    /// <summary>Tareas que pasaron su vencimiento sin cerrarse.</summary>
    void RecordOverdue(int count);
}
