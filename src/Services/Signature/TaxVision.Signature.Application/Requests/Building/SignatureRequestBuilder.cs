using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Requests.Building;

/// <summary>Datos del firmante ya validados.</summary>
public sealed record SignerIdentity(SignerEmail Email, SignerFullName FullName, SignerPhoneNumber? Phone);

/// <summary>Lo que comparten las solicitudes que nacen de un formulario o de una plantilla.</summary>
public static class SignatureRequestBuilder
{
    // Fail-fast y en este orden: el primer dato inválido es el error que ve quien pidió la solicitud.
    public static Result<SignerIdentity> ParseSigner(string email, string fullName, string? phone)
    {
        var emailResult = SignerEmail.Create(email);
        if (emailResult.IsFailure)
            return Result.Failure<SignerIdentity>(emailResult.Error);

        var nameResult = SignerFullName.Create(fullName);
        if (nameResult.IsFailure)
            return Result.Failure<SignerIdentity>(nameResult.Error);

        if (string.IsNullOrWhiteSpace(phone))
            return Result.Success(new SignerIdentity(emailResult.Value, nameResult.Value, null));

        var phoneResult = SignerPhoneNumber.Create(phone);
        return phoneResult.IsFailure
            ? Result.Failure<SignerIdentity>(phoneResult.Error)
            : Result.Success(new SignerIdentity(emailResult.Value, nameResult.Value, phoneResult.Value));
    }

    /// <summary>Agrega los documentos en orden y devuelve sus ids en ese mismo orden.</summary>
    public static Result<IReadOnlyList<Guid>> AddDocuments(
        SignatureRequest request,
        SignatureLimits limits,
        IEnumerable<(Guid FileId, string Title)> documents
    )
    {
        var ids = new List<Guid>();
        foreach (var (fileId, title) in documents)
        {
            var added = request.AddDocument(limits, fileId, title);
            if (added.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(added.Error);
            ids.Add(added.Value.Id);
        }

        return Result.Success<IReadOnlyList<Guid>>(ids);
    }
}
