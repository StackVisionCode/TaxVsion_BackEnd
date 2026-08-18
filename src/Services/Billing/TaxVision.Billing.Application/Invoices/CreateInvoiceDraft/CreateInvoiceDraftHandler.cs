using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;

/// <summary>Crea y persiste el borrador. Handler estático con inyección por método (Wolverine).</summary>
public static class CreateInvoiceDraftHandler
{
    public static async Task<Result<CreateInvoiceDraftResult>> Handle(
        CreateInvoiceDraftCommand command,
        IInvoiceRepository invoices,
        IIssuerProfileRepository issuerProfiles,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;

        var customer = new CustomerSnapshot(
            command.Customer.CustomerId,
            command.Customer.Name,
            command.Customer.Email,
            command.Customer.Phone,
            command.Customer.TaxId,
            ToAddress(command.Customer.Billing)
        );

        // Emisor: si el caller manda uno explícito, se usa; si no, se estampa el PERFIL de empresa del
        // tenant guardado en el backend (así el PDF sale con los datos de la empresa sin reenviarlos).
        IssuerSnapshot? issuer;
        if (command.Issuer is not null)
        {
            issuer = new IssuerSnapshot(
                command.Issuer.Name,
                ToAddress(command.Issuer.Address)!,
                command.Issuer.Phone,
                command.Issuer.Email,
                command.Issuer.Website,
                command.Issuer.LogoFileId,
                command.Issuer.TaxId
            );
        }
        else
        {
            var profile = await issuerProfiles.GetByTenantAsync(command.TenantId, ct);
            issuer = profile is { IsUsable: true } ? profile.ToSnapshot() : null;
        }

        var lines = command
            .Lines.Select(l => new DraftInvoiceLine(
                l.Description,
                l.Quantity,
                l.UnitAmountCents,
                l.TaxBasisPoints,
                l.CatalogItemId
            ))
            .ToList();

        var result = Invoice.CreateDraft(
            command.TenantId,
            command.ActorUserId,
            customer,
            command.Currency,
            lines,
            command.Notes,
            nowUtc,
            issuer
        );
        if (result.IsFailure)
            return Result.Failure<CreateInvoiceDraftResult>(result.Error);

        var invoice = result.Value;
        await invoices.AddAsync(invoice, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new CreateInvoiceDraftResult(invoice.Id, invoice.Status.ToString()));
    }

    private static Address? ToAddress(InvoiceAddressInput? input) =>
        input is null ? null : new Address(input.Line1, input.Line2, input.City, input.State, input.Zip, input.Country);
}
