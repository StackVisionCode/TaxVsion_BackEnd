using BuildingBlocks.Contracts.Files;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas de la biblioteca con tenant explícito (mismo patrón que <see cref="FileObjectRepository"/>). El
/// predicado de orígenes es uno solo, con una bandera por origen; <c>Status</c> se compara con la constante para
/// que SQL Server use los índices filtrados de la biblioteca.
/// </summary>
public sealed class LibraryReadRepository(CloudStorageDbContext db) : ILibraryReadRepository
{
    public async Task<IReadOnlyList<LibraryRow>> SearchAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        int take,
        CancellationToken ct
    ) =>
        await Project(Ordered(After(Filtered(Visible(tenantId, scope), filter), filter), filter.Sort))
            .Take(take)
            .ToListAsync(ct);

    public Task<LibraryRow?> FindAsync(Guid tenantId, LibraryReadScope scope, Guid fileId, CancellationToken ct) =>
        Project(Visible(tenantId, scope).Where(file => file.Id == fileId)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<LibrarySource, int>> CountBySourceAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        CancellationToken ct
    )
    {
        // Cada fila cae en un solo origen y el tipo de recurso lo identifica.
        var counts = await Filtered(Visible(tenantId, scope), filter)
            .GroupBy(file => file.SourceResourceType)
            .Select(group => new { ResourceType = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        return counts
            .Where(count => SourceOf(count.ResourceType) is not null)
            .ToDictionary(count => SourceOf(count.ResourceType)!.Value, count => count.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FolderPathsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    )
    {
        var ids = folderIds.ToList();
        return await db
            .Folders.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(folder => folder.TenantId == tenantId && ids.Contains(folder.Id))
            .ToDictionaryAsync(folder => folder.Id, folder => folder.RelativePath, ct);
    }

    // Predicado de la lista blanca (diseño §2.3). Un origen que no está en el alcance apaga su rama.
    private IQueryable<FileObject> Visible(Guid tenantId, LibraryReadScope scope)
    {
        var canDocuments = scope.Includes(LibrarySource.Documents);
        var restrictDocuments = scope.IsRestricted(LibrarySource.Documents);
        var canSignature = scope.Includes(LibrarySource.Signature);
        var restrictSignature = scope.IsRestricted(LibrarySource.Signature);
        var canBilling = scope.Includes(LibrarySource.Billing);
        var restrictBilling = scope.IsRestricted(LibrarySource.Billing);
        var canFinancial = scope.Includes(LibrarySource.Financial);
        var restrictFinancial = scope.IsRestricted(LibrarySource.Financial);
        var canTasks = scope.Includes(LibrarySource.Tasks);
        var canForms = scope.Includes(LibrarySource.Forms);
        var canMail = scope.Includes(LibrarySource.Mail);
        var canNotes = scope.Includes(LibrarySource.Notes);
        var assigned = scope.AssignedCustomerIds.ToList();
        // Communication: sin ámbito (la descarga) no se acota; el dueño confirma cada fila igual.
        var canChat = scope.Includes(LibrarySource.Chat);
        var communicationScoped = scope.Communication is not null;
        var conversations = (scope.Communication?.ConversationIds ?? []).ToList();
        // Grabaciones: cada tipo con su variante cumplida y su lista del ámbito.
        var recordingTypes = scope.Sources.FirstOrDefault(s => s.Source == LibrarySource.Recordings) is { } recordings
            ? recordings.ResourceTypes
            : [];
        var canCallRecordings =
            scope.Includes(LibrarySource.Recordings)
            && (recordingTypes is null || recordingTypes.Contains(FileVisibilityResourceTypes.Call));
        var canMeetingRecordings =
            scope.Includes(LibrarySource.Recordings)
            && (recordingTypes is null || recordingTypes.Contains(FileVisibilityResourceTypes.Meeting));
        var calls = (scope.Communication?.CallIds ?? []).ToList();
        var meetings = (scope.Communication?.MeetingIds ?? []).ToList();

        return db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.Status == FileStatus.Available
                && (
                    (
                        canDocuments
                        && file.SourceResourceType == null
                        && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.Tenant)
                        && (
                            file.FolderType == FolderType.Documents
                            || file.FolderType == FolderType.Receipts
                            || file.FolderType == FolderType.Invoices
                        )
                        && (
                            !restrictDocuments
                            || file.OwnerType == OwnerType.Tenant
                            || (file.OwnerId != null && assigned.Contains(file.OwnerId.Value))
                        )
                    )
                    || (
                        canSignature
                        && file.SourceResourceType == LibrarySourceCatalog.SignatureResourceType
                        && file.FolderType == FolderType.Signatures
                        && (
                            (
                                (
                                    file.SourceRole == LibrarySourceCatalog.SealedRole
                                    || file.SourceRole == LibrarySourceCatalog.CertificateRole
                                ) && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.Signature)
                            )
                            // Original sin firmar: solo con dueño Signature.
                            || (
                                file.SourceRole == LibrarySourceCatalog.OriginalRole
                                && file.OwnerType == OwnerType.Signature
                            )
                        )
                        && (
                            !restrictSignature
                            || (file.RelatedCustomerId != null && assigned.Contains(file.RelatedCustomerId.Value))
                        )
                    )
                    || (
                        canBilling
                        && file.SourceResourceType == LibrarySourceCatalog.InvoiceResourceType
                        && file.SourceRole == LibrarySourceCatalog.InvoicePdfRole
                        && file.OwnerType == OwnerType.Invoice
                        && file.FolderType == FolderType.Invoices
                        && (
                            !restrictBilling
                            || (file.RelatedCustomerId != null && assigned.Contains(file.RelatedCustomerId.Value))
                        )
                    )
                    || (
                        canFinancial
                        && file.SourceResourceType == LibrarySourceCatalog.FinancialResourceType
                        && file.SourceRole == LibrarySourceCatalog.StatementRole
                        && file.OwnerType == OwnerType.Customer
                        && (
                            !restrictFinancial
                            || (file.RelatedCustomerId != null && assigned.Contains(file.RelatedCustomerId.Value))
                        )
                    )
                    // Orígenes por fila: solo candidatos, sin recorte por cliente; cada fila la confirma el dueño.
                    || (
                        canTasks
                        && file.SourceResourceType == LibrarySourceCatalog.TaskResourceType
                        && file.SourceRole == LibrarySourceCatalog.AttachmentRole
                        && file.FolderType == FolderType.Tasks
                        && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.User)
                    )
                    || (
                        canForms
                        && file.SourceResourceType == LibrarySourceCatalog.FormRequestResourceType
                        && (
                            file.SourceRole == LibrarySourceCatalog.AttachmentRole
                            || file.SourceRole == LibrarySourceCatalog.SummaryPdfRole
                        )
                        && file.FolderType == FolderType.ClientForms
                        && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.FormParticipant)
                    )
                    || (
                        canMail
                        && (
                            (
                                file.SourceResourceType == LibrarySourceCatalog.EmailMessageResourceType
                                && file.SourceRole == LibrarySourceCatalog.OutgoingAttachmentRole
                                && file.FolderType == FolderType.EmailOutgoing
                            )
                            || (
                                file.SourceResourceType == LibrarySourceCatalog.IncomingEmailResourceType
                                && file.SourceRole == LibrarySourceCatalog.IncomingAttachmentRole
                                && file.FolderType == FolderType.EmailIncoming
                            )
                        )
                        && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.User)
                    )
                    // Notas: sin recorte por cliente; Notes decide cada fila (privadas, borradas, asignación).
                    || (
                        canNotes
                        && file.SourceResourceType == FileVisibilityResourceTypes.Note
                        && file.SourceRole == LibrarySourceCatalog.AttachmentRole
                        && (file.OwnerType == OwnerType.Customer || file.OwnerType == OwnerType.Tenant)
                        && file.FolderType == FolderType.Other
                    )
                    // El ámbito va como un solo parámetro JSON (OPENJSON): puede traer miles de conversaciones.
                    || (
                        canChat
                        && file.SourceResourceType == FileVisibilityResourceTypes.Conversation
                        && (
                            file.SourceRole == LibrarySourceCatalog.AttachmentRole
                            || file.SourceRole == LibrarySourceCatalog.VoiceNoteRole
                        )
                        && (
                            (file.OwnerType == OwnerType.Communication && file.FolderType == FolderType.Other)
                            || file.FolderType == FolderType.VoiceNotes
                        )
                        && (
                            !communicationScoped
                            || (
                                file.SourceResourceId != null
                                && EF.Parameter(conversations).Contains(file.SourceResourceId.Value)
                            )
                        )
                    )
                    // Nunca transcript_data ni pistas: solo la grabación y el .txt.
                    || (
                        canCallRecordings
                        && file.SourceResourceType == FileVisibilityResourceTypes.Call
                        && (
                            file.SourceRole == LibrarySourceCatalog.RecordingRole
                            || file.SourceRole == LibrarySourceCatalog.TranscriptRole
                        )
                        && file.OwnerType == OwnerType.Communication
                        && (file.FolderType == FolderType.Recordings || file.FolderType == FolderType.Transcripts)
                        && (
                            !communicationScoped
                            || (
                                file.SourceResourceId != null
                                && EF.Parameter(calls).Contains(file.SourceResourceId.Value)
                            )
                        )
                    )
                    || (
                        canMeetingRecordings
                        && file.SourceResourceType == FileVisibilityResourceTypes.Meeting
                        && (
                            file.SourceRole == LibrarySourceCatalog.RecordingRole
                            || file.SourceRole == LibrarySourceCatalog.TranscriptRole
                        )
                        && file.OwnerType == OwnerType.Communication
                        && (file.FolderType == FolderType.Recordings || file.FolderType == FolderType.Transcripts)
                        && (
                            !communicationScoped
                            || (
                                file.SourceResourceId != null
                                && EF.Parameter(meetings).Contains(file.SourceResourceId.Value)
                            )
                        )
                    )
                )
            );
    }

    private static IQueryable<FileObject> Filtered(IQueryable<FileObject> files, LibraryFilter filter)
    {
        if (filter.SearchPattern is { } pattern)
            files = files.Where(file => EF.Functions.Like(file.OriginalName, pattern, @"\"));
        if (filter.CustomerId is { } customerId)
            files = files.Where(file => file.RelatedCustomerId == customerId);
        if (filter.UploadedBy is { } uploadedBy)
            files = files.Where(file => file.CreatedBy == uploadedBy);
        if (filter.FromUtc is { } from)
            files = files.Where(file => file.CreatedAtUtc >= from);
        if (filter.ToUtc is { } to)
            files = files.Where(file => file.CreatedAtUtc < to);
        if (filter.TaxYear is { } taxYear)
            files = files.Where(file => file.TaxYear == taxYear);
        if (filter.FileId is { } fileId)
            files = files.Where(file => file.Id == fileId);
        if (filter.OwnerType is { } ownerType)
            files = files.Where(file => file.OwnerType == ownerType);
        if (filter.OwnerId is { } ownerId)
            files = files.Where(file => file.OwnerId == ownerId);
        return filter.TypeGroups.Count == 0 ? files : OfTypes(files, filter.TypeGroups);
    }

    private static IQueryable<FileObject> OfTypes(IQueryable<FileObject> files, IReadOnlyList<LibraryTypeGroup> groups)
    {
        var types = groups.SelectMany(LibraryTypeGroups.ContentTypesOf).ToList();
        var includeOther = groups.Contains(LibraryTypeGroup.Other);
        var known = LibraryTypeGroups.Known.ToList();
        return files.Where(file =>
            types.Contains(file.DetectedContentType ?? file.DeclaredContentType)
            || (includeOther && !known.Contains(file.DetectedContentType ?? file.DeclaredContentType))
        );
    }

    // Paginación por clave: la fila siguiente a la última examinada, con el id como desempate.
    private static IQueryable<FileObject> After(IQueryable<FileObject> files, LibraryFilter filter)
    {
        if (filter.Cursor is not { } cursor)
            return files;
        var id = cursor.Id;
        if (filter.Sort == LibrarySort.Name)
        {
            var name = cursor.Key;
            return files.Where(file =>
                string.Compare(file.OriginalName, name) > 0 || (file.OriginalName == name && file.Id.CompareTo(id) > 0)
            );
        }
        var at = cursor.CreatedAtKey;
        return filter.Sort == LibrarySort.Oldest
            ? files.Where(file => file.CreatedAtUtc > at || (file.CreatedAtUtc == at && file.Id.CompareTo(id) > 0))
            : files.Where(file => file.CreatedAtUtc < at || (file.CreatedAtUtc == at && file.Id.CompareTo(id) < 0));
    }

    private static IQueryable<FileObject> Ordered(IQueryable<FileObject> files, LibrarySort sort) =>
        sort switch
        {
            LibrarySort.Oldest => files.OrderBy(file => file.CreatedAtUtc).ThenBy(file => file.Id),
            LibrarySort.Name => files.OrderBy(file => file.OriginalName).ThenBy(file => file.Id),
            _ => files.OrderByDescending(file => file.CreatedAtUtc).ThenByDescending(file => file.Id),
        };

    private static IQueryable<LibraryRow> Project(IQueryable<FileObject> files) =>
        files.Select(file => new LibraryRow(
            file.Id,
            file.OriginalName,
            file.DetectedContentType ?? file.DeclaredContentType,
            file.SizeBytes,
            file.CreatedAtUtc,
            file.CreatedBy,
            file.TaxYear,
            file.OwnerType,
            file.OwnerId,
            file.FolderType,
            file.FolderId,
            file.SourceResourceType,
            file.SourceResourceId,
            file.SourceRole,
            file.RelatedCustomerId
        ));

    private static LibrarySource? SourceOf(string? resourceType) =>
        resourceType switch
        {
            null => LibrarySource.Documents,
            LibrarySourceCatalog.SignatureResourceType => LibrarySource.Signature,
            LibrarySourceCatalog.InvoiceResourceType => LibrarySource.Billing,
            LibrarySourceCatalog.FinancialResourceType => LibrarySource.Financial,
            _ => null,
        };
}
