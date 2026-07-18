using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Connect;

namespace TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

/// <summary>
/// Único gateway de Connect — a diferencia de <c>StripePaymentAdapter</c> (keyed por
/// provider, un adapter por proveedor de pago), esto no necesita factory: solo Stripe soporta
/// Connect en el catálogo actual. Corre siempre con <see cref="PlatformStripeCredentials.PlatformSecretKey"/>,
/// nunca con credenciales de tenant.
/// </summary>
public sealed class StripeConnectGateway : IStripeConnectGateway
{
    private readonly StripeClient _client;
    private readonly ILogger<StripeConnectGateway> _logger;

    public StripeConnectGateway(IOptions<PlatformStripeCredentials> options, ILogger<StripeConnectGateway> logger)
    {
        _client = new StripeClient(options.Value.PlatformSecretKey);
        _logger = logger;
    }

    public async Task<Result<string>> CreateAccountAsync(
        ConnectAccountType type,
        string tenantEmail,
        CancellationToken ct
    )
    {
        var service = new AccountService(_client);
        try
        {
            var account = await service.CreateAsync(
                new AccountCreateOptions
                {
                    Type = MapAccountType(type),
                    Email = tenantEmail,
                    Capabilities = new AccountCapabilitiesOptions
                    {
                        CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                        Transfers = new AccountCapabilitiesTransfersOptions { Requested = true },
                    },
                },
                cancellationToken: ct
            );

            return Result.Success(account.Id);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Connect CreateAccount failed for email {Email}", tenantEmail);
            return Result.Failure<string>(
                new Error("StripeConnect.Account.CreateFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<string>> CreateOnboardingLinkAsync(
        string stripeConnectAccountId,
        string refreshUrl,
        string returnUrl,
        CancellationToken ct
    )
    {
        var service = new AccountLinkService(_client);
        try
        {
            var link = await service.CreateAsync(
                new AccountLinkCreateOptions
                {
                    Account = stripeConnectAccountId,
                    RefreshUrl = refreshUrl,
                    ReturnUrl = returnUrl,
                    Type = "account_onboarding",
                },
                cancellationToken: ct
            );

            return Result.Success(link.Url);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe Connect CreateOnboardingLink failed for account {Account}",
                stripeConnectAccountId
            );
            return Result.Failure<string>(
                new Error("StripeConnect.OnboardingLink.CreateFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public async Task<Result<ConnectAccountStatusSnapshot>> GetAccountStatusAsync(
        string stripeConnectAccountId,
        CancellationToken ct
    )
    {
        var service = new AccountService(_client);
        try
        {
            var account = await service.GetAsync(stripeConnectAccountId, cancellationToken: ct);

            return Result.Success(
                new ConnectAccountStatusSnapshot(
                    account.ChargesEnabled,
                    account.PayoutsEnabled,
                    account.Requirements?.CurrentlyDue ?? []
                )
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe Connect GetAccountStatus failed for account {Account}",
                stripeConnectAccountId
            );
            return Result.Failure<ConnectAccountStatusSnapshot>(
                new Error("StripeConnect.Account.StatusFailed", ex.StripeError?.Message ?? ex.Message)
            );
        }
    }

    public Task<Result<ConnectWebhookEvent>> VerifyAndParseConnectWebhookAsync(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        CancellationToken ct
    )
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(rawPayload, signatureHeader, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Connect webhook signature verification failed.");
            return Task.FromResult(
                Result.Failure<ConnectWebhookEvent>(new Error("StripeConnect.WebhookSignature.Invalid", ex.Message))
            );
        }

        var result = stripeEvent.Type switch
        {
            "account.updated" or "capability.updated" => ParseAccountEvent(stripeEvent),
            "payout.paid" => ParsePayoutEvent(stripeEvent, failed: false),
            "payout.failed" => ParsePayoutEvent(stripeEvent, failed: true),
            _ => Result.Failure<ConnectWebhookEvent>(
                new Error(
                    "StripeConnect.Webhook.UnsupportedEventType",
                    $"Event type '{stripeEvent.Type}' is not handled."
                )
            ),
        };

        return Task.FromResult(result);
    }

    private static Result<ConnectWebhookEvent> ParseAccountEvent(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Account account)
            return Result.Failure<ConnectWebhookEvent>(
                new Error("StripeConnect.Webhook.UnexpectedPayload", "Expected an Account object.")
            );

        return Result.Success(
            new ConnectWebhookEvent(
                stripeEvent.Id,
                stripeEvent.Type,
                account.Id,
                ChargesEnabled: account.ChargesEnabled,
                PayoutsEnabled: account.PayoutsEnabled,
                RequirementsCurrentlyDue: account.Requirements?.CurrentlyDue ?? [],
                PayoutReference: null,
                PayoutAmountCents: null,
                PayoutCurrency: null,
                PayoutFailureReason: null
            )
        );
    }

    /// <summary><see cref="Event.Account"/> es el connected account dueño del payout — Stripe
    /// lo puebla en todo evento originado en una cuenta conectada, a diferencia de
    /// <c>account.updated</c> donde el id ya viene en el objeto mismo.</summary>
    private static Result<ConnectWebhookEvent> ParsePayoutEvent(Event stripeEvent, bool failed)
    {
        if (stripeEvent.Data.Object is not Payout payout)
            return Result.Failure<ConnectWebhookEvent>(
                new Error("StripeConnect.Webhook.UnexpectedPayload", "Expected a Payout object.")
            );

        if (string.IsNullOrWhiteSpace(stripeEvent.Account))
            return Result.Failure<ConnectWebhookEvent>(
                new Error("StripeConnect.Webhook.MissingAccount", "Payout event did not carry a connected account id.")
            );

        return Result.Success(
            new ConnectWebhookEvent(
                stripeEvent.Id,
                stripeEvent.Type,
                stripeEvent.Account,
                ChargesEnabled: null,
                PayoutsEnabled: null,
                RequirementsCurrentlyDue: null,
                PayoutReference: payout.Id,
                PayoutAmountCents: payout.Amount,
                PayoutCurrency: payout.Currency,
                PayoutFailureReason: failed ? payout.FailureMessage ?? "Payout failed." : null
            )
        );
    }

    private static string MapAccountType(ConnectAccountType type) =>
        type switch
        {
            ConnectAccountType.Standard => "standard",
            ConnectAccountType.Express => "express",
            ConnectAccountType.Custom => "custom",
            _ => "standard",
        };
}
