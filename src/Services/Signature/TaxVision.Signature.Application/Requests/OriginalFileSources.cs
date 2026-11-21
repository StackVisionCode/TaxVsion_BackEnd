using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests;

/// <summary>Originales de una solicitud y el origen con que se declaran, antes de un cambio.</summary>
public sealed record OriginalsSnapshot(IReadOnlySet<Guid> Files, SignatureFileSource Source);

/// <summary>
/// El original a firmar se declara en CloudStorage como <c>original</c> de la solicitud, con el cliente firmante si
/// es exactamente uno; el que se reemplaza o se quita, como <c>original_superseded</c> (no se lista). El PDF base
/// de una plantilla nunca se declara. Se llama después de guardar (sale con el commit, por outbox).
/// </summary>
public static class OriginalFileSources
{
    public static SignatureFileSource Of(Guid requestId, IReadOnlyCollection<Guid?> mappedCustomerIds)
    {
        var (role, customerId) = SealedFileRole.Resolve(mappedCustomerIds, SealedFileRole.Original);
        return new SignatureFileSource(requestId, role, customerId);
    }

    public static OriginalsSnapshot Snapshot(SignatureRequest request) =>
        new(request.Documents.Select(document => document.OriginalFileId).ToHashSet(), SourceOf(request));

    /// <summary>Lo que cambió desde <paramref name="before"/>; si cambió el cliente, se re-declaran todos.</summary>
    public static Task DeclareChangesAsync(
        IMessageBus bus,
        ISignatureTemplateFileQuery templates,
        OriginalsSnapshot before,
        SignatureRequest request,
        string correlationId,
        CancellationToken ct
    )
    {
        var after = Snapshot(request);
        var originals = after.Source == before.Source ? after.Files.Except(before.Files) : after.Files;
        return DeclareAsync(
            bus,
            templates,
            request,
            [.. originals],
            [.. before.Files.Except(after.Files)],
            correlationId,
            ct
        );
    }

    public static async Task DeclareAsync(
        IMessageBus bus,
        ISignatureTemplateFileQuery templates,
        SignatureRequest request,
        IReadOnlyCollection<Guid> originals,
        IReadOnlyCollection<Guid> superseded,
        string correlationId,
        CancellationToken ct
    )
    {
        if (originals.Count == 0 && superseded.Count == 0)
            return;
        var templateFiles = await templates.TemplateBaseFilesAsync(request.TenantId, [.. originals, .. superseded], ct);
        await PublishAsync(
            bus,
            request,
            originals.Where(fileId => !templateFiles.Contains(fileId)),
            superseded.Where(fileId => !templateFiles.Contains(fileId)),
            correlationId
        );
    }

    /// <summary>Sin consultar plantillas: quien llama ya sabe que ningún archivo es base de una.</summary>
    public static async Task PublishAsync(
        IMessageBus bus,
        SignatureRequest request,
        IEnumerable<Guid> originals,
        IEnumerable<Guid> superseded,
        string correlationId
    )
    {
        var source = SourceOf(request);
        var retired = source with { Role = SealedFileRole.OriginalSuperseded };
        foreach (var fileId in originals)
            await bus.PublishAsync(source.ToDeclaration(request.TenantId, fileId, correlationId));
        foreach (var fileId in superseded)
            await bus.PublishAsync(retired.ToDeclaration(request.TenantId, fileId, correlationId));
    }

    private static SignatureFileSource SourceOf(SignatureRequest request) =>
        Of(request.Id, [.. request.Signers.Select(signer => signer.MappedCustomerId)]);
}
