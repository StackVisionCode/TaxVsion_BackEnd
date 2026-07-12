using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.FiscalProfiles;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

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
        var customer = await LoadCustomerScopedAsync(cmd, repository, ct);
        if (customer is null)
            return Result.Failure<CustomerFiscalProfileResponse>(new Error("Customer.NotFound", "Customer not found."));

        var taxIdValidation = ValidateAndNormalizeTaxIdentifier(cmd.TaxIdentifier, cmd.SubjectKind);
        if (taxIdValidation.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(taxIdValidation.Error);

        var encryption = EncryptSensitiveFields(taxIdValidation.Value, cmd, protector);

        var uniquenessCheck = await EnsureNoBlindIndexConflictAsync(
            repository,
            cmd.TenantId,
            encryption.TaxIdBlindIndex,
            cmd.CustomerId,
            ct
        );
        if (uniquenessCheck.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(uniquenessCheck.Error);

        var setResult = customer.SetFiscalProfile(
            subjectKind: cmd.SubjectKind,
            taxIdentifierCipher: encryption.TaxIdCipher,
            taxIdentifierBlindIndex: encryption.TaxIdBlindIndex,
            taxIdentifierLast4: encryption.TaxIdLast4,
            filingStatus: cmd.FilingStatus,
            priorYearAgi: cmd.PriorYearAgi,
            isReturningCustomer: cmd.IsReturningCustomer,
            refundBankAccountCipher: encryption.BankAccountCipher,
            refundBankRoutingCipher: encryption.BankRoutingCipher,
            byUserId: cmd.ModifiedByUserId
        );

        if (setResult.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        LogAudit(logger, cmd);

        return Result.Success(MapResponse(customer, cmd.CustomerId));
    }

    // ============== Fase 1: cargar customer con validacion de tenant ==============

    private static async Task<CustomerEntity?> LoadCustomerScopedAsync(
        SetCustomerFiscalProfileCommand cmd,
        ICustomerRepository repository,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        return customer is null || customer.TenantId != cmd.TenantId ? null : customer;
    }

    // ============== Fase 2: validar y normalizar el tax identifier ==============

    private static Result<string> ValidateAndNormalizeTaxIdentifier(string raw, FiscalSubjectKind kind)
    {
        var normalized = NormalizeTaxIdentifier(raw);
        if (!IsValidTaxIdentifier(normalized, kind))
            return Result.Failure<string>(
                new Error("FiscalProfile.TaxId", "Tax identifier format is invalid for the subject kind.")
            );
        return Result.Success(normalized);
    }

    private static string NormalizeTaxIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    private static bool IsValidTaxIdentifier(string normalized, FiscalSubjectKind kind) =>
        kind switch
        {
            // SSN/ITIN: 9 digitos, no empieza con 000 ni 666
            FiscalSubjectKind.Individual => normalized.Length == 9
                && !normalized.StartsWith("000")
                && !normalized.StartsWith("666"),
            // EIN: 9 digitos
            FiscalSubjectKind.Business => normalized.Length == 9,
            _ => false,
        };

    // ============== Fase 3: cifrar SSN/EIN + banking (opcional) ==============

    private sealed record EncryptedFields(
        byte[] TaxIdCipher,
        string TaxIdBlindIndex,
        string TaxIdLast4,
        byte[]? BankAccountCipher,
        byte[]? BankRoutingCipher
    );

    private static EncryptedFields EncryptSensitiveFields(
        string normalizedTaxId,
        SetCustomerFiscalProfileCommand cmd,
        ISensitiveDataProtector protector
    )
    {
        var cipher = protector.Protect(normalizedTaxId);
        var blindIndex = protector.ComputeBlindIndex(normalizedTaxId, cmd.TenantId);
        var last4 = normalizedTaxId[^4..];

        var (bankAccountCipher, bankRoutingCipher) = EncryptBankingIfPresent(
            cmd.RefundBankAccount,
            cmd.RefundBankRouting,
            protector
        );

        return new EncryptedFields(cipher, blindIndex, last4, bankAccountCipher, bankRoutingCipher);
    }

    private static (byte[]? account, byte[]? routing) EncryptBankingIfPresent(
        string? account,
        string? routing,
        ISensitiveDataProtector protector
    )
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(routing))
            return (null, null);

        return (protector.Protect(account.Trim()), protector.Protect(routing.Trim()));
    }

    // ============== Fase 4: verificar unicidad del blind index en el tenant ==============

    private static async Task<Result> EnsureNoBlindIndexConflictAsync(
        ICustomerRepository repository,
        Guid tenantId,
        string blindIndex,
        Guid currentCustomerId,
        CancellationToken ct
    )
    {
        var conflictingCustomerId = await repository.FindCustomerIdByFiscalBlindIndexAsync(
            tenantId,
            blindIndex,
            excludeCustomerId: currentCustomerId,
            ct
        );

        if (conflictingCustomerId is not null)
            return Result.Failure(
                new Error(
                    "FiscalProfile.TaxIdentifierAlreadyExists",
                    "Another customer in this tenant already has this tax identifier."
                )
            );

        return Result.Success();
    }

    // ============== Fase 5: audit log (sin PII) ==============

    private static void LogAudit(ILogger logger, SetCustomerFiscalProfileCommand cmd) =>
        logger.LogInformation(
            "Fiscal profile updated for customer {CustomerId} in tenant {TenantId} by user {UserId}",
            cmd.CustomerId,
            cmd.TenantId,
            cmd.ModifiedByUserId
        );

    // ============== Mapeo aggregate -> response ==============

    private static CustomerFiscalProfileResponse MapResponse(CustomerEntity customer, Guid customerId)
    {
        var fp = customer.FiscalProfile!;
        return new CustomerFiscalProfileResponse(
            CustomerId: customerId,
            SubjectKind: fp.SubjectKind,
            TaxIdentifierLast4: fp.TaxIdentifierLast4,
            FilingStatus: fp.FilingStatus,
            PriorYearAgi: fp.PriorYearAgi,
            IsReturningCustomer: fp.IsReturningCustomer,
            HasRefundBankInfo: fp.RefundBankAccountCipher is not null,
            UpdatedAtUtc: fp.UpdatedAtUtc,
            UpdatedByUserId: fp.UpdatedByUserId
        );
    }
}
