using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Resultado del backfill: solicitudes revisadas y archivos declarados (o por declarar en <c>DryRun</c>).</summary>
public sealed record RedeclaredFileSourcesReport(bool DryRun, int RequestsScanned, int FilesDeclared);

/// <summary>
/// Backfill: vuelve a declarar en CloudStorage los sellados, certificados (o sus maestros) y las copias por
/// cliente de las solicitudes completadas del tenant, y el original a firmar de todas sus solicitudes. No
/// modifica filas de Signature. Idempotente.
/// <paramref name="DryRun"/> solo cuenta.
/// </summary>
public sealed record RedeclareFileSourcesCommand(Guid TenantId, bool DryRun);

public static class RedeclareFileSourcesHandler
{
    private const int PageSize = 500;

    public static async Task<Result<RedeclaredFileSourcesReport>> Handle(
        RedeclareFileSourcesCommand command,
        ISignatureRequestRepository requests,
        ISignatureOriginalFileQuery originals,
        ISignatureTemplateFileQuery templates,
        IFileSourceDeclarationPublisher publisher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var completed = await requests.ListCompletedWithSealedFileAsync(command.TenantId, ct);
        var withOriginals = await originals.ListAsync(command.TenantId, ct);
        var templateFiles = await templates.TemplateBaseFilesAsync(
            command.TenantId,
            [.. withOriginals.SelectMany(request => request.FileIds)],
            ct
        );
        var declarations = completed
            .SelectMany(request => SignatureFileDeclarations.SealedOutputs(request, correlation.CorrelationId))
            .Concat(
                withOriginals.SelectMany(request =>
                    SignatureFileDeclarations.Originals(
                        command.TenantId,
                        request,
                        templateFiles,
                        correlation.CorrelationId
                    )
                )
            )
            .ToList();
        var scanned = completed.Select(request => request.Id).Union(withOriginals.Select(r => r.RequestId)).Count();

        if (!command.DryRun)
            foreach (var page in declarations.Chunk(PageSize))
            {
                var published = await publisher.PublishAsync(page, ct);
                if (published.IsFailure)
                    return Result.Failure<RedeclaredFileSourcesReport>(published.Error);
            }

        return Result.Success(new RedeclaredFileSourcesReport(command.DryRun, scanned, declarations.Count));
    }
}
