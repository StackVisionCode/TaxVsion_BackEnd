using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.ProviderCustomers;

/// <summary>
/// Representa al tenant como "customer" en un provider — un registro por
/// <c>(TenantId, ProviderCode)</c>. Guarda los métodos de pago tokenizados que el tenant
/// adjuntó, para que un cobro automático (renovación, dunning) no dependa de que el
/// tenant esté presente en el checkout. Se provisiona eager al alta del tenant
/// (<c>TenantCreatedConsumer</c>, §D.4) o lazy en el primer <see cref="AttachPaymentMethod"/>.
/// </summary>
public sealed class TenantProviderCustomer : TenantEntity
{
    private readonly List<TenantSavedPaymentMethod> _savedMethods = [];

    public PaymentProviderCode ProviderCode { get; private set; }
    public ProviderCustomerReference CustomerReference { get; private set; } = null!;
    public string Email { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<TenantSavedPaymentMethod> SavedMethods => _savedMethods;

    private TenantProviderCustomer() { }

    public static Result<TenantProviderCustomer> Register(
        Guid tenantId, PaymentProviderCode providerCode, ProviderCustomerReference customerReference, string email, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantProviderCustomer>(new Error("TenantProviderCustomer.InvalidTenant", "TenantId is required."));

        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<TenantProviderCustomer>(new Error("TenantProviderCustomer.InvalidEmail", "Email is required."));

        var customer = new TenantProviderCustomer
        {
            ProviderCode = providerCode,
            CustomerReference = customerReference,
            Email = email.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        customer.SetTenant(tenantId);
        return Result.Success(customer);
    }

    /// <summary>Adjunta un método tokenizado nuevo. El primero que se adjunta siempre queda
    /// como default — no tiene sentido un customer sin default cuando solo tiene un método.</summary>
    public Result<Guid> AttachPaymentMethod(
        string methodReference, string brand, string last4, int expMonth, int expYear, bool setAsDefault, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(methodReference))
            return Result.Failure<Guid>(new Error("TenantProviderCustomer.InvalidMethodReference", "MethodReference is required."));

        if (FindActiveByReference(methodReference) is not null)
            return Result.Failure<Guid>(new Error("TenantProviderCustomer.MethodAlreadyAttached", "This payment method is already attached."));

        var makeDefault = setAsDefault || !HasAnyActiveMethod();
        if (makeDefault)
            ClearDefault();

        var method = TenantSavedPaymentMethod.Create(Id, TenantId, methodReference, brand, last4, expMonth, expYear, makeDefault, nowUtc);
        _savedMethods.Add(method);
        Touch(nowUtc);
        return Result.Success(method.Id);
    }

    public Result DetachPaymentMethod(Guid methodId, DateTime nowUtc)
    {
        var method = FindActiveById(methodId);
        if (method is null)
            return Result.Failure(new Error("TenantProviderCustomer.MethodNotFound", "Payment method does not exist or was already detached."));

        var wasDefault = method.IsDefault;
        method.MarkDetached(nowUtc);
        Touch(nowUtc);

        if (wasDefault)
            PromoteNextAsDefault(nowUtc);

        return Result.Success();
    }

    public Result MarkPaymentMethodAsDefault(Guid methodId, DateTime nowUtc)
    {
        var method = FindActiveById(methodId);
        if (method is null)
            return Result.Failure(new Error("TenantProviderCustomer.MethodNotFound", "Payment method does not exist or was detached."));

        ClearDefault();
        method.SetDefault(true);
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>El que usa un cobro automático cuando no se especifica un método puntual.</summary>
    public TenantSavedPaymentMethod? GetDefaultMethod()
    {
        foreach (var method in _savedMethods)
        {
            if (!method.IsDetached && method.IsDefault)
                return method;
        }

        return null;
    }

    private void PromoteNextAsDefault(DateTime nowUtc)
    {
        foreach (var method in _savedMethods)
        {
            if (!method.IsDetached)
            {
                method.SetDefault(true);
                Touch(nowUtc);
                return;
            }
        }
    }

    private void ClearDefault()
    {
        foreach (var method in _savedMethods)
        {
            if (method.IsDefault)
                method.SetDefault(false);
        }
    }

    private bool HasAnyActiveMethod()
    {
        foreach (var method in _savedMethods)
        {
            if (!method.IsDetached)
                return true;
        }

        return false;
    }

    private TenantSavedPaymentMethod? FindActiveById(Guid methodId)
    {
        foreach (var method in _savedMethods)
        {
            if (method.Id == methodId && !method.IsDetached)
                return method;
        }

        return null;
    }

    private TenantSavedPaymentMethod? FindActiveByReference(string methodReference)
    {
        foreach (var method in _savedMethods)
        {
            if (!method.IsDetached && method.MethodReference == methodReference)
                return method;
        }

        return null;
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
