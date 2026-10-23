using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Foto de los runs antes de planificar. Se mide después de guardar y solo lo que nació o cambió de
/// estado, para que una transacción que no confirma no cuente nada.
/// </summary>
public sealed class FormActionRunTally
{
    private readonly Dictionary<Guid, FormActionRunStatus> _before;

    private FormActionRunTally(Dictionary<Guid, FormActionRunStatus> before) => _before = before;

    public static FormActionRunTally Of(FormRequest request) =>
        new(request.ActionRuns.ToDictionary(run => run.Id, run => run.Status));

    public void Record(FormRequest request, IClientFormsMetrics metrics)
    {
        foreach (var run in request.ActionRuns)
        {
            if (!_before.TryGetValue(run.Id, out var status) || status != run.Status)
                metrics.RecordActionRun(run.ActionType, run.Status);
        }
    }
}
