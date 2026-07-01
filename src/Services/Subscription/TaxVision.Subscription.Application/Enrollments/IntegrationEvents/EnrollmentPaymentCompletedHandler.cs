using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;

namespace TaxVision.Subscription.Application.Enrollments.IntegrationEvents;

public static class EnrollmentPaymentCompletedHandler
{
    public static async Task Handle(
        EnrollmentPaymentCompletedIntegrationEvent evt,
        IEnrollmentRepository enrollmentRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var enrollment = await enrollmentRepo.GetByIdAsync(evt.EnrollmentId, ct);
        if (enrollment is null) return;

        var confirmResult = enrollment.Confirm();
        if (confirmResult.IsFailure) return; // ya confirmado — idempotente

        enrollment.MarkProvisioning();

        // Pedir al Tenant Service que cree el tenant
        await bus.PublishAsync(new TenantProvisioningRequestedIntegrationEvent
        {
            TenantId = Guid.Empty,
            EnrollmentId = enrollment.Id,
            PlanCode = enrollment.PlanCode,
            AdminEmail = enrollment.AdminEmail,
            OrgName = enrollment.OrgName,
            Subdomain = enrollment.Subdomain,
            TimeZoneId = enrollment.TimeZoneId,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}
