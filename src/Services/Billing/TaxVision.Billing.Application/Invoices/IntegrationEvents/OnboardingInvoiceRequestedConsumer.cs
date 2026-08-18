using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Gift/Referral en onboarding — Billing es la fuente de verdad financiera: asienta UNA factura por
/// cada operación comercial (pago normal, descuento parcial o cubierta 100% con total $0), a pedido de
/// Auth (FINALIZE). La factura nace bajo <c>PlatformTenant.Id</c> (pre-tenant) y luego se re-hospeda al
/// tenant real (<see cref="OnboardingInvoiceBackfillConsumer"/>). Idempotente por OnboardingId (índice
/// único + chequeo de existencia). Tras crearla dispara el pipeline de PDF existente (Documents solo
/// renderiza). IntegrationEventTenantMiddleware ya restauró el tenant (PlatformTenant.Id) del sobre.
/// </summary>
public static class OnboardingInvoiceRequestedConsumer
{
    public static async Task Handle(
        OnboardingInvoiceRequestedIntegrationEvent evt,
        IInvoiceRepository invoices,
        IInvoiceNumberSequenceRepository sequences,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<PlatformIssuerOptions> platformIssuer,
        TimeProvider clock,
        ICorrelationContext correlation,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        // Idempotencia: una sola factura por onboarding (reproceso del bus = no-op).
        var existing = await invoices.GetByOnboardingIdAsync(evt.OnboardingId, ct);
        if (existing is not null)
            return;

        if (!Enum.TryParse<SettlementType>(evt.SettlementType, ignoreCase: true, out var settlement))
        {
            logger.LogWarning(
                "OnboardingInvoiceRequested for {OnboardingId} has invalid SettlementType '{Settlement}'; ignoring.",
                evt.OnboardingId,
                evt.SettlementType
            );
            return;
        }

        var adjustments = new List<OnboardingInvoiceAdjustment>();
        foreach (var a in evt.Adjustments)
        {
            if (!Enum.TryParse<InvoiceAdjustmentType>(a.Type, ignoreCase: true, out var type))
            {
                logger.LogWarning(
                    "OnboardingInvoiceRequested for {OnboardingId} has invalid adjustment type '{Type}'; ignoring.",
                    evt.OnboardingId,
                    a.Type
                );
                return;
            }
            adjustments.Add(new OnboardingInvoiceAdjustment(type, a.Code, a.GrowthReservationId, a.AmountCents));
        }

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var periodKey = nowUtc.Year.ToString(CultureInfo.InvariantCulture);

        // Secuencia de onboarding propia, bajo PlatformTenant.Id (distinta del INV por tenant).
        var sequence = await sequences.GetOrCreateAsync(PlatformTenant.Id, $"ONB-{periodKey}", ct);
        var number = sequence.Allocate();
        var invoiceNumber = $"ONB-{periodKey}-{number:D5}";

        var customer = new CustomerSnapshot(evt.OnboardingId, evt.PayerName, evt.PayerEmail, null, null, null);

        var result = Invoice.CreateForOnboarding(
            PlatformTenant.Id,
            evt.OnboardingId,
            evt.PlanId,
            evt.PaymentId,
            invoiceNumber,
            customer,
            platformIssuer.Value.ToSnapshot(),
            evt.PlanDescription,
            evt.GrossAmountCents,
            evt.DiscountAmountCents,
            evt.NetAmountCents,
            evt.Currency,
            settlement,
            adjustments,
            nowUtc,
            nowUtc
        );
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not create onboarding invoice for {OnboardingId}: {Code} - {Message}",
                evt.OnboardingId,
                result.Error.Code,
                result.Error.Message
            );
            return;
        }

        await invoices.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Pipeline de PDF existente (bajo PlatformTenant.Id; el contenido no depende del tenant dueño).
        bus.TenantId = PlatformTenant.Id.ToString();
        await bus.PublishAsync(new GenerateInvoicePdfCommand(PlatformTenant.Id, result.Value.Id));

        logger.LogInformation(
            "Onboarding invoice {Number} created for {OnboardingId} ({Settlement}, net {Net} {Currency}).",
            invoiceNumber,
            evt.OnboardingId,
            settlement,
            evt.NetAmountCents,
            evt.Currency
        );
    }
}

/// <summary>Identidad del emisor de las facturas de onboarding = la plataforma (aún no hay tenant).
/// Config <c>Billing:PlatformIssuer</c>. Defaults razonables para dev.</summary>
public sealed class PlatformIssuerOptions
{
    public const string SectionName = "Billing:PlatformIssuer";

    public string Name { get; set; } = "TaxVision";
    public string AddressLine1 { get; set; } = "N/A";
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Zip { get; set; } = "00000";
    public string Country { get; set; } = "US";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? TaxId { get; set; }

    public IssuerSnapshot ToSnapshot() =>
        new(
            Name,
            new Address(AddressLine1, AddressLine2, City, State, Zip, Country),
            Phone,
            Email,
            Website,
            LogoFileId: null,
            TaxId
        );
}
