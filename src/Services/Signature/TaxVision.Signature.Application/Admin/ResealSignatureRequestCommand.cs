using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Admin;

/// <summary>Resultado del reseal: documentos que se volvieron a mandar a sellar.</summary>
public sealed record ResealReport(Guid SignatureRequestId, int DocumentCount);

/// <summary>
/// SIG-2: reintento manual del sellado, después de corregir un fallo permanente. Limpia
/// <c>SealingFailedAtUtc</c> y vuelve a publicar <c>SignatureDocumentsReadyForSealing</c>. El sellado salta
/// lo ya sellado, así que repetirlo no duplica nada.
/// </summary>
public sealed record ResealSignatureRequestCommand(Guid TenantId, Guid SignatureRequestId);

public static class ResealSignatureRequestHandler
{
    public static async Task<Result<ResealReport>> Handle(
        ResealSignatureRequestCommand command,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<ResealReport>(
                new Error("Signature.Request.NotFound", "The signature request does not exist.")
            );

        if (request.Status != SignatureRequestStatus.Completed)
            return Result.Failure<ResealReport>(
                new Error("Signature.Request.NotCompleted", "Only a completed request can be sealed again.")
            );

        var unsealed = request
            .Documents.Where(document => document.SealedFileId is null)
            .Select(document => document.Id)
            .ToList();
        var missingCertificates = request.GenerateCertificate && !request.AllCertificateArtifactsGenerated();
        if (unsealed.Count == 0 && !missingCertificates)
            return Result.Failure<ResealReport>(
                new Error("Signature.Request.NothingToSeal", "Every document and certificate is already sealed.")
            );

        // Con todo sellado y el certificado pendiente, el sellado lo genera al recibir cualquier documento.
        var documentIds = unsealed.Count > 0 ? unsealed : request.Documents.Select(document => document.Id).ToList();

        request.ClearSealingFailure();
        await unitOfWork.SaveChangesAsync(ct);
        await SubmitSignatureHandler.PublishDocumentsReadyForSealingAsync(
            request,
            documentIds,
            DateTime.UtcNow,
            correlation,
            bus
        );
        return Result.Success(new ResealReport(request.Id, documentIds.Count));
    }
}
