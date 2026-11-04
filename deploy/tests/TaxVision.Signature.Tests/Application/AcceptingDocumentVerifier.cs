using System.Security.Cryptography;
using System.Text;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application;

/// <summary>Doble de la revisión del servidor que acepta todo: para tests de handlers que no la ejercitan.</summary>
internal sealed class AcceptingDocumentVerifier : IRequestDocumentVerifier
{
    public const int PageCount = 10;

    public static readonly AcceptingDocumentVerifier Instance = new();

    public Task<DocumentVerificationResult> VerifyAsync(SignatureRequest request, Guid documentId, CancellationToken ct)
    {
        var fileId = request.Documents.Single(document => document.Id == documentId).OriginalFileId;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileId.ToString()))).ToLowerInvariant();
        return Accept(request, documentId, hash);
    }

    public Task<DocumentVerificationResult> VerifyAsync(
        SignatureRequest request,
        Guid documentId,
        VerifiableFile file,
        CancellationToken ct
    ) => Accept(request, documentId, file.ChecksumSha256);

    private static Task<DocumentVerificationResult> Accept(SignatureRequest request, Guid documentId, string hash)
    {
        var verified = VerifiedDocument.Create(DocumentHash.Create(hash).Value, PageCount).Value;
        request.AttachVerifiedDocument(documentId, verified);
        return Task.FromResult(DocumentVerificationResult.Verified);
    }
}
