using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 4 — ciclo de vida del agregado TenantOnboarding.</summary>
public sealed class TenantOnboardingTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static TenantOnboarding Valid() =>
        TenantOnboarding
            .Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", "+1 809-555-0100", Now)
            .Value;

    private static RegistrationTokenHash ValidHash() => RegistrationTokenHash.Create(new string('a', 64)).Value;

    private static TenantOnboarding AtPaymentProcessing()
    {
        var onboarding = Valid();
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test_123");
        return onboarding;
    }

    private static TenantOnboarding AtPaymentCompleted()
    {
        var onboarding = AtPaymentProcessing();
        onboarding.MarkPaymentCompleted("cs_test_123", Now);
        return onboarding;
    }

    private static TenantOnboarding AtRegistrationPending()
    {
        var onboarding = AtPaymentCompleted();
        onboarding.SetRegistrationToken(ValidHash(), Now.AddHours(72));
        return onboarding;
    }

    private static TenantOnboarding AtProvisioning() => AtProvisioningAtStep(TenantProvisioningStep.Tenant, out _);

    private static TenantOnboarding AtProvisioningAtStep(TenantProvisioningStep step, out TenantOnboarding onboarding)
    {
        onboarding = AtRegistrationPending();
        onboarding.StartProvisioning(
            "Castillo Tax Services",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now
        );

        switch (step)
        {
            case TenantProvisioningStep.TenantAdmin:
                onboarding.SetTenantCreated(Guid.NewGuid());
                break;
            case TenantProvisioningStep.Subscription:
                onboarding.SetTenantCreated(Guid.NewGuid());
                onboarding.SetTenantAdminCreated(Guid.NewGuid());
                break;
            case TenantProvisioningStep.CloudStorage:
                onboarding.SetTenantCreated(Guid.NewGuid());
                onboarding.SetTenantAdminCreated(Guid.NewGuid());
                onboarding.SetSubscriptionActivated(Guid.NewGuid());
                break;
        }

        return onboarding;
    }

    private static TenantOnboarding AtReadyToComplete()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.CloudStorage, out _);
        onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage);
        onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain);
        onboarding.MarkStepCompleted(TenantProvisioningStep.Defaults);
        return onboarding;
    }

    // ---------- Create ----------

    [Fact]
    public void Create_succeeds_with_valid_data_and_starts_pending_payment()
    {
        var result = TenantOnboarding.Create(
            "owner@castillotax.com",
            Now,
            Guid.NewGuid(),
            "Carlos",
            "Castillo",
            "+1 809-555-0100",
            Now
        );

        Assert.True(result.IsSuccess);
        var onboarding = result.Value;
        Assert.Equal("owner@castillotax.com", onboarding.Email);
        Assert.Equal(TenantOnboardingStatus.PendingPayment, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.None, onboarding.CurrentStep);
    }

    [Fact]
    public void Create_normalizes_email_and_trims_names()
    {
        var result = TenantOnboarding.Create(
            "  OWNER@Castillotax.com  ",
            Now,
            Guid.NewGuid(),
            "  Carlos  ",
            "  Castillo  ",
            null,
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("owner@castillotax.com", result.Value.Email);
        Assert.Equal("Carlos", result.Value.FirstName);
        Assert.Equal("Castillo", result.Value.LastName);
        Assert.Null(result.Value.Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Create_rejects_invalid_email(string email)
    {
        var result = TenantOnboarding.Create(email, Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.Email", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_plan()
    {
        var result = TenantOnboarding.Create("owner@castillotax.com", Now, Guid.Empty, "Carlos", "Castillo", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.Plan", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_missing_names()
    {
        var result = TenantOnboarding.Create("owner@castillotax.com", Now, Guid.NewGuid(), "", "Castillo", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.Name", result.Error.Code);
    }

    // ---------- MarkPaymentProcessing ----------

    [Fact]
    public void MarkPaymentProcessing_transitions_from_pending_payment()
    {
        var onboarding = Valid();
        var paymentId = Guid.NewGuid();

        var result = onboarding.MarkPaymentProcessing(paymentId, "cs_test_123");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentProcessing, onboarding.Status);
        Assert.Equal(paymentId, onboarding.PaymentId);
        Assert.Equal("Processing", onboarding.PaymentStatus);
    }

    [Fact]
    public void MarkPaymentProcessing_rejects_empty_ids()
    {
        var onboarding = Valid();

        var result = onboarding.MarkPaymentProcessing(Guid.Empty, "cs_test_123");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.PaymentReference", result.Error.Code);
    }

    [Fact]
    public void MarkPaymentProcessing_is_idempotent_for_the_same_payment_id()
    {
        var onboarding = AtPaymentProcessing();
        var paymentId = onboarding.PaymentId!.Value;

        var result = onboarding.MarkPaymentProcessing(paymentId, "cs_test_123");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentProcessing, onboarding.Status);
    }

    [Fact]
    public void MarkPaymentProcessing_rejects_wrong_state()
    {
        var onboarding = AtPaymentCompleted();

        var result = onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test_456");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- MarkPaymentCompleted ----------

    [Fact]
    public void MarkPaymentCompleted_transitions_from_payment_processing()
    {
        var onboarding = AtPaymentProcessing();

        var result = onboarding.MarkPaymentCompleted("cs_test_123", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentCompleted, onboarding.Status);
        Assert.Equal("Succeeded", onboarding.PaymentStatus);
        Assert.Equal(Now, onboarding.PaymentCompletedAtUtc);
    }

    [Fact]
    public void MarkPaymentCompleted_is_idempotent_for_the_same_reference()
    {
        var onboarding = AtPaymentCompleted();

        var result = onboarding.MarkPaymentCompleted("cs_test_123", Now.AddSeconds(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentCompleted, onboarding.Status);
    }

    [Fact]
    public void MarkPaymentCompleted_rejects_reference_mismatch()
    {
        var onboarding = AtPaymentProcessing();

        var result = onboarding.MarkPaymentCompleted("cs_test_DIFFERENT", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.PaymentReferenceMismatch", result.Error.Code);
    }

    [Fact]
    public void MarkPaymentCompleted_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.MarkPaymentCompleted("cs_test_123", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- RecordSettledAmount ----------

    [Fact]
    public void RecordSettledAmount_persists_the_paid_amount_when_no_code_froze_the_breakdown()
    {
        // Carril pagado sin código: el desglose nunca se congeló, así que el monto vivía solo en el evento.
        var onboarding = AtPaymentCompleted();
        Assert.Null(onboarding.NetAmountCents);

        onboarding.RecordSettledAmount(4999, "USD");

        // Bruto = neto = lo cobrado, descuento 0 → un recibo regenerado (resend/reconcile) sale con el monto real.
        Assert.Equal(4999, onboarding.GrossAmountCents);
        Assert.Equal(0, onboarding.TotalDiscountCents);
        Assert.Equal(4999, onboarding.NetAmountCents);
        Assert.Equal("USD", onboarding.Currency);
    }

    [Fact]
    public void RecordSettledAmount_is_noop_for_the_zero_amount_carril()
    {
        var onboarding = AtPaymentCompleted();

        onboarding.RecordSettledAmount(0, "USD");

        Assert.Null(onboarding.NetAmountCents);
    }

    // ---------- MarkPaymentFailed ----------

    [Fact]
    public void MarkPaymentFailed_transitions_from_payment_processing()
    {
        var onboarding = AtPaymentProcessing();

        var result = onboarding.MarkPaymentFailed("card_declined");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentFailed, onboarding.Status);
        Assert.Equal("card_declined", onboarding.FailureReason);
    }

    [Fact]
    public void MarkPaymentFailed_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.MarkPaymentFailed("card_declined");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- SetRegistrationToken ----------

    [Fact]
    public void SetRegistrationToken_transitions_from_payment_completed()
    {
        var onboarding = AtPaymentCompleted();
        var hash = ValidHash();
        var expires = Now.AddHours(72);

        var result = onboarding.SetRegistrationToken(hash, expires);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.Equal(hash.Value, onboarding.RegistrationTokenHash);
        Assert.Equal(expires, onboarding.RegistrationTokenExpiresAtUtc);
    }

    [Fact]
    public void SetRegistrationToken_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.SetRegistrationToken(ValidHash(), Now.AddHours(72));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- StartProvisioning ----------

    [Fact]
    public void StartProvisioning_transitions_from_registration_pending()
    {
        var onboarding = AtRegistrationPending();

        var result = onboarding.StartProvisioning(
            "Castillo Tax Services",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Provisioning, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Tenant, onboarding.CurrentStep);
        Assert.Equal("castillotax", onboarding.RequestedSubdomain);
    }

    [Fact]
    public void StartProvisioning_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.StartProvisioning(
            "Castillo Tax Services",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void StartProvisioning_rejects_used_token()
    {
        var onboarding = AtRegistrationPending();
        onboarding.ConsumeRegistrationToken(Now);

        var result = onboarding.StartProvisioning(
            "Castillo Tax Services",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TokenUsed", result.Error.Code);
    }

    [Fact]
    public void StartProvisioning_rejects_expired_token()
    {
        var onboarding = AtRegistrationPending();

        var result = onboarding.StartProvisioning(
            "Castillo Tax Services",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now.AddHours(73)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TokenExpired", result.Error.Code);
    }

    [Fact]
    public void StartProvisioning_rejects_missing_office_name()
    {
        var onboarding = AtRegistrationPending();

        var result = onboarding.StartProvisioning(
            "",
            "castillotax",
            Guid.NewGuid(),
            new string('b', 64),
            "203.0.113.10",
            "Mozilla/5.0",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.RegistrationDetails", result.Error.Code);
    }

    // ---------- SetTenantCreated ----------

    [Fact]
    public void SetTenantCreated_advances_to_tenant_admin_step()
    {
        var onboarding = AtProvisioning();
        var tenantId = Guid.NewGuid();

        var result = onboarding.SetTenantCreated(tenantId);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, onboarding.TenantId);
        Assert.Equal(TenantProvisioningStep.TenantAdmin, onboarding.CurrentStep);
    }

    [Fact]
    public void SetTenantCreated_is_idempotent_for_the_same_tenant_id()
    {
        var onboarding = AtProvisioning();
        var tenantId = Guid.NewGuid();
        onboarding.SetTenantCreated(tenantId);

        var result = onboarding.SetTenantCreated(tenantId);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantProvisioningStep.TenantAdmin, onboarding.CurrentStep);
    }

    [Fact]
    public void SetTenantCreated_rejects_wrong_step()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.Subscription, out _);

        var result = onboarding.SetTenantCreated(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- SetTenantAdminCreated ----------

    [Fact]
    public void SetTenantAdminCreated_advances_to_subscription_step()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.TenantAdmin, out _);
        var userId = Guid.NewGuid();

        var result = onboarding.SetTenantAdminCreated(userId);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, onboarding.UserId);
        Assert.Equal(TenantProvisioningStep.Subscription, onboarding.CurrentStep);
    }

    [Fact]
    public void SetTenantAdminCreated_rejects_wrong_step()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.SetTenantAdminCreated(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- SetSubscriptionActivated ----------

    [Fact]
    public void SetSubscriptionActivated_advances_to_cloud_storage_step()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.Subscription, out _);
        var subscriptionId = Guid.NewGuid();

        var result = onboarding.SetSubscriptionActivated(subscriptionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(subscriptionId, onboarding.SubscriptionId);
        Assert.Equal(TenantProvisioningStep.CloudStorage, onboarding.CurrentStep);
    }

    [Fact]
    public void SetSubscriptionActivated_rejects_wrong_step()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.SetSubscriptionActivated(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- MarkStepCompleted ----------

    [Fact]
    public void MarkStepCompleted_progresses_cloud_storage_subdomain_and_defaults()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.CloudStorage, out _);

        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage).IsSuccess);
        Assert.Equal(TenantProvisioningStep.Subdomain, onboarding.CurrentStep);

        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain).IsSuccess);
        Assert.Equal(TenantProvisioningStep.Defaults, onboarding.CurrentStep);

        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.Defaults).IsSuccess);
        Assert.Equal(TenantProvisioningStep.Completed, onboarding.CurrentStep);
    }

    [Fact]
    public void MarkStepCompleted_is_idempotent_on_replay()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.CloudStorage, out _);
        onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage);

        var result = onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantProvisioningStep.Subdomain, onboarding.CurrentStep);
    }

    [Fact]
    public void MarkStepCompleted_rejects_step_out_of_order()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void MarkStepCompleted_rejects_none_and_completed()
    {
        var onboarding = AtProvisioning();

        Assert.Equal("Onboarding.InvalidStep", onboarding.MarkStepCompleted(TenantProvisioningStep.None).Error.Code);
        Assert.Equal(
            "Onboarding.InvalidStep",
            onboarding.MarkStepCompleted(TenantProvisioningStep.Completed).Error.Code
        );
    }

    // ---------- MarkProvisioningFailed / ResumeProvisioning / MarkManualReview ----------

    [Fact]
    public void MarkProvisioningFailed_records_failed_step_and_reason()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.Subscription, out _);

        var result = onboarding.MarkProvisioningFailed(
            TenantProvisioningStep.Subscription,
            "Subscription.DbUnavailable",
            "SQL timeout while activating the subscription."
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.ProvisioningFailed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Subscription, onboarding.FailedStep);
        Assert.Equal("Subscription.DbUnavailable", onboarding.FailureCode);
    }

    [Fact]
    public void MarkProvisioningFailed_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "x");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void ResumeProvisioning_returns_to_provisioning_from_failed()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout");

        var result = onboarding.ResumeProvisioning();

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Provisioning, onboarding.Status);
    }

    [Fact]
    public void ResumeProvisioning_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.ResumeProvisioning();

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void MarkManualReview_transitions_from_provisioning_failed()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout");

        var result = onboarding.MarkManualReview("Retries exhausted after 24h.");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.ManualReview, onboarding.Status);
    }

    [Fact]
    public void MarkManualReview_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.MarkManualReview("Retries exhausted after 24h.");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- MarkCompleted / ConsumeRegistrationToken ----------

    [Fact]
    public void MarkCompleted_transitions_once_all_steps_are_done()
    {
        var onboarding = AtReadyToComplete();

        var result = onboarding.MarkCompleted(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
        Assert.Equal(Now, onboarding.RegistrationCompletedAtUtc);
    }

    [Fact]
    public void MarkCompleted_is_idempotent_when_already_completed()
    {
        var onboarding = AtReadyToComplete();
        onboarding.MarkCompleted(Now);

        var result = onboarding.MarkCompleted(Now.AddSeconds(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
    }

    [Fact]
    public void MarkCompleted_rejects_when_steps_are_not_all_done()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.MarkCompleted(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void ConsumeRegistrationToken_marks_token_as_used()
    {
        var onboarding = AtRegistrationPending();

        var result = onboarding.ConsumeRegistrationToken(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, onboarding.RegistrationTokenUsedAtUtc);
    }

    [Fact]
    public void ConsumeRegistrationToken_is_idempotent_on_replay()
    {
        var onboarding = AtRegistrationPending();
        onboarding.ConsumeRegistrationToken(Now);

        var result = onboarding.ConsumeRegistrationToken(Now.AddSeconds(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, onboarding.RegistrationTokenUsedAtUtc);
    }

    [Fact]
    public void ConsumeRegistrationToken_rejects_when_no_token_was_issued()
    {
        var onboarding = Valid();

        var result = onboarding.ConsumeRegistrationToken(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NoToken", result.Error.Code);
    }

    // ---------- Cancel / MarkExpired / MarkRefunded ----------

    [Theory]
    [MemberData(nameof(CancellableStates))]
    public void Cancel_succeeds_from_cancellable_states(TenantOnboarding onboarding)
    {
        var result = onboarding.Cancel("Customer requested cancellation.");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Cancelled, onboarding.Status);
    }

    public static IEnumerable<object[]> CancellableStates()
    {
        yield return new object[] { Valid() };
        yield return new object[] { AtPaymentProcessing() };
        yield return new object[] { PaymentFailedOnboarding() };
    }

    private static TenantOnboarding PaymentFailedOnboarding()
    {
        var onboarding = AtPaymentProcessing();
        onboarding.MarkPaymentFailed("card_declined");
        return onboarding;
    }

    [Fact]
    public void Cancel_rejects_states_past_payment()
    {
        var onboarding = AtPaymentCompleted();

        var result = onboarding.Cancel("too late");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Theory]
    [MemberData(nameof(ExpirableStates))]
    public void MarkExpired_succeeds_from_expirable_states(TenantOnboarding onboarding)
    {
        var result = onboarding.MarkExpired();

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Expired, onboarding.Status);
    }

    public static IEnumerable<object[]> ExpirableStates()
    {
        yield return new object[] { Valid() };
        yield return new object[] { AtPaymentProcessing() };
        yield return new object[] { AtRegistrationPending() };
    }

    [Fact]
    public void MarkExpired_rejects_states_past_registration()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.MarkExpired();

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void MarkRefunded_succeeds_from_provisioning_failed()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout");

        var result = onboarding.MarkRefunded("Cannot recover, refunding per support decision.");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Refunded, onboarding.Status);
    }

    [Fact]
    public void MarkRefunded_succeeds_from_manual_review()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout");
        onboarding.MarkManualReview("escalated");

        var result = onboarding.MarkRefunded("Cannot recover, refunding per support decision.");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Refunded, onboarding.Status);
    }

    [Fact]
    public void MarkRefunded_rejects_wrong_state()
    {
        var onboarding = Valid();

        var result = onboarding.MarkRefunded("too early");

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- ScheduleRetry / ResetRetryState (Fase 17) ----------

    [Fact]
    public void ScheduleRetry_sets_next_retry_without_consuming_attempt()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip");

        var result = onboarding.ScheduleRetry(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, onboarding.RetryAttempt);
        Assert.Equal(Now.AddMinutes(5), onboarding.NextRetryAtUtc);
    }

    [Fact]
    public void ScheduleRetry_preserves_dispatched_attempts_across_repeated_failures()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip");
        onboarding.ScheduleRetry(Now.AddMinutes(5));
        onboarding.MarkRetryDispatched(Now.AddMinutes(10));
        onboarding.ResumeProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip again");

        var result = onboarding.ScheduleRetry(Now.AddMinutes(15));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, onboarding.RetryAttempt);
    }

    [Fact]
    public void ScheduleRetry_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.ScheduleRetry(Now.AddMinutes(5));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void MarkRetryDispatched_preserves_failure_context_and_consumes_attempt()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip");
        onboarding.ScheduleRetry(Now.AddMinutes(-1));

        var result = onboarding.MarkRetryDispatched(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.ProvisioningFailed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Tenant, onboarding.FailedStep);
        Assert.Equal("Tenant.RequestFailed", onboarding.FailureCode);
        Assert.Equal(1, onboarding.RetryAttempt);
        Assert.Equal(Now.AddMinutes(5), onboarding.NextRetryAtUtc);
    }

    [Fact]
    public void MarkRetryDispatched_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.MarkRetryDispatched(Now.AddMinutes(5));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    [Fact]
    public void ResumeProvisioning_does_not_reset_retry_attempt()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip");
        onboarding.ScheduleRetry(Now.AddMinutes(5));
        onboarding.MarkRetryDispatched(Now.AddMinutes(10));

        onboarding.ResumeProvisioning();

        Assert.Equal(1, onboarding.RetryAttempt);
    }

    [Fact]
    public void ResetRetryState_clears_attempt_and_next_retry()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.RequestFailed", "network blip");
        onboarding.ScheduleRetry(Now.AddMinutes(5));
        onboarding.MarkRetryDispatched(Now.AddMinutes(10));

        var result = onboarding.ResetRetryState();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, onboarding.RetryAttempt);
        Assert.Null(onboarding.NextRetryAtUtc);
    }

    [Fact]
    public void ResetRetryState_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.ResetRetryState();

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- UpdateProvisioningInputs (Fase 17) ----------

    [Fact]
    public void UpdateProvisioningInputs_updates_subdomain_and_plan()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.SubdomainTaken", "taken");
        var newPlanId = Guid.NewGuid();

        var result = onboarding.UpdateProvisioningInputs("newsubdomain", newPlanId);

        Assert.True(result.IsSuccess);
        Assert.Equal("newsubdomain", onboarding.RequestedSubdomain);
        Assert.Equal(newPlanId, onboarding.PlanId);
    }

    [Fact]
    public void UpdateProvisioningInputs_allows_partial_update()
    {
        var onboarding = AtProvisioning();
        var originalPlanId = onboarding.PlanId;
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.SubdomainTaken", "taken");

        var result = onboarding.UpdateProvisioningInputs("newsubdomain", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("newsubdomain", onboarding.RequestedSubdomain);
        Assert.Equal(originalPlanId, onboarding.PlanId);
    }

    [Fact]
    public void UpdateProvisioningInputs_rejects_blank_subdomain()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.SubdomainTaken", "taken");

        var result = onboarding.UpdateProvisioningInputs("   ", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.RegistrationDetails", result.Error.Code);
    }

    [Fact]
    public void UpdateProvisioningInputs_rejects_empty_plan()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.SubdomainTaken", "taken");

        var result = onboarding.UpdateProvisioningInputs(null, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.Plan", result.Error.Code);
    }

    [Fact]
    public void UpdateProvisioningInputs_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.UpdateProvisioningInputs("newsubdomain", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- AdminForceComplete (Fase 17) ----------

    [Fact]
    public void AdminForceComplete_completes_when_all_identities_exist()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.CloudStorage, out _);
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.CloudStorage, "CloudStorage.RequestFailed", "timeout");

        var result = onboarding.AdminForceComplete("Manually verified all downstream resources exist.", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Completed, onboarding.CurrentStep);
        Assert.Equal(Now, onboarding.RegistrationCompletedAtUtc);
    }

    [Fact]
    public void AdminForceComplete_rejects_when_tenant_admin_or_subscription_missing()
    {
        var onboarding = AtProvisioningAtStep(TenantProvisioningStep.TenantAdmin, out _);
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.TenantAdmin, "Auth.RequestFailed", "timeout");

        var result = onboarding.AdminForceComplete("Trying to skip ahead.", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ForceCompleteIncomplete", result.Error.Code);
    }

    [Fact]
    public void AdminForceComplete_rejects_wrong_state()
    {
        var onboarding = AtProvisioning();

        var result = onboarding.AdminForceComplete("too early", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- Full happy path ----------

    [Fact]
    public void Full_happy_path_reaches_completed_with_all_ids_set()
    {
        var onboarding = Valid();
        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        Assert.True(onboarding.MarkPaymentProcessing(paymentId, "cs_test_full").IsSuccess);
        Assert.True(onboarding.MarkPaymentCompleted("cs_test_full", Now).IsSuccess);
        Assert.True(onboarding.SetRegistrationToken(ValidHash(), Now.AddHours(72)).IsSuccess);
        Assert.True(
            onboarding
                .StartProvisioning(
                    "Castillo Tax Services",
                    "castillotax",
                    Guid.NewGuid(),
                    new string('b', 64),
                    "203.0.113.10",
                    "Mozilla/5.0",
                    Now
                )
                .IsSuccess
        );
        Assert.True(onboarding.SetTenantCreated(tenantId).IsSuccess);
        Assert.True(onboarding.SetTenantAdminCreated(userId).IsSuccess);
        Assert.True(onboarding.SetSubscriptionActivated(subscriptionId).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain).IsSuccess);
        Assert.True(onboarding.MarkStepCompleted(TenantProvisioningStep.Defaults).IsSuccess);
        Assert.True(onboarding.MarkCompleted(Now).IsSuccess);
        Assert.True(onboarding.ConsumeRegistrationToken(Now).IsSuccess);

        Assert.Equal(TenantOnboardingStatus.Completed, onboarding.Status);
        Assert.Equal(TenantProvisioningStep.Completed, onboarding.CurrentStep);
        Assert.Equal(tenantId, onboarding.TenantId);
        Assert.Equal(userId, onboarding.UserId);
        Assert.Equal(subscriptionId, onboarding.SubscriptionId);
        Assert.NotNull(onboarding.RegistrationTokenUsedAtUtc);
        Assert.NotNull(onboarding.RegistrationCompletedAtUtc);
    }
}
