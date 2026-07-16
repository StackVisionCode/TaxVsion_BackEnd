using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Connect;

/// <summary>
/// Proyección local de la Connected Account de Stripe para un tenant — único por
/// <c>(TenantId, ProviderCode)</c>. El Domain nunca importa el SDK de Stripe: esta clase solo
/// conoce el <see cref="StripeConnectAccountId"/> opaco y el estado que
/// <see cref="UpdateFromWebhook"/> recibe ya traducido desde Infrastructure
/// (<c>StripeConnectWebhookController</c>/<c>ProcessConnectWebhookHandler</c>).
/// </summary>
public sealed class TenantConnectAccount : TenantEntity
{
    public PaymentProviderCode ProviderCode { get; private set; }
    public StripeConnectAccountId StripeConnectAccountId { get; private set; } = null!;
    public ConnectAccountType AccountType { get; private set; }
    public ConnectAccountStatus Status { get; private set; }
    public OnboardingStep OnboardingStep { get; private set; }
    public bool CanCharge { get; private set; }
    public bool CanReceivePayouts { get; private set; }
    public IReadOnlyList<string> RequirementsCurrentlyDue { get; private set; } = [];
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private TenantConnectAccount() { }

    /// <summary>Se llama justo después de que Infrastructure crea la Account en Stripe — el
    /// id ya viene resuelto, nunca se genera acá (el Domain no habla con Stripe).</summary>
    public static Result<TenantConnectAccount> Create(
        Guid tenantId, PaymentProviderCode providerCode, ConnectAccountType accountType, StripeConnectAccountId stripeConnectAccountId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantConnectAccount>(new Error("TenantConnectAccount.InvalidTenant", "TenantId is required."));

        var account = new TenantConnectAccount
        {
            ProviderCode = providerCode,
            AccountType = accountType,
            StripeConnectAccountId = stripeConnectAccountId,
            Status = ConnectAccountStatus.Pending,
            OnboardingStep = OnboardingStep.NotStarted,
            CanCharge = false,
            CanReceivePayouts = false,
            RequirementsCurrentlyDue = [],
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        account.SetTenant(tenantId);
        return Result.Success(account);
    }

    /// <summary>Marca que se (re)generó un <c>AccountLink</c> hosted de Stripe — los links
    /// expiran, así que este método puede llamarse más de una vez mientras el tenant no
    /// termine el formulario.</summary>
    public Result InitiateOnboarding(DateTime nowUtc)
    {
        if (Status is ConnectAccountStatus.Disabled)
            return Result.Failure(new Error("TenantConnectAccount.InvalidTransition", "Cannot onboard a disabled Connect account."));

        if (Status == ConnectAccountStatus.Pending)
            Status = ConnectAccountStatus.InProgress;

        OnboardingStep = OnboardingStep.LinkGenerated;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Traduce <c>account.updated</c>/<c>capability.updated</c> ya parseados — la
    /// máquina de estados sigue el diagrama del diseño (§20.5): Enabled requiere cobrar
    /// habilitado y cero requirements pendientes; perder <c>chargesEnabled</c> desde Enabled
    /// degrada a Restricted; recuperarlo vuelve a Enabled. <c>Disabled</c> es terminal — solo
    /// <see cref="Deactivate"/> (acción de admin) lo alcanza.</summary>
    public Result UpdateFromWebhook(bool chargesEnabled, bool payoutsEnabled, IReadOnlyList<string> requirementsCurrentlyDue, DateTime nowUtc)
    {
        if (Status == ConnectAccountStatus.Disabled)
            return Result.Failure(new Error("TenantConnectAccount.InvalidTransition", "A disabled Connect account cannot be updated by webhook."));

        CanCharge = chargesEnabled;
        CanReceivePayouts = payoutsEnabled;
        RequirementsCurrentlyDue = requirementsCurrentlyDue;

        Status = (Status, chargesEnabled, requirementsCurrentlyDue.Count) switch
        {
            (ConnectAccountStatus.Restricted, true, _) => ConnectAccountStatus.Enabled,
            (ConnectAccountStatus.Enabled, false, _) => ConnectAccountStatus.Restricted,
            (_, true, 0) => ConnectAccountStatus.Enabled,
            _ => Status,
        };

        if (Status == ConnectAccountStatus.Enabled)
            OnboardingStep = OnboardingStep.Completed;

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantConnectAccount.InvalidReason", "Reason is required."));

        if (Status == ConnectAccountStatus.Disabled)
            return Result.Failure(new Error("TenantConnectAccount.InvalidTransition", "Connect account is already disabled."));

        Status = ConnectAccountStatus.Disabled;
        CanCharge = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
