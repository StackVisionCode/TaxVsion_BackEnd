using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Domain.Messages;

namespace TaxVision.Sms.Application.Messages;

/// <summary>Quién puede recibir un envío: política de contacto del cliente, asignación del remitente y STOP. La política
/// se resuelve antes de reservar el saldo, para que lo filtrado no se cobre; la asignación, una vez por lote.</summary>
public sealed class SmsSendGate
{
    private readonly HashSet<Guid> _smsDisabled;
    private HashSet<Guid>? _allowedCustomers;

    private SmsSendGate(HashSet<Guid> smsDisabled) => _smsDisabled = smsDisabled;

    public static async Task<SmsSendGate> ForContactPolicyAsync(
        SendSmsBatchCommand command,
        ICustomerContactPolicyRepository contactPolicies,
        CancellationToken ct
    )
    {
        if (!command.EnforceContactPolicy)
            return new SmsSendGate([]);

        var disabled = await contactPolicies.GetSmsDisabledCustomerIdsAsync(
            command.TenantId,
            command.Items.Select(i => i.CustomerId).Distinct().ToList(),
            ct
        );
        return new SmsSendGate(disabled.ToHashSet());
    }

    /// <summary>Visibilidad por asignación: un preparador solo le escribe a sus clientes. Sin restricción para un
    /// servicio M2M, con <c>customers.view_all</c> o con el flag apagado.</summary>
    public async Task LoadAssignmentsAsync(
        SendSmsBatchCommand command,
        SmsVisibilityOptions visibility,
        ISmsCustomerAssignmentReader assignments,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        if (assignedTo is { } sender)
            _allowedCustomers = (
                await assignments.GetAssignedCustomerIdsAsync(command.TenantId, sender, ct)
            ).ToHashSet();
    }

    public bool BlockedByContactPolicy(Guid customerId) => _smsDisabled.Contains(customerId);

    public bool IsAssigned(Guid customerId) => _allowedCustomers is null || _allowedCustomers.Contains(customerId);

    /// <summary>Unidades a reservar: todo lo que la política deja pasar. El resto se libera al liquidar.</summary>
    public int ChargeableCount(IReadOnlyList<SmsSendItemDto> items) =>
        items.Count(item => !BlockedByContactPolicy(item.CustomerId));

    /// <summary>Política de contacto del cliente, o STOP por (tenant, número): la fila queda Suppressed.</summary>
    public static async Task ApplySuppressionAsync(
        SmsMessage message,
        bool blockedByContactPolicy,
        ISmsOptOutRepository optOuts,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (blockedByContactPolicy)
            message.MarkSuppressed(
                "Customer turned off SMS in the contact policy.",
                nowUtc,
                SmsMessage.ContactPolicySuppressionCode
            );
        else if (await optOuts.IsPhoneOptedOutAsync(message.TenantId, message.To, ct))
            message.MarkSuppressed("Recipient opted out (STOP).", nowUtc);
    }
}
