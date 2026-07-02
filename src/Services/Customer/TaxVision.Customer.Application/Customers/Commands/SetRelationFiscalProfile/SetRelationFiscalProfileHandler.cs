using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Application.Customers.Commands.SetRelationFiscalProfile;

public static class SetRelationFiscalProfileHandler
{
    public static async Task<Result<RelationFiscalProfileResponse>> Handle(
        SetRelationFiscalProfileCommand cmd,
        ICustomerRepository repository,
        ISensitiveDataProtector protector,
        IUnitOfWork unitOfWork,
        ILogger<SetRelationFiscalProfileCommand> logger,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<RelationFiscalProfileResponse>(new Error("Customer.NotFound", "Customer not found."));

        var relation = customer.Relations.FirstOrDefault(r => r.Id == cmd.RelationId);
        if (relation is null)
            return Result.Failure<RelationFiscalProfileResponse>(
                new Error("Relation.NotFound", "Relation not found within the customer.")
            );

        var relevanceCheck = EnsureTaxRelevantRelation(relation);
        if (relevanceCheck.IsFailure)
            return Result.Failure<RelationFiscalProfileResponse>(relevanceCheck.Error);

        var taxIdValidation = ValidateAndNormalizeTaxIdentifier(cmd.TaxIdentifier);
        if (taxIdValidation.IsFailure)
            return Result.Failure<RelationFiscalProfileResponse>(taxIdValidation.Error);

        var encryption = EncryptTaxIdentifier(taxIdValidation.Value, cmd.TenantId, protector);

        var uniquenessCheck = await EnsureNoBlindIndexConflictAsync(
            repository,
            cmd.TenantId,
            encryption.BlindIndex,
            cmd.RelationId,
            ct
        );
        if (uniquenessCheck.IsFailure)
            return Result.Failure<RelationFiscalProfileResponse>(uniquenessCheck.Error);

        var setResult = relation.SetFiscalProfile(
            role: cmd.Role,
            taxIdentifierCipher: encryption.Cipher,
            taxIdentifierBlindIndex: encryption.BlindIndex,
            taxIdentifierLast4: encryption.Last4,
            taxYear: cmd.TaxYear,
            qualifiesAsDependent: cmd.QualifiesAsDependent,
            livedWithTaxpayer: cmd.LivedWithTaxpayer,
            byUserId: cmd.ModifiedByUserId
        );

        if (setResult.IsFailure)
            return Result.Failure<RelationFiscalProfileResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        LogAudit(logger, cmd);

        return Result.Success(MapResponse(relation, cmd.RelationId));
    }

    // ============== Reglas de negocio ==============

    private static Result EnsureTaxRelevantRelation(CustomerRelation relation)
    {
        var taxRelevant =
            relation.Purposes.HasFlag(RelationPurpose.Dependent)
            || relation.Purposes.HasFlag(RelationPurpose.TaxHouseholdMember)
            || relation.RelationshipKind == RelationshipKind.Spouse;
        return taxRelevant
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Relation.NotFiscal",
                    "Only spouse, dependent or tax-household relations can have a fiscal profile."
                )
            );
    }

    // ============== Validacion + normalizacion del tax identifier ==============

    private static Result<string> ValidateAndNormalizeTaxIdentifier(string raw)
    {
        var normalized = new string(raw.Where(char.IsDigit).ToArray());
        return normalized.Length != 9
            ? Result.Failure<string>(new Error("Relation.TaxId", "Tax identifier must be 9 digits."))
            : Result.Success(normalized);
    }

    // ============== Cifrado ==============

    private sealed record EncryptedTaxIdentifier(byte[] Cipher, string BlindIndex, string Last4);

    private static EncryptedTaxIdentifier EncryptTaxIdentifier(
        string normalizedTaxId,
        Guid tenantId,
        ISensitiveDataProtector protector
    ) =>
        new(
            protector.Protect(normalizedTaxId),
            protector.ComputeBlindIndex(normalizedTaxId, tenantId),
            normalizedTaxId[^4..]
        );

    // ============== Unicidad del blind index (dentro del tenant) ==============

    private static async Task<Result> EnsureNoBlindIndexConflictAsync(
        ICustomerRepository repository,
        Guid tenantId,
        string blindIndex,
        Guid currentRelationId,
        CancellationToken ct
    )
    {
        var conflictingRelationId = await repository.FindRelationIdByFiscalBlindIndexAsync(
            tenantId,
            blindIndex,
            excludeRelationId: currentRelationId,
            ct
        );

        if (conflictingRelationId is not null)
            return Result.Failure(
                new Error(
                    "RelationFiscalProfile.TaxIdentifierAlreadyExists",
                    "Another relation in this tenant already has this tax identifier."
                )
            );

        return Result.Success();
    }

    // ============== Audit log (sin PII) ==============

    private static void LogAudit(ILogger logger, SetRelationFiscalProfileCommand cmd) =>
        logger.LogInformation(
            "Relation fiscal profile updated for relation {RelationId} (customer {CustomerId}) in tenant {TenantId} by user {UserId}",
            cmd.RelationId,
            cmd.CustomerId,
            cmd.TenantId,
            cmd.ModifiedByUserId
        );

    // ============== Mapeo ==============

    private static RelationFiscalProfileResponse MapResponse(CustomerRelation relation, Guid relationId)
    {
        var fp = relation.FiscalProfile!;
        return new RelationFiscalProfileResponse(
            CustomerRelationId: relationId,
            Role: fp.Role,
            TaxIdentifierLast4: fp.TaxIdentifierLast4,
            TaxYear: fp.TaxYear,
            QualifiesAsDependent: fp.QualifiesAsDependent,
            LivedWithTaxpayer: fp.LivedWithTaxpayer,
            UpdatedAtUtc: fp.UpdatedAtUtc,
            UpdatedByUserId: fp.UpdatedByUserId
        );
    }
}
