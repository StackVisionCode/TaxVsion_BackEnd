using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.Payment.Application.Abstractions;

namespace TaxVision.Payment.Infrastructure.Payments;

public sealed class StripeGateway : IStripeGateway
{
    private readonly CustomerService _customerService;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly string _webhookSecret;

    public StripeGateway(IOptions<StripeOptions> options)
    {
        var secretKey = options.Value.SecretKey;
        _webhookSecret = options.Value.WebhookSecret;

        var client = new StripeClient(secretKey);
        _customerService = new CustomerService(client);
        _paymentIntentService = new PaymentIntentService(client);
    }

    public async Task<string> GetOrCreateCustomerAsync(Guid tenantId, string email, CancellationToken ct = default)
    {
        // Search for existing customer by metadata
        var searchOptions = new CustomerSearchOptions
        {
            Query = $"metadata['tenantId']:'{tenantId}'"
        };
        var searchResult = await _customerService.SearchAsync(searchOptions, cancellationToken: ct);
        var existing = searchResult.Data.FirstOrDefault();
        if (existing is not null)
            return existing.Id;

        // Create new customer
        var createOptions = new CustomerCreateOptions
        {
            Email = email,
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId.ToString()
            }
        };
        var customer = await _customerService.CreateAsync(createOptions, cancellationToken: ct);
        return customer.Id;
    }

    public async Task<StripePaymentResult> CreatePaymentIntentAsync(
        string stripeCustomerId,
        long amountCents,
        string currency,
        string description,
        CancellationToken ct = default)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = currency.ToLowerInvariant(),
                Customer = stripeCustomerId,
                Description = description,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                },
                Confirm = false
            };
            var intent = await _paymentIntentService.CreateAsync(options, cancellationToken: ct);
            return new StripePaymentResult(true, intent.Id, null);
        }
        catch (StripeException ex)
        {
            return new StripePaymentResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<StripePaymentResult> ConfirmPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken ct = default)
    {
        try
        {
            var options = new PaymentIntentConfirmOptions
            {
                PaymentMethod = "pm_card_visa" // Test payment method — in production this comes from client
            };
            var intent = await _paymentIntentService.ConfirmAsync(paymentIntentId, options, cancellationToken: ct);
            var success = intent.Status is "succeeded" or "processing";
            return new StripePaymentResult(success, intent.Id, success ? null : $"Unexpected intent status: {intent.Status}");
        }
        catch (StripeException ex)
        {
            return new StripePaymentResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public Task<bool> VerifyWebhookSignatureAsync(string payload, string signature, CancellationToken ct = default)
    {
        try
        {
            EventUtility.ConstructEvent(payload, signature, _webhookSecret);
            return Task.FromResult(true);
        }
        catch (StripeException)
        {
            return Task.FromResult(false);
        }
    }
}
