using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Clientes asignados al remitente. Anota si se consultó: cuando el alcance no aplica, no debe.</summary>
public sealed class FakeSmsCustomerAssignmentReader : ISmsCustomerAssignmentReader
{
    public List<Guid> Assigned { get; } = [];
    public bool WasAsked { get; private set; }

    public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        WasAsked = true;
        return Task.FromResult<IReadOnlyCollection<Guid>>(Assigned);
    }
}
