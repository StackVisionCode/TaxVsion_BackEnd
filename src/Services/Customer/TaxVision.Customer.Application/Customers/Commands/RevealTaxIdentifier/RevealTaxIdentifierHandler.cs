using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Audit;
using TaxVision.Customer.Domain.FiscalProfiles;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Commands.RevealTaxIdentifier;

public static class RevealTaxIdentifierHandler
{
    public static async Task<Result<RevealedTaxIdentifierResponse>> Handle(
        RevealTaxIdentifierCommand cmd,
        ICustomerRepository repository,
        ISensitiveDataProtector protector,
        ICustomerAuditWriter auditWriter,
        IUnitOfWork unitOfWork,
        ILogger<RevealTaxIdentifierCommand> logger,
        CancellationToken ct
    )
    {
        var customer = await LoadCustomerScopedAsync(cmd, repository, ct);
        if (customer is null)
        {
            await RecordAuditAsync(cmd, auditWriter, unitOfWork, outcome: "denied.not_found", ct);
            return Result.Failure<RevealedTaxIdentifierResponse>(new Error("Customer.NotFound", "Customer not found."));
        }

        if (customer.FiscalProfile is null)
        {
            await RecordAuditAsync(cmd, auditWriter, unitOfWork, outcome: "denied.no_fiscal_profile", ct);
            return Result.Failure<RevealedTaxIdentifierResponse>(
                new Error("FiscalProfile.NotFound", "This customer has no fiscal profile on file.")
            );
        }

        var plainTaxId = DecryptTaxIdentifier(customer.FiscalProfile, protector);
        var formatted = FormatForDisplay(plainTaxId, customer.FiscalProfile.SubjectKind);

        await RecordAuditAsync(cmd, auditWriter, unitOfWork, outcome: "granted", ct);

        return Result.Success(
            new RevealedTaxIdentifierResponse(cmd.CustomerId, customer.FiscalProfile.SubjectKind, formatted)
        );
    }

    // ============== Fase 1: cargar customer con validacion de tenant ==============

    private static async Task<CustomerEntity?> LoadCustomerScopedAsync(
        RevealTaxIdentifierCommand cmd,
        ICustomerRepository repository,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        return customer is null || customer.TenantId != cmd.TenantId ? null : customer;
    }

    // ============== Fase 2: desencriptar y formatear segun SubjectKind ==============

    private static string DecryptTaxIdentifier(
        CustomerFiscalProfile fiscalProfile,
        ISensitiveDataProtector protector
    ) => protector.Unprotect(fiscalProfile.TaxIdentifierCipher);

    private static string FormatForDisplay(string normalizedTaxId, FiscalSubjectKind kind) =>
        kind switch
        {
            // SSN/ITIN: 123-45-6789
            FiscalSubjectKind.Individual => $"{normalizedTaxId[..3]}-{normalizedTaxId[3..5]}-{normalizedTaxId[5..]}",
            // EIN: 12-3456789
            FiscalSubjectKind.Business => $"{normalizedTaxId[..2]}-{normalizedTaxId[2..]}",
            _ => normalizedTaxId,
        };

    // ============== Fase 3: audit trail — quien revelo (o intento revelar) que, cuando ==============

    private static async Task RecordAuditAsync(
        RevealTaxIdentifierCommand cmd,
        ICustomerAuditWriter auditWriter,
        IUnitOfWork unitOfWork,
        string outcome,
        CancellationToken ct
    )
    {
        var log = CustomerAuditLog.Create(
            cmd.TenantId,
            cmd.CustomerId,
            cmd.RequestedByUserId,
            CustomerAuditAction.FiscalProfileTaxIdentifierRevealed,
            outcome,
            cmd.IpAddress,
            cmd.UserAgent,
            cmd.CorrelationId,
            details: null,
            DateTime.UtcNow
        );
        await auditWriter.AddAsync(log, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
