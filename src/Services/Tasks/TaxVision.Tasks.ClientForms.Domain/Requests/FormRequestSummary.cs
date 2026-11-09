using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// El Submission Summary de cada revisión: su PDF y su firma (P-18). Parte del agregado <see cref="FormRequest"/>.
/// La revisión no tiene RowVersion: cada cambio toca la raíz para que dos consumidores en paralelo choquen (TSK-4,
/// TSK-11). La correlación va por número de revisión; la del dispatch, por GenerationId.
/// </summary>
public sealed class FormRequestSummary
{
    private readonly FormRequest _request;

    internal FormRequestSummary(FormRequest request) => _request = request;

    public Result MarkPdfRequested(int revisionNumber, Guid generationId)
    {
        var revision = Revision(revisionNumber);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.MarkPdfRequested(generationId);
        _request.Touch(DateTime.UtcNow);
        return Result.Success();
    }

    public Result LinkPdf(Guid generationId, Guid fileId)
    {
        var revision = _request.Submission?.RevisionForPdf(generationId);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.LinkPdf(fileId);
        _request.Touch(DateTime.UtcNow);
        return Result.Success();
    }

    public Result MarkPdfFailed(Guid generationId)
    {
        var revision = _request.Submission?.RevisionForPdf(generationId);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.MarkPdfFailed();
        _request.Touch(DateTime.UtcNow);
        return Result.Success();
    }

    public int? RevisionNumberForGeneration(Guid generationId) =>
        _request.Submission?.RevisionForPdf(generationId)?.RevisionNumber;

    public Guid? SignatureRequestIdFor(int revisionNumber) => Revision(revisionNumber)?.SignatureRequestId;

    public bool RevisionHasPendingSignature(int revisionNumber) =>
        Revision(revisionNumber)?.HasPendingSignature ?? false;

    public FormRevisionSignatureStatus RevisionSignatureStatus(int revisionNumber) =>
        Revision(revisionNumber)?.SignatureStatus ?? FormRevisionSignatureStatus.None;

    public bool RevisionAwaitsSignature(int revisionNumber) => Revision(revisionNumber)?.AwaitsSignature ?? false;

    public Result MarkSignatureRequesting(int revisionNumber, Guid copyFileId, string documentHashPre)
    {
        var revision = Revision(revisionNumber);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.MarkSignatureRequesting(copyFileId, documentHashPre);
        _request.Touch(DateTime.UtcNow);
        return Result.Success();
    }

    public Result<bool> LinkSignatureRequest(int revisionNumber, Guid signatureRequestId) =>
        ApplySignatureTransition(revisionNumber, revision => revision.LinkSignatureRequest(signatureRequestId));

    public Result MarkSignatureFailed(int revisionNumber)
    {
        var revision = Revision(revisionNumber);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        // Un segundo aviso de fallo (dispatch duplicado) no vuelve a planificar ni a notificar.
        if (!revision.MarkSignatureFailed())
            return Result.Failure(FormRequestErrors.SignatureNotPending);
        _request.Touch(DateTime.UtcNow);
        return Result.Success();
    }

    public Result<bool> MarkSignatureSigned(int revisionNumber) =>
        ApplySignatureTransition(revisionNumber, revision => revision.MarkSignatureSigned());

    public Result<bool> MarkSignatureCompleted(int revisionNumber) =>
        ApplySignatureTransition(revisionNumber, revision => revision.MarkSignatureCompleted());

    public Result<bool> MarkSignatureSealed(
        int revisionNumber,
        Guid sealedFileId,
        string documentHashPost,
        Guid? certificateFileId
    ) =>
        ApplySignatureTransition(
            revisionNumber,
            revision => revision.MarkSignatureSealed(sealedFileId, documentHashPost, certificateFileId)
        );

    public Result<bool> MarkSignatureDeclined(int revisionNumber) =>
        ApplySignatureTransition(revisionNumber, revision => revision.MarkSignatureDeclined());

    public Result<bool> MarkSignatureCanceled(int revisionNumber) =>
        ApplySignatureTransition(revisionNumber, revision => revision.MarkSignatureCanceled());

    public Result<bool> MarkSignatureExpired(int revisionNumber) =>
        ApplySignatureTransition(revisionNumber, revision => revision.MarkSignatureExpired());

    private FormSubmissionRevision? Revision(int revisionNumber) =>
        _request.Submission?.CurrentRevision(revisionNumber);

    // Devuelve false si el estado actual no admite la transición.
    private Result<bool> ApplySignatureTransition(int revisionNumber, Func<FormSubmissionRevision, bool> transition)
    {
        var revision = Revision(revisionNumber);
        if (revision is null)
            return Result.Failure<bool>(FormRequestErrors.NotFound);

        var applied = transition(revision);
        if (applied)
            _request.Touch(DateTime.UtcNow);
        return Result.Success(applied);
    }
}
