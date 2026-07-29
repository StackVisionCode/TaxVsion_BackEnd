using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// PayFlow Fase 7 — matriz exhaustiva de las 12 transiciones de estado (<see cref="TenantOnboardingStatus"/>)
/// x los 12 métodos que las gatillan. No toca código productivo: sólo ejercita <see cref="TenantOnboarding"/>
/// (Fase 4) desde cada uno de sus 12 estados posibles, complementando los casos puntuales ya cubiertos en
/// TenantOnboardingTests.cs con una verificación sistemática de que CADA combinación (estado, método) termina
/// en {éxito | failure con "Onboarding.InvalidState"} — nunca en una excepción ni en un estado inconsistente.
///
/// Se excluyen del cruce SetTenantCreated/SetTenantAdminCreated/SetSubscriptionActivated/MarkStepCompleted
/// (gatean por <see cref="TenantProvisioningStep"/> dentro de Provisioning, no por Status — ya cubiertos paso
/// a paso en TenantOnboardingTests.cs) y ConsumeRegistrationToken (no gatea por Status en absoluto).
/// </summary>
public sealed class StatusTransitionsMatrixTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static readonly Guid FixedPaymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string FixedPaymentReference = "cs_test_matrix";
    private static readonly RegistrationTokenHash FixedHash = RegistrationTokenHash.Create(new string('a', 64)).Value;
    private static readonly Guid FixedTermsVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string FixedTermsHash = new('b', 64);
    private static readonly Guid FixedTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FixedUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FixedSubscriptionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ---------- Un builder por cada uno de los 12 estados, encadenados con valores fijos ----------
    // (los valores fijos son deliberados: permiten que los métodos con rama idempotente — p.ej.
    // MarkPaymentProcessing/MarkPaymentCompleted/MarkCompleted — la disparen realmente al reinvocarse
    // desde su propio estado destino, en vez de fallar por un mismatch de datos incidental).

    private static TenantOnboarding AtPendingPayment() =>
        TenantOnboarding.Create("owner@matrixtax.com", Now, Guid.NewGuid(), "Ada", "Matrix", null, Now).Value;

    private static TenantOnboarding AtPaymentProcessing()
    {
        var onboarding = AtPendingPayment();
        onboarding.MarkPaymentProcessing(FixedPaymentId, FixedPaymentReference);
        return onboarding;
    }

    private static TenantOnboarding AtPaymentCompleted()
    {
        var onboarding = AtPaymentProcessing();
        onboarding.MarkPaymentCompleted(FixedPaymentReference, Now);
        return onboarding;
    }

    private static TenantOnboarding AtRegistrationPending()
    {
        var onboarding = AtPaymentCompleted();
        onboarding.SetRegistrationToken(FixedHash, Now.AddHours(72));
        return onboarding;
    }

    private static TenantOnboarding AtProvisioning()
    {
        var onboarding = AtRegistrationPending();
        onboarding.StartProvisioning(
            "Matrix Tax Services",
            "matrixtax",
            FixedTermsVersionId,
            FixedTermsHash,
            "203.0.113.20",
            "Mozilla/5.0",
            Now
        );
        return onboarding;
    }

    private static TenantOnboarding AtProvisioningFailed()
    {
        var onboarding = AtProvisioning();
        onboarding.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout");
        return onboarding;
    }

    private static TenantOnboarding AtManualReview()
    {
        var onboarding = AtProvisioningFailed();
        onboarding.MarkManualReview("escalated after retries");
        return onboarding;
    }

    private static TenantOnboarding AtCompleted()
    {
        var onboarding = AtProvisioning();
        onboarding.SetTenantCreated(FixedTenantId);
        onboarding.SetTenantAdminCreated(FixedUserId);
        onboarding.SetSubscriptionActivated(FixedSubscriptionId);
        onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage);
        onboarding.MarkStepCompleted(TenantProvisioningStep.Subdomain);
        onboarding.MarkStepCompleted(TenantProvisioningStep.Defaults);
        onboarding.MarkCompleted(Now);
        return onboarding;
    }

    private static TenantOnboarding AtPaymentFailed()
    {
        var onboarding = AtPaymentProcessing();
        onboarding.MarkPaymentFailed("card_declined");
        return onboarding;
    }

    private static TenantOnboarding AtCancelled()
    {
        var onboarding = AtPendingPayment();
        onboarding.Cancel("customer requested cancellation");
        return onboarding;
    }

    private static TenantOnboarding AtExpired()
    {
        var onboarding = AtPendingPayment();
        onboarding.MarkExpired();
        return onboarding;
    }

    private static TenantOnboarding AtRefunded()
    {
        var onboarding = AtManualReview();
        onboarding.MarkRefunded("cannot recover, refunding per support decision");
        return onboarding;
    }

    private static readonly Dictionary<TenantOnboardingStatus, Func<TenantOnboarding>> Builders = new()
    {
        [TenantOnboardingStatus.PendingPayment] = AtPendingPayment,
        [TenantOnboardingStatus.PaymentProcessing] = AtPaymentProcessing,
        [TenantOnboardingStatus.PaymentCompleted] = AtPaymentCompleted,
        [TenantOnboardingStatus.RegistrationPending] = AtRegistrationPending,
        [TenantOnboardingStatus.Provisioning] = AtProvisioning,
        [TenantOnboardingStatus.ProvisioningFailed] = AtProvisioningFailed,
        [TenantOnboardingStatus.ManualReview] = AtManualReview,
        [TenantOnboardingStatus.Completed] = AtCompleted,
        [TenantOnboardingStatus.PaymentFailed] = AtPaymentFailed,
        [TenantOnboardingStatus.Cancelled] = AtCancelled,
        [TenantOnboardingStatus.Expired] = AtExpired,
        [TenantOnboardingStatus.Refunded] = AtRefunded,
    };

    // ---------- Los 12 métodos que transicionan Status, y desde qué estado(s) tienen éxito real ----------
    // (derivado leyendo TenantOnboarding.cs método a método — no es una suposición sobre el diseño).

    private static readonly (
        string Name,
        Func<TenantOnboarding, Result> Invoke,
        TenantOnboardingStatus[] SucceedsFrom
    )[] Methods =
    {
        (
            "MarkPaymentProcessing",
            o => o.MarkPaymentProcessing(FixedPaymentId, FixedPaymentReference),
            new[] { TenantOnboardingStatus.PendingPayment, TenantOnboardingStatus.PaymentProcessing }
        ),
        (
            "MarkPaymentCompleted",
            o => o.MarkPaymentCompleted(FixedPaymentReference, Now),
            new[] { TenantOnboardingStatus.PaymentProcessing, TenantOnboardingStatus.PaymentCompleted }
        ),
        (
            "MarkPaymentFailed",
            o => o.MarkPaymentFailed("card_declined"),
            new[] { TenantOnboardingStatus.PaymentProcessing }
        ),
        (
            "SetRegistrationToken",
            o => o.SetRegistrationToken(FixedHash, Now.AddHours(72)),
            new[] { TenantOnboardingStatus.PaymentCompleted }
        ),
        (
            "StartProvisioning",
            o =>
                o.StartProvisioning(
                    "Matrix Tax Services",
                    "matrixtax",
                    FixedTermsVersionId,
                    FixedTermsHash,
                    "203.0.113.20",
                    "Mozilla/5.0",
                    Now
                ),
            new[] { TenantOnboardingStatus.RegistrationPending }
        ),
        (
            "MarkProvisioningFailed",
            o => o.MarkProvisioningFailed(TenantProvisioningStep.Tenant, "Tenant.DbUnavailable", "timeout"),
            new[] { TenantOnboardingStatus.Provisioning }
        ),
        (
            "ResumeProvisioning",
            o => o.ResumeProvisioning(),
            new[] { TenantOnboardingStatus.ProvisioningFailed, TenantOnboardingStatus.ManualReview }
        ),
        (
            "MarkManualReview",
            o => o.MarkManualReview("escalated after retries"),
            new[] { TenantOnboardingStatus.ProvisioningFailed }
        ),
        ("MarkCompleted", o => o.MarkCompleted(Now), new[] { TenantOnboardingStatus.Completed }),
        (
            "Cancel",
            o => o.Cancel("customer requested cancellation"),
            new[]
            {
                TenantOnboardingStatus.PendingPayment,
                TenantOnboardingStatus.PaymentProcessing,
                TenantOnboardingStatus.PaymentFailed,
            }
        ),
        (
            "MarkExpired",
            o => o.MarkExpired(),
            new[]
            {
                TenantOnboardingStatus.PendingPayment,
                TenantOnboardingStatus.PaymentProcessing,
                TenantOnboardingStatus.RegistrationPending,
            }
        ),
        (
            "MarkRefunded",
            o => o.MarkRefunded("cannot recover, refunding per support decision"),
            new[] { TenantOnboardingStatus.ProvisioningFailed, TenantOnboardingStatus.ManualReview }
        ),
    };

    public static IEnumerable<object[]> AllStateMethodCombinations()
    {
        foreach (var (status, builder) in Builders)
        foreach (var (name, invoke, succeedsFrom) in Methods)
            yield return new object[] { status, name, builder, invoke, succeedsFrom.Contains(status) };
    }

    public static IEnumerable<object[]> AllMethods() =>
        Methods.Select(m => new object[] { m.Name, m.Invoke, m.SucceedsFrom });

    // ---------- La matriz: 12 estados x 12 métodos = 144 casos ----------

    [Theory]
    [MemberData(nameof(AllStateMethodCombinations))]
    public void Method_from_state_matches_the_expected_outcome(
        TenantOnboardingStatus status,
        string methodName,
        Func<TenantOnboarding> builder,
        Func<TenantOnboarding, Result> invoke,
        bool expectSuccess
    )
    {
        var onboarding = builder();
        Assert.Equal(status, onboarding.Status);

        var result = invoke(onboarding);

        if (expectSuccess)
        {
            Assert.True(result.IsSuccess, $"{methodName} from {status} was expected to succeed but failed.");
        }
        else
        {
            Assert.True(result.IsFailure, $"{methodName} from {status} was expected to fail but succeeded.");
            Assert.Equal("Onboarding.InvalidState", result.Error.Code);
        }
    }

    // ---------- Idempotencia: cada uno de los 12 métodos, invocado 2 veces sobre la misma instancia ----------
    // desde su propio estado legal de origen, produce un resultado determinista (nunca una excepción).
    // El resultado esperado de la 2da llamada se deriva del mismo mapa SucceedsFrom: si el estado resultante
    // de la 1ra llamada también dispara éxito, la 2da es una repetición idempotente; si no, falla limpio con
    // "Onboarding.InvalidState" (esto documenta que no todos los métodos tienen réplica idempotente —
    // p.ej. MarkPaymentFailed/StartProvisioning no la necesitan porque nunca se reintentan sobre el mismo
    // evento — y confirma que ninguno corrompe el agregado ni lanza una excepción al reintentarse).

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void Method_called_twice_on_the_same_instance_is_deterministic(
        string methodName,
        Func<TenantOnboarding, Result> invoke,
        TenantOnboardingStatus[] succeedsFrom
    )
    {
        var onboarding = Builders[succeedsFrom[0]]();

        var first = invoke(onboarding);
        Assert.True(
            first.IsSuccess,
            $"{methodName}'s first call from its own legal start state was expected to succeed."
        );

        var statusAfterFirstCall = onboarding.Status;
        var second = invoke(onboarding);

        if (succeedsFrom.Contains(statusAfterFirstCall))
        {
            Assert.True(second.IsSuccess, $"{methodName} called twice was expected to be idempotent.");
        }
        else
        {
            Assert.True(second.IsFailure, $"{methodName} called twice was expected to fail cleanly on replay.");
            Assert.Equal("Onboarding.InvalidState", second.Error.Code);
        }
    }
}
