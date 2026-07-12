using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.AddSigner;

/// <summary>
/// Agrega un firmante a la solicitud. Antes de invocar al aggregate, consulta la
/// proyección local de clientes (regla P-14) para vincular <c>MappedCustomerId</c>
/// si existe un cliente activo del tenant con ese email.
/// </summary>
public static class AddSignerHandler
{
    public static async Task<Result<SignerResponse>> Handle(
        AddSignerCommand cmd,
        ISignatureRequestRepository requestRepository,
        ICustomerEmailProjectionRepository customerProjectionRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var voResult = BuildValueObjects(cmd);
        if (voResult.IsFailure)
            return Result.Failure<SignerResponse>(voResult.Error);

        var request = await requestRepository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var mappedCustomerId = await ResolveMappedCustomerId(
            cmd.TenantId,
            voResult.Value.Email,
            customerProjectionRepository,
            ct
        );

        var signerResult = request.AddSigner(
            voResult.Value.Email,
            voResult.Value.FullName,
            mappedCustomerId,
            voResult.Value.PhoneNumber
        );
        if (signerResult.IsFailure)
            return Result.Failure<SignerResponse>(signerResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(signerResult.Value));
    }

    private sealed record SignerValueObjects(
        SignerEmail Email,
        SignerFullName FullName,
        SignerPhoneNumber? PhoneNumber
    );

    private static Result<SignerValueObjects> BuildValueObjects(AddSignerCommand cmd)
    {
        var emailResult = SignerEmail.Create(cmd.Email);
        if (emailResult.IsFailure)
            return Result.Failure<SignerValueObjects>(emailResult.Error);

        var nameResult = SignerFullName.Create(cmd.FullName);
        if (nameResult.IsFailure)
            return Result.Failure<SignerValueObjects>(nameResult.Error);

        SignerPhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(cmd.PhoneNumber))
        {
            var phoneResult = SignerPhoneNumber.Create(cmd.PhoneNumber);
            if (phoneResult.IsFailure)
                return Result.Failure<SignerValueObjects>(phoneResult.Error);
            phone = phoneResult.Value;
        }

        return Result.Success(new SignerValueObjects(emailResult.Value, nameResult.Value, phone));
    }

    private static async Task<Guid?> ResolveMappedCustomerId(
        Guid tenantId,
        SignerEmail email,
        ICustomerEmailProjectionRepository projectionRepository,
        CancellationToken ct
    )
    {
        var match = await projectionRepository.FindActiveByEmailAsync(tenantId, email.Value, ct);
        return match?.CustomerId;
    }

    private static Result<SignerResponse> NotFound() =>
        Result.Failure<SignerResponse>(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );

    private static SignerResponse Map(Signer signer) =>
        new(
            signer.Id,
            signer.Email.Value,
            signer.FullName.Value,
            signer.MappedCustomerId,
            signer.Order,
            signer.Status,
            signer.SignedAtUtc,
            signer
                .Fields.Select(f => new SignatureFieldResponse(
                    f.Id,
                    signer.Id,
                    f.Kind,
                    f.Position.Page,
                    f.Position.X,
                    f.Position.Y,
                    f.Position.Width,
                    f.Position.Height,
                    f.Label,
                    f.IsRequired
                ))
                .ToList()
        );
}
