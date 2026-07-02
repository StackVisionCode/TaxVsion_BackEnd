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

        // Solo relaciones fiscalmente relevantes pueden tener fiscal profile
        var taxRelevant =
            relation.Purposes.HasFlag(RelationPurpose.Dependent)
            || relation.Purposes.HasFlag(RelationPurpose.TaxHouseholdMember)
            || relation.RelationshipKind == RelationshipKind.Spouse;
        if (!taxRelevant)
            return Result.Failure<RelationFiscalProfileResponse>(
                new Error(
                    "Relation.NotFiscal",
                    "Only spouse, dependent or tax-household relations can have a fiscal profile."
                )
            );

        var normalizedIdent = new string(cmd.TaxIdentifier.Where(char.IsDigit).ToArray());
        if (normalizedIdent.Length != 9)
            return Result.Failure<RelationFiscalProfileResponse>(
                new Error("Relation.TaxId", "Tax identifier must be 9 digits.")
            );

        var cipher = protector.Protect(normalizedIdent);
        var blindIndex = protector.ComputeBlindIndex(normalizedIdent, cmd.TenantId);
        var last4 = normalizedIdent[^4..];

        var setResult = relation.SetFiscalProfile(
            role: cmd.Role,
            taxIdentifierCipher: cipher,
            taxIdentifierBlindIndex: blindIndex,
            taxIdentifierLast4: last4,
            taxYear: cmd.TaxYear,
            qualifiesAsDependent: cmd.QualifiesAsDependent,
            livedWithTaxpayer: cmd.LivedWithTaxpayer,
            byUserId: cmd.ModifiedByUserId
        );

        if (setResult.IsFailure)
            return Result.Failure<RelationFiscalProfileResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Relation fiscal profile updated for relation {RelationId} (customer {CustomerId}) in tenant {TenantId} by user {UserId}",
            cmd.RelationId,
            cmd.CustomerId,
            cmd.TenantId,
            cmd.ModifiedByUserId
        );

        var fp = relation.FiscalProfile!;
        return Result.Success(
            new RelationFiscalProfileResponse(
                CustomerRelationId: cmd.RelationId,
                Role: fp.Role,
                TaxIdentifierLast4: fp.TaxIdentifierLast4,
                TaxYear: fp.TaxYear,
                QualifiesAsDependent: fp.QualifiesAsDependent,
                LivedWithTaxpayer: fp.LivedWithTaxpayer,
                UpdatedAtUtc: fp.UpdatedAtUtc,
                UpdatedByUserId: fp.UpdatedByUserId
            )
        );
    }
}
