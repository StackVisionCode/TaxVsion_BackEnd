using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Enrollments;

public sealed class SubscriptionEnrollment : BaseEntity
{
    // No hereda TenantEntity — el tenant aún no existe al momento del enrollment
    public string PlanCode { get; private set; } = default!;
    public Guid PlanId { get; private set; }
    public BillingPeriod BillingPeriod { get; private set; }
    public string AdminEmail { get; private set; } = default!;
    public string OrgName { get; private set; } = default!;
    public string Subdomain { get; private set; } = default!;
    public string TimeZoneId { get; private set; } = default!;
    public EnrollmentStatus Status { get; private set; }
    public Guid? ProvisionedTenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private SubscriptionEnrollment() { }

    public static Result<SubscriptionEnrollment> Create(
        string planCode,
        Guid planId,
        BillingPeriod billingPeriod,
        string adminEmail,
        string orgName,
        string subdomain,
        string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
            return Result.Failure<SubscriptionEnrollment>(
                new Error("Enrollment.Email", "Admin email is required."));
        if (string.IsNullOrWhiteSpace(subdomain))
            return Result.Failure<SubscriptionEnrollment>(
                new Error("Enrollment.Subdomain", "Subdomain is required."));
        if (string.IsNullOrWhiteSpace(orgName))
            return Result.Failure<SubscriptionEnrollment>(
                new Error("Enrollment.OrgName", "Organization name is required."));
        if (planId == Guid.Empty)
            return Result.Failure<SubscriptionEnrollment>(
                new Error("Enrollment.Plan", "A valid plan is required."));

        return Result.Success(new SubscriptionEnrollment
        {
            Id = Guid.NewGuid(),
            PlanCode = planCode,
            PlanId = planId,
            BillingPeriod = billingPeriod,
            AdminEmail = adminEmail.Trim().ToLowerInvariant(),
            OrgName = orgName.Trim(),
            Subdomain = subdomain.Trim().ToLowerInvariant(),
            TimeZoneId = timeZoneId,
            Status = EnrollmentStatus.PendingPayment,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        });
    }

    public Result Confirm()
    {
        if (Status != EnrollmentStatus.PendingPayment)
            return Result.Failure(new Error("Enrollment.InvalidStatus",
                $"Cannot confirm enrollment in status {Status}."));
        Status = EnrollmentStatus.PaymentConfirmed;
        return Result.Success();
    }

    public Result MarkProvisioning()
    {
        if (Status != EnrollmentStatus.PaymentConfirmed)
            return Result.Failure(new Error("Enrollment.InvalidStatus",
                $"Cannot start provisioning from status {Status}."));
        Status = EnrollmentStatus.Provisioning;
        return Result.Success();
    }

    public Result AssignTenant(Guid tenantId)
    {
        if (Status != EnrollmentStatus.Provisioning)
            return Result.Failure(new Error("Enrollment.InvalidStatus",
                $"Cannot assign tenant to enrollment in status {Status}."));
        ProvisionedTenantId = tenantId;
        Status = EnrollmentStatus.Provisioned;
        return Result.Success();
    }

    public void MarkExpired() => Status = EnrollmentStatus.Expired;
    public void MarkFailed() => Status = EnrollmentStatus.Failed;
}
