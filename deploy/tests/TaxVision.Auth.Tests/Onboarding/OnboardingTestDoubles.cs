using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Tests.Onboarding;

internal sealed class FakeAuthAuditWriter : IAuthAuditWriter
{
    public AuthAuditLog? Written { get; private set; }

    public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
    {
        Written = log;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRequestContext : IRequestContext
{
    public string? IpAddress => "203.0.113.10";
    public string? UserAgent => "test-agent";
}

/// <summary>Test doubles for PayFlow's Onboarding module, shared across the Fase 9 handler/consumer tests.</summary>
internal sealed class FakeOnboardingMetrics : IOnboardingMetrics
{
    public void RecordStarted() { }

    public void RecordCompleted() { }

    public void RecordFailed(string step) { }

    public void RecordManualReview() { }

    public void RecordDurationSeconds(double seconds, string outcome) { }

    public void RecordHttpClientRetry(string clientName) { }

    public void RecordHttpClientCircuitOpened(string clientName) { }
}

/// <summary>Fake best-effort catalog lookup. Devuelve el nombre configurado (o null si no) sin
/// pegarle a Subscription — el fix en vivo del día agregó <see cref="IPlanCatalogClient"/> como
/// dep de <c>PreviewRegistrationHandler</c> y <c>OnboardingPaymentSucceededConsumer</c>.</summary>
internal sealed class FakePlanCatalogClient(string? planName = null) : IPlanCatalogClient
{
    public Task<string?> GetPlanNameAsync(Guid planId, CancellationToken ct = default) => Task.FromResult(planName);
}

internal sealed class FakeEmailVerificationChallengeRepository : IEmailVerificationChallengeRepository
{
    public EmailVerificationChallenge? Challenge { get; set; }

    public Task<EmailVerificationChallenge?> GetByIdAsync(Guid challengeId, CancellationToken ct = default) =>
        Task.FromResult(Challenge);

    public Task AddAsync(EmailVerificationChallenge challenge, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class FakeTenantOnboardingRepository : ITenantOnboardingRepository
{
    public TenantOnboarding? Existing { get; set; }
    public TenantOnboarding? Added { get; private set; }

    public Task<TenantOnboarding?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Existing);

    public Task<TenantOnboarding?> GetByRegistrationTokenHashAsync(
        string registrationTokenHash,
        CancellationToken ct = default
    ) => Task.FromResult(Existing?.RegistrationTokenHash == registrationTokenHash ? Existing : null);

    public Task AddAsync(TenantOnboarding onboarding, CancellationToken ct = default)
    {
        Added = onboarding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TenantOnboarding>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<TenantOnboarding>>(Existing is null ? [] : [Existing]);

    public Task<(IReadOnlyList<TenantOnboarding> Items, int TotalCount)> GetPagedAdminAsync(
        TenantOnboardingStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<TenantOnboarding> items = Existing is null ? [] : [Existing];
        return Task.FromResult((items, items.Count));
    }
}

internal sealed class FakeOnboardingSessionStore : IOnboardingSessionStore
{
    public Dictionary<string, OnboardingSession> Sessions { get; } = [];
    public List<string> RemovedHashes { get; } = [];

    public Task SetAsync(
        string sessionTokenHash,
        OnboardingSession session,
        TimeSpan ttl,
        CancellationToken ct = default
    )
    {
        Sessions[sessionTokenHash] = session;
        return Task.CompletedTask;
    }

    public Task<OnboardingSession?> GetAsync(string sessionTokenHash, CancellationToken ct = default) =>
        Task.FromResult(Sessions.TryGetValue(sessionTokenHash, out var session) ? session : null);

    public Task RemoveAsync(string sessionTokenHash, CancellationToken ct = default)
    {
        RemovedHashes.Add(sessionTokenHash);
        Sessions.Remove(sessionTokenHash);
        return Task.CompletedTask;
    }
}

internal sealed class FakePaymentAppOnboardingClient(Result<PaymentAppCheckoutResult> result)
    : IPaymentAppOnboardingClient
{
    public PaymentAppCheckoutRequest? LastRequest { get; private set; }
    public PaymentAppPaymentOptionsRequest? LastOptionsRequest { get; private set; }
    public PaymentAppReconcileRequest? LastReconcileRequest { get; private set; }
    public Result<PaymentAppPaymentOptionsResult> OptionsResult { get; set; } =
        Result.Success(
            new PaymentAppPaymentOptionsResult([
                new PaymentAppPaymentOption(
                    "Stripe",
                    "Card",
                    "Card",
                    Enabled: true,
                    Priority: 10,
                    DisabledReason: null
                ),
            ])
        );
    public Result<PaymentAppReconcileResult> ReconcileResult { get; set; } =
        Result.Success(
            new PaymentAppReconcileResult(
                Guid.NewGuid(),
                OnboardingPaymentStatus.Processing,
                4900,
                "USD",
                FailureCode: null,
                FailureMessage: null,
                ProviderPaymentReference: null,
                PaidAtUtc: null
            )
        );

    public Task<Result<PaymentAppCheckoutResult>> CreateCheckoutAsync(
        PaymentAppCheckoutRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        return Task.FromResult(result);
    }

    public Task<Result<PaymentAppPaymentOptionsResult>> GetPaymentOptionsAsync(
        PaymentAppPaymentOptionsRequest request,
        CancellationToken ct = default
    )
    {
        LastOptionsRequest = request;
        return Task.FromResult(OptionsResult);
    }

    public Task<Result<PaymentAppReconcileResult>> ReconcileCheckoutAsync(
        PaymentAppReconcileRequest request,
        CancellationToken ct = default
    )
    {
        LastReconcileRequest = request;
        return Task.FromResult(ReconcileResult);
    }
}

internal sealed class FakeReceiptDocumentClient(Result result) : IReceiptDocumentClient
{
    public RequestReceiptGenerationRequest? LastRequest { get; private set; }

    public Task<Result> RequestReceiptGenerationAsync(
        RequestReceiptGenerationRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}

internal sealed class FakeCloudStorageDownloadUrlClient(Result<Uri> result) : ICloudStorageDownloadUrlClient
{
    public Guid? LastFileId { get; private set; }

    public Task<Result<Uri>> GetDownloadUrlAsync(Guid fileId, CancellationToken ct = default)
    {
        LastFileId = fileId;
        return Task.FromResult(result);
    }
}

internal sealed class FakeTenantProvisioningClient(Result result) : ITenantProvisioningClient
{
    public CreateTenantForOnboardingRequest? LastRequest { get; private set; }

    public Task<Result> CreateTenantAsync(CreateTenantForOnboardingRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}

internal sealed class FakeAuthInternalOwnerCreationClient(Result result) : IAuthInternalOwnerCreationClient
{
    public CreateTenantOwnerForOnboardingRequest? LastRequest { get; private set; }

    public Task<Result> CreateOwnerAsync(CreateTenantOwnerForOnboardingRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}

internal sealed class FakeSubscriptionActivationClient(Result result) : ISubscriptionActivationClient
{
    public ActivateSubscriptionForOnboardingRequest? LastRequest { get; private set; }

    public Task<Result> ActivateAsync(ActivateSubscriptionForOnboardingRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(result);
    }
}

internal sealed class FakeTokenReferenceStore : ITokenReferenceStore
{
    public string? Stored { get; private set; }
    public Guid Reference { get; } = Guid.NewGuid();
    public Guid? StoredReference { get; private set; }
    public string? ToConsume { get; set; }
    public string? ToPeek { get; set; }

    public Task<Guid> StoreAsync(string rawToken, CancellationToken ct = default)
    {
        Stored = rawToken;
        StoredReference = Reference;
        return Task.FromResult(Reference);
    }

    public Task StoreAsync(Guid reference, string rawToken, CancellationToken ct = default)
    {
        Stored = rawToken;
        StoredReference = reference;
        return Task.CompletedTask;
    }

    public Task<string?> ConsumeAsync(Guid reference, CancellationToken ct = default) =>
        Task.FromResult(reference == Reference || reference == StoredReference ? ToConsume : null);

    public Task<string?> PeekAsync(Guid reference, CancellationToken ct = default) =>
        Task.FromResult(reference == Reference || reference == StoredReference ? ToPeek : null);
}

internal static class OnboardingTestFactory
{
    public static EmailVerificationChallenge VerifiedChallenge(string email, DateTime nowUtc)
    {
        var challenge = EmailVerificationChallenge.Create(email, "123456", nowUtc, TimeSpan.FromMinutes(10)).Value;
        if (!challenge.Verify("123456", nowUtc).IsSuccess)
            throw new InvalidOperationException("Test setup failure: could not verify the OTP challenge.");
        return challenge;
    }

    public static TenantOnboarding NewOnboarding(DateTime now) =>
        TenantOnboarding.Create("buyer@example.com", now, Guid.NewGuid(), "Ada", "Lovelace", null, now).Value;
}
