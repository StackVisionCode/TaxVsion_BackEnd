using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Admin.Queries;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Admin.Commands;

public sealed record SetOnboardingPaymentMethodAvailabilityCommand(
    PaymentProviderCode Provider,
    PaymentMethodKind Method,
    bool Enabled,
    string? DisabledReason,
    Guid ActorUserId
);

public static class SetOnboardingPaymentMethodAvailabilityHandler
{
    public static async Task<Result<OnboardingPaymentMethodAdminResponse>> Handle(
        SetOnboardingPaymentMethodAvailabilityCommand command,
        IOnboardingPaymentMethodCatalog catalog,
        IOnboardingPaymentMethodOverrideRepository overrides,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.ActorUserId == Guid.Empty)
            return Result.Failure<OnboardingPaymentMethodAdminResponse>(
                new Error("PaymentMethodOverride.ActorRequired", "Actor user id is required.")
            );

        var currentOptions = await catalog.GetOperationalOptionsAsync(ct);
        if (currentOptions.IsFailure)
            return Result.Failure<OnboardingPaymentMethodAdminResponse>(currentOptions.Error);

        var beforeOption = currentOptions.Value.FirstOrDefault(option =>
            option.Provider == command.Provider && option.Method == command.Method
        );
        if (beforeOption is null)
            return Result.Failure<OnboardingPaymentMethodAdminResponse>(
                new Error("PaymentMethodCatalog.OptionNotFound", "Payment method is not configured for onboarding.")
            );

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await overrides.GetAsync(command.Provider, command.Method.ToString(), ct);
        Result mutation;
        if (existing is null)
        {
            var created = OnboardingPaymentMethodOverride.Create(
                command.Provider,
                command.Method.ToString(),
                command.Enabled,
                command.DisabledReason,
                command.ActorUserId,
                nowUtc
            );
            if (created.IsFailure)
                return Result.Failure<OnboardingPaymentMethodAdminResponse>(created.Error);

            existing = created.Value;
            await overrides.AddAsync(existing, ct);
            mutation = Result.Success();
        }
        else
        {
            mutation = existing.UpdateAvailability(
                command.Enabled,
                command.DisabledReason,
                command.ActorUserId,
                nowUtc
            );
        }

        if (mutation.IsFailure)
            return Result.Failure<OnboardingPaymentMethodAdminResponse>(mutation.Error);

        await AuditEntryFactory.AppendAsync(
            audit,
            Guid.Empty,
            nameof(OnboardingPaymentMethodOverride),
            existing.Id,
            PaymentAuditAction.OnboardingPaymentMethodAvailabilityChanged,
            command.ActorUserId,
            correlation.CorrelationId,
            before: new
            {
                beforeOption.Provider,
                beforeOption.Method,
                beforeOption.Enabled,
                beforeOption.DisabledReason,
            },
            after: new
            {
                command.Provider,
                command.Method,
                command.Enabled,
                DisabledReason = command.Enabled ? null : existing.DisabledReason,
            },
            reason: existing.DisabledReason,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        var refreshedOptions = await catalog.GetOperationalOptionsAsync(ct);
        if (refreshedOptions.IsFailure)
            return Result.Failure<OnboardingPaymentMethodAdminResponse>(refreshedOptions.Error);

        var afterOption = refreshedOptions.Value.First(option =>
            option.Provider == command.Provider && option.Method == command.Method
        );

        return Result.Success(
            new OnboardingPaymentMethodAdminResponse(
                afterOption.Provider.ToString(),
                afterOption.Method.ToString(),
                afterOption.DisplayName,
                afterOption.Enabled,
                afterOption.Priority,
                afterOption.DisabledReason,
                HasOverride: true,
                existing.UpdatedAtUtc,
                existing.UpdatedByUserId
            )
        );
    }
}
