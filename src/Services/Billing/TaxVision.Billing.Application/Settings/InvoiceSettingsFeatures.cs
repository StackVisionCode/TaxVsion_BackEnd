using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Settings;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Settings;

/// <summary>Ajustes de facturación del tenant. <c>PaymentTerms</c> va como nombre del enum (ej. "Net30").</summary>
public sealed record InvoiceSettingsResponse(string PaymentTerms, string? PaymentInstructions, string? InvoiceFooter)
{
    public static InvoiceSettingsResponse From(TenantBillingSettings? settings)
    {
        var snapshot = settings?.ToSnapshot() ?? InvoiceTermsSnapshot.Default;
        return new(snapshot.PaymentTerms.ToString(), snapshot.PaymentInstructions, snapshot.InvoiceFooter);
    }
}

// ---------------- Get ----------------
public sealed record GetInvoiceSettingsQuery(Guid TenantId);

public static class GetInvoiceSettingsHandler
{
    /// <summary>Sin fila guardada devuelve los valores por defecto (vence al recibirla), nunca 404.</summary>
    public static async Task<Result<InvoiceSettingsResponse>> Handle(
        GetInvoiceSettingsQuery query,
        ITenantBillingSettingsRepository settings,
        CancellationToken ct
    ) => Result.Success(InvoiceSettingsResponse.From(await settings.GetByTenantAsync(query.TenantId, ct)));
}

// ---------------- Update (estado completo) ----------------
public sealed record UpdateInvoiceSettingsCommand(
    Guid TenantId,
    string? PaymentTerms,
    string? PaymentInstructions,
    string? InvoiceFooter
);

public static class UpdateInvoiceSettingsHandler
{
    public static async Task<Result<InvoiceSettingsResponse>> Handle(
        UpdateInvoiceSettingsCommand command,
        ITenantBillingSettingsRepository repository,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!PaymentTermsExtensions.TryParse(command.PaymentTerms, out var terms))
            return Result.Failure<InvoiceSettingsResponse>(TenantBillingSettings.PaymentTermsInvalid);

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var settings = await repository.GetByTenantAsync(command.TenantId, ct);
        var isNew = settings is null;
        settings ??= TenantBillingSettings.Create(command.TenantId, nowUtc);

        // El agregado valida los largos; un 400 no deja una fila nueva a medias.
        var updated = settings.Update(terms, command.PaymentInstructions, command.InvoiceFooter, nowUtc);
        if (updated.IsFailure)
            return Result.Failure<InvoiceSettingsResponse>(updated.Error);

        if (isNew)
            await repository.AddAsync(settings, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(InvoiceSettingsResponse.From(settings));
    }
}
