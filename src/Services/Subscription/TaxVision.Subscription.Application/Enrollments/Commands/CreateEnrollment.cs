using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Enrollments.IntegrationEvents;
using TaxVision.Subscription.Domain.Enrollments;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Enrollments.Commands;

public sealed record CreateEnrollmentCommand(
    string PlanCode,
    BillingPeriod BillingPeriod,
    string AdminEmail,
    string OrgName,
    string Subdomain,
    string TimeZoneId);

public sealed record CreateEnrollmentResponse(
    Guid EnrollmentId,
    string PlanCode,
    string PlanName,
    decimal TotalAmount,
    string Currency,
    string Status);

public static class CreateEnrollmentHandler
{
    public static async Task<Result<CreateEnrollmentResponse>> Handle(
        CreateEnrollmentCommand cmd,
        IPlanRepository planRepo,
        IEnrollmentRepository enrollmentRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var plan = await planRepo.GetByCodeAsync(cmd.PlanCode, ct);
        if (plan is null)
            return Result.Failure<CreateEnrollmentResponse>(
                new Error("Plan.NotFound", $"Plan '{cmd.PlanCode}' not found."));

        if (!plan.IsActive)
            return Result.Failure<CreateEnrollmentResponse>(
                new Error("Plan.Inactive", $"Plan '{cmd.PlanCode}' is not available."));

        // PlanId en lugar de PlanVersionId — precio se lee del plan vigente
        var enrollResult = SubscriptionEnrollment.Create(
            cmd.PlanCode, plan.Id, cmd.BillingPeriod,
            cmd.AdminEmail, cmd.OrgName, cmd.Subdomain, cmd.TimeZoneId);

        if (enrollResult.IsFailure)
            return Result.Failure<CreateEnrollmentResponse>(enrollResult.Error);

        var enrollment = enrollResult.Value;
        await enrollmentRepo.AddAsync(enrollment, ct);

        var price = plan.GetBasePrice(cmd.BillingPeriod);

        await bus.PublishAsync(new EnrollmentPaymentRequestedIntegrationEvent
        {
            TenantId = Guid.Empty,
            EnrollmentId = enrollment.Id,
            PlanCode = cmd.PlanCode,
            PlanVersionId = plan.Id,   // campo renombrado en el evento como PlanId
            AdminEmail = cmd.AdminEmail,
            OrgName = cmd.OrgName,
            Amount = price,
            Currency = plan.Currency,
            BillingPeriod = cmd.BillingPeriod,
            ExpiresAtUtc = enrollment.ExpiresAtUtc,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);

        return Result.Success(new CreateEnrollmentResponse(
            enrollment.Id, plan.Code, plan.Name,
            price, plan.Currency, enrollment.Status.ToString()));
    }
}
