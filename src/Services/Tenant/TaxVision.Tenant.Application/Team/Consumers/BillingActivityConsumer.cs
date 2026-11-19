using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>
/// Facturas, siempre <see cref="ActivityVisibility.Restricted"/> (verlas exige <c>invoicing.view</c>). Sin montos.
/// Un cobro en línea no trae <c>RecordedByUserId</c>: no es acción de un miembro.
/// </summary>
public static class BillingActivityConsumer
{
    private const ActivityVisibility Restricted = ActivityVisibility.Restricted;

    public static Task Handle(
        InvoiceIssuedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.IssuedByUserId, ActivityVerbs.InvoiceIssued, evt.InvoiceId, evt.CustomerId, Restricted),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        InvoicePaidIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.RecordedByUserId, ActivityVerbs.InvoicePaymentRecorded, evt.InvoiceId, evt.CustomerId, Restricted),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        InvoiceVoidedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.VoidedByUserId, ActivityVerbs.InvoiceVoided, evt.InvoiceId, evt.CustomerId, Restricted),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        InvoiceSentToCustomerIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.SentByUserId, ActivityVerbs.InvoiceSent, evt.InvoiceId, evt.CustomerId, Restricted),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
