using BuildingBlocks.Results;

namespace TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;

/// <summary>Handler estático con inyección por método (convención Wolverine). SCAFFOLD B1:
/// devuelve NotImplemented; la lógica real se implementa en B2.</summary>
public static class CreateInvoiceDraftHandler
{
    public static Task<Result<CreateInvoiceDraftResult>> Handle(
        CreateInvoiceDraftCommand command,
        CancellationToken ct
    )
    {
        _ = command;
        _ = ct;
        return Task.FromResult(
            Result.Failure<CreateInvoiceDraftResult>(
                new Error("Billing.NotImplemented", "CreateInvoiceDraft is scaffolded; implemented in phase B2.")
            )
        );
    }
}
