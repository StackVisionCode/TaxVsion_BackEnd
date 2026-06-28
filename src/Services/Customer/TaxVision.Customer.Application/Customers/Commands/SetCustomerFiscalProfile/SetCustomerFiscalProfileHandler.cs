using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Customers.Commands.SetCustomerFiscalProfile;

public static class SetCustomerFiscalProfileHandler
{
    public static async Task<Result<CustomerFiscalProfileResponse>> Handle(
        SetCustomerFiscalProfileCommand cmd,
        ICustomerRepository repository,
        ISensitiveDataProtector protector,
        IUnitOfWork unitOfWork,
        ILogger<SetCustomerFiscalProfileCommand> logger,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<CustomerFiscalProfileResponse>(new Error("Customer.NotFound", "Customer not found."));

        var normalizedIdent = NormalizeTaxIdentifier(cmd.TaxIdentifier);
        if (!IsValidTaxIdentifier(normalizedIdent, cmd.SubjectKind))
            return Result.Failure<CustomerFiscalProfileResponse>(
                new Error("FiscalProfile.TaxId", "Tax identifier format is invalid for the subject kind.")
            );

        var cipher = protector.Protect(normalizedIdent);
        var blindIndex = protector.ComputeBlindIndex(normalizedIdent, cmd.TenantId);
        var last4 = normalizedIdent[^4..];

        byte[]? bankAccountCipher = null;
        byte[]? bankRoutingCipher = null;
        if (!string.IsNullOrWhiteSpace(cmd.RefundBankAccount) && !string.IsNullOrWhiteSpace(cmd.RefundBankRouting))
        {
            bankAccountCipher = protector.Protect(cmd.RefundBankAccount.Trim());
            bankRoutingCipher = protector.Protect(cmd.RefundBankRouting.Trim());
        }

        var setResult = customer.SetFiscalProfile(
            subjectKind: cmd.SubjectKind,
            taxIdentifierCipher: cipher,
            taxIdentifierBlindIndex: blindIndex,
            taxIdentifierLast4: last4,
            filingStatus: cmd.FilingStatus,
            priorYearAgi: cmd.PriorYearAgi,
            isReturningCustomer: cmd.IsReturningCustomer,
            refundBankAccountCipher: bankAccountCipher,
            refundBankRoutingCipher: bankRoutingCipher,
            byUserId: cmd.ModifiedByUserId
        );

        if (setResult.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // Audit minimo (sin PII). Identificador y datos bancarios JAMAS al log.
        logger.LogInformation(
            "Fiscal profile updated for customer {CustomerId} in tenant {TenantId} by user {UserId}",
            cmd.CustomerId,
            cmd.TenantId,
            cmd.ModifiedByUserId
        );

        var fp = customer.FiscalProfile!;
        return Result.Success(
            new CustomerFiscalProfileResponse(
                CustomerId: cmd.CustomerId,
                SubjectKind: fp.SubjectKind,
                TaxIdentifierLast4: fp.TaxIdentifierLast4,
                FilingStatus: fp.FilingStatus,
                PriorYearAgi: fp.PriorYearAgi,
                IsReturningCustomer: fp.IsReturningCustomer,
                HasRefundBankInfo: fp.RefundBankAccountCipher is not null,
                UpdatedAtUtc: fp.UpdatedAtUtc,
                UpdatedByUserId: fp.UpdatedByUserId
            )
        );
    }

    private static string NormalizeTaxIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        // Conservar solo dígitos (quitar guiones, espacios)
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    private static bool IsValidTaxIdentifier(string normalized, FiscalSubjectKind kind) =>
        kind switch
        {
            // SSN: 9 dígitos, no empieza con 9 (eso es ITIN), no 000/666 en primer grupo
            FiscalSubjectKind.Individual => normalized.Length == 9
                && !normalized.StartsWith("000")
                && !normalized.StartsWith("666"),
            // EIN: 9 dígitos
            FiscalSubjectKind.Business => normalized.Length == 9,
            _ => false,
        };
}
