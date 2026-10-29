using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
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

        // Editar sin re-enviar el identificador: cuando el tax id viene vacío y YA existe un perfil,
        // se actualizan solo los campos no-sensibles (filing/AGI/returning) y el banco si se envió —
        // el cifrado/blind-index/last4 quedan intactos. Crear un perfil SÍ exige identificador.
        if (string.IsNullOrWhiteSpace(cmd.TaxIdentifier))
        {
            if (customer.FiscalProfile is null)
                return Result.Failure<CustomerFiscalProfileResponse>(
                    new Error(
                        "FiscalProfile.TaxIdRequired",
                        "A tax identifier is required to create the fiscal profile."
                    )
                );

            var (keepBankAccount, keepBankRouting) = EncryptBankingIfPresent(
                cmd.RefundBankAccount,
                cmd.RefundBankRouting,
                protector
            );

            var updateResult = customer.UpdateFiscalProfile(
                filingStatus: cmd.FilingStatus,
                priorYearAgi: cmd.PriorYearAgi,
                isReturningCustomer: cmd.IsReturningCustomer,
                refundBankAccountCipher: keepBankAccount,
                refundBankRoutingCipher: keepBankRouting,
                byUserId: cmd.ModifiedByUserId
            );
            if (updateResult.IsFailure)
                return Result.Failure<CustomerFiscalProfileResponse>(updateResult.Error);

            await unitOfWork.SaveChangesAsync(ct);
            LogAudit(logger, cmd);
            return Result.Success(MapResponse(customer, cmd.CustomerId));
        }

        var taxIdValidation = FiscalTaxIdentifierService.ValidateAndNormalize(cmd.TaxIdentifier, cmd.SubjectKind);
        if (taxIdValidation.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(taxIdValidation.Error);

        var encrypted = FiscalTaxIdentifierService.Encrypt(taxIdValidation.Value, cmd.TenantId, protector);
        var (bankAccountCipher, bankRoutingCipher) = EncryptBankingIfPresent(
            cmd.RefundBankAccount,
            cmd.RefundBankRouting,
            protector
        );

        var uniquenessCheck = await FiscalTaxIdentifierService.EnsureUniqueAsync(
            repository,
            cmd.TenantId,
            encrypted.BlindIndexCandidates,
            cmd.CustomerId,
            ct
        );
        if (uniquenessCheck.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(uniquenessCheck.Error);

        var setResult = customer.SetFiscalProfile(
            subjectKind: cmd.SubjectKind,
            taxIdentifierCipher: encrypted.Cipher,
            taxIdentifierBlindIndex: encrypted.BlindIndex,
            taxIdentifierLast4: encrypted.Last4,
            filingStatus: cmd.FilingStatus,
            priorYearAgi: cmd.PriorYearAgi,
            isReturningCustomer: cmd.IsReturningCustomer,
            refundBankAccountCipher: bankAccountCipher,
            refundBankRoutingCipher: bankRoutingCipher,
            byUserId: cmd.ModifiedByUserId,
            taxIdentifierKeyVersion: encrypted.KeyVersion
        );

        if (setResult.IsFailure)
            return Result.Failure<CustomerFiscalProfileResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        LogAudit(logger, cmd);

        return Result.Success(MapResponse(customer, cmd.CustomerId));
    }

    private static async Task<CustomerEntity?> LoadCustomerScopedAsync(
        SetCustomerFiscalProfileCommand cmd,
        ICustomerRepository repository,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        return customer is null || customer.TenantId != cmd.TenantId ? null : customer;
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

    private static void LogAudit(ILogger logger, SetCustomerFiscalProfileCommand cmd) =>
        logger.LogInformation(
            "Fiscal profile updated for customer {CustomerId} in tenant {TenantId} by user {UserId}",
            cmd.CustomerId,
            cmd.TenantId,
            cmd.ModifiedByUserId
        );

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
