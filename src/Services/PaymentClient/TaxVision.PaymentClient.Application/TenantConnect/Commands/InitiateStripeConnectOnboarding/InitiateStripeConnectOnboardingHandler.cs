using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.InitiateStripeConnectOnboarding;

/// <summary>
/// Idempotente por diseño: si el tenant ya tiene una Connected Account (de un intento previo
/// que no terminó el formulario), reusa esa cuenta y solo genera un <c>AccountLink</c> nuevo
/// — los links expiran a los pocos minutos, así que re-pedir onboarding es el camino normal,
/// no un caso de error.
/// </summary>
public static class InitiateStripeConnectOnboardingHandler
{
    public static async Task<Result<InitiateStripeConnectOnboardingResponse>> Handle(
        InitiateStripeConnectOnboardingCommand command,
        ITenantConnectAccountRepository connectAccounts,
        IStripeConnectGateway gateway,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var account = await connectAccounts.GetByTenantAndProviderAsync(
            command.TenantId,
            PaymentProviderCode.Stripe,
            ct
        );
        var isNewAccount = account is null;

        if (account is null)
        {
            var createResult = await gateway.CreateAccountAsync(command.Type, command.Email, ct);
            if (createResult.IsFailure)
                return Result.Failure<InitiateStripeConnectOnboardingResponse>(createResult.Error);

            var stripeAccountIdResult = StripeConnectAccountId.Create(createResult.Value);
            if (stripeAccountIdResult.IsFailure)
                return Result.Failure<InitiateStripeConnectOnboardingResponse>(stripeAccountIdResult.Error);

            var accountResult = TenantConnectAccount.Create(
                command.TenantId,
                PaymentProviderCode.Stripe,
                command.Type,
                stripeAccountIdResult.Value,
                nowUtc
            );
            if (accountResult.IsFailure)
                return Result.Failure<InitiateStripeConnectOnboardingResponse>(accountResult.Error);

            account = accountResult.Value;
            await connectAccounts.AddAsync(account, ct);
        }

        var linkResult = await gateway.CreateOnboardingLinkAsync(
            account.StripeConnectAccountId.Value,
            command.RefreshUrl,
            command.ReturnUrl,
            ct
        );
        if (linkResult.IsFailure)
            return Result.Failure<InitiateStripeConnectOnboardingResponse>(linkResult.Error);

        var onboardResult = account.InitiateOnboarding(nowUtc);
        if (onboardResult.IsFailure)
            return Result.Failure<InitiateStripeConnectOnboardingResponse>(onboardResult.Error);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantConnectAccount),
            account.Id,
            isNewAccount
                ? PaymentAuditAction.TenantConnectAccountCreated
                : PaymentAuditAction.TenantConnectAccountOnboardingInitiated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { account.StripeConnectAccountId.Value, command.Type },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new InitiateStripeConnectOnboardingResponse(account.Id, linkResult.Value));
    }
}
