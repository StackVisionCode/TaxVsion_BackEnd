using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

public static class FormActionRunViews
{
    // El cliente existente de un duplicado solo se muestra si el actor puede verlo.
    public static async Task<IReadOnlyList<FormActionRunResponse>> ForStaffAsync(
        FormRequest request,
        Guid actorUserId,
        bool canViewAll,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var runs = new List<FormActionRunResponse>();
        foreach (
            var run in request
                .ActionRuns.OrderByDescending(item => item.RequestedAtUtc)
                .ThenByDescending(item => item.Attempt)
        )
        {
            var visible =
                run.ExistingCustomerId is { } existing
                && await visibility.CanViewCustomerAsync(request.TenantId, actorUserId, canViewAll, existing, ct);
            runs.Add(FormActionRunResponse.From(run, visible));
        }
        return runs;
    }
}
