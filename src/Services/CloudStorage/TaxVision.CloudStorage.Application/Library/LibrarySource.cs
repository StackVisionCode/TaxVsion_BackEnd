using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Library;

/// <summary>Orígenes de la biblioteca de archivos. El orden es el de la respuesta.</summary>
public enum LibrarySource
{
    Documents,
    Signature,
    Billing,
    Financial,
    Tasks,
    Forms,
    Mail,
    Chat,
    Recordings,
    Notes,
}

/// <summary>Una forma de leer un origen: su permiso, su módulo y los tipos de recurso que abre.</summary>
public sealed record LibrarySourceVariant(
    string Id,
    IReadOnlyList<string> ReadPermissions,
    string? Module,
    IReadOnlyList<string> ResourceTypes
);

/// <param name="ReadPermissions">Basta uno para leer el origen.</param>
/// <param name="Module">Módulo del plan que se exige siempre; null = sin módulo.</param>
/// <param name="AlwaysRestrict">Se recorta a clientes asignados aunque el flag de CloudStorage esté apagado.</param>
/// <param name="RowScoped">La visibilidad de cada fila la decide el servicio dueño.</param>
/// <param name="Owner">Servicio que confirma cada fila; solo en los orígenes por fila.</param>
/// <param name="Variants">
/// Si las hay, sustituyen a <paramref name="ReadPermissions"/> y <paramref name="Module"/>: el origen se lee con una
/// sola cumplida y cada fila exige la de su tipo de recurso.
/// </param>
/// <param name="ScopeLists">Listas del ámbito de Communication que acotan sus candidatos; vacío = sin ámbito.</param>
/// <param name="Published">false = definido pero todavía sin dueño que responda: no se ofrece a nadie.</param>
public sealed record LibrarySourceDefinition(
    LibrarySource Source,
    string Id,
    IReadOnlyList<string> ReadPermissions,
    string? Module,
    bool AlwaysRestrict,
    bool RowScoped,
    FileOwnerService? Owner = null,
    IReadOnlyList<LibrarySourceVariant>? Variants = null,
    IReadOnlyList<string>? ScopeLists = null,
    bool Published = true
)
{
    public bool UsesScope => ScopeLists is { Count: > 0 };
}

/// <summary>
/// Lista blanca de la biblioteca: qué filas de <c>Files</c> entran en cada origen, con qué permiso y a dónde
/// abren. El SQL del repositorio aplica la misma clasificación.
/// </summary>
public static class LibrarySourceCatalog
{
    // Billing no publica sus constantes en BuildingBlocks.
    private const string InvoicingView = "invoicing.view";

    public const string SignatureResourceType = "signature_request";
    public const string SealedRole = "sealed";
    public const string CertificateRole = "certificate";
    public const string OriginalRole = "original";
    public const string InvoiceResourceType = "invoice";
    public const string InvoicePdfRole = "invoice_pdf";
    public const string FinancialResourceType = "financial_import_batch";
    public const string StatementRole = "statement";
    public const string TaskResourceType = "task";
    public const string AttachmentRole = "attachment";
    public const string FormRequestResourceType = "form_request";
    public const string SummaryPdfRole = "summary_pdf";
    public const string EmailMessageResourceType = "email_message";
    public const string OutgoingAttachmentRole = "outgoing_attachment";
    public const string IncomingEmailResourceType = "incoming_email";
    public const string IncomingAttachmentRole = "incoming_attachment";
    public const string VoiceNoteRole = "voice_note";
    public const string RecordingRole = "recording";
    public const string TranscriptRole = "transcript";

    public static IReadOnlyList<LibrarySourceDefinition> All { get; } =
    [
        new(LibrarySource.Documents, "documents", [CloudStoragePermissions.FileView], "documents", false, false),
        new(LibrarySource.Signature, "signature", [SignaturePermissions.RequestRead], "signatures", false, false),
        new(LibrarySource.Billing, "billing", [InvoicingView], null, false, false),
        new(
            LibrarySource.Financial,
            "financial",
            [FinancialInsightsPermissions.View],
            "financial_insights",
            true,
            false
        ),
        new(LibrarySource.Tasks, "tasks", [TasksPermissions.Read], "planner", false, true, FileOwnerService.Tasks),
        new(
            LibrarySource.Forms,
            "forms",
            [TasksPermissions.FormRequestsRead],
            "planner",
            false,
            true,
            FileOwnerService.Tasks
        ),
        new(
            LibrarySource.Mail,
            "mail",
            [CorrespondencePermissions.Read],
            "email",
            false,
            true,
            FileOwnerService.Correspondence
        ),
        new(
            LibrarySource.Chat,
            "chat",
            [CommunicationPermissions.ChatReply],
            "comms",
            false,
            true,
            FileOwnerService.Communication,
            ScopeLists: [CommunicationFileScopeLists.Conversations]
        ),
        new(
            LibrarySource.Recordings,
            "recordings",
            [],
            null,
            false,
            true,
            FileOwnerService.Communication,
            Variants:
            [
                new("call", [CommunicationPermissions.CallStart], "comms", [FileVisibilityResourceTypes.Call]),
                new(
                    "meeting",
                    [CommunicationPermissions.MeetingJoin],
                    "meetings",
                    [FileVisibilityResourceTypes.Meeting]
                ),
            ],
            ScopeLists: [CommunicationFileScopeLists.Calls, CommunicationFileScopeLists.Meetings]
        ),
        new(LibrarySource.Notes, "notes", [NotesPermissions.Read], "planner", false, true, FileOwnerService.Notes),
    ];

    /// <summary>Orígenes que se ofrecen, en el orden de la respuesta.</summary>
    public static IReadOnlyList<LibrarySourceDefinition> Published { get; } = All.Where(d => d.Published).ToList();

    public static LibrarySourceDefinition Get(LibrarySource source) => All.Single(d => d.Source == source);

    public static string IdOf(LibrarySource source) => Get(source).Id;

    public static bool TryParse(string? id, out LibrarySource source)
    {
        var definition = Published.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
        source = definition?.Source ?? default;
        return definition is not null;
    }

    /// <summary>Origen de una fila disponible, o null si no está en la lista blanca.</summary>
    public static LibrarySource? Classify(OwnerType owner, FolderType folder, string? resourceType, string? role) =>
        resourceType switch
        {
            null
                when owner is OwnerType.Customer or OwnerType.Tenant
                    && folder is FolderType.Documents or FolderType.Receipts or FolderType.Invoices =>
                LibrarySource.Documents,
            SignatureResourceType
                when role is SealedRole or CertificateRole
                    && folder == FolderType.Signatures
                    && owner is OwnerType.Customer or OwnerType.Signature => LibrarySource.Signature,
            SignatureResourceType
                when role == OriginalRole && folder == FolderType.Signatures && owner == OwnerType.Signature =>
                LibrarySource.Signature,
            InvoiceResourceType
                when role == InvoicePdfRole && owner == OwnerType.Invoice && folder == FolderType.Invoices =>
                LibrarySource.Billing,
            FinancialResourceType when role == StatementRole && owner == OwnerType.Customer => LibrarySource.Financial,
            // Orígenes por fila: solo candidatos; cada fila la confirma el dueño.
            TaskResourceType
                when role == AttachmentRole
                    && folder == FolderType.Tasks
                    && owner is OwnerType.Customer or OwnerType.User => LibrarySource.Tasks,
            // Externo sin vincular (E-8): Tasks lo confirma con la regla de la bandeja y no lleva cliente.
            FormRequestResourceType
                when role is AttachmentRole or SummaryPdfRole
                    && folder == FolderType.ClientForms
                    && owner is OwnerType.Customer or OwnerType.FormParticipant => LibrarySource.Forms,
            // Correo: el dueño User (sin cliente) no decide nada; la fila la confirma Correspondence.
            EmailMessageResourceType
                when role == OutgoingAttachmentRole
                    && folder == FolderType.EmailOutgoing
                    && owner is OwnerType.Customer or OwnerType.User => LibrarySource.Mail,
            IncomingEmailResourceType
                when role == IncomingAttachmentRole
                    && folder == FolderType.EmailIncoming
                    && owner is OwnerType.Customer or OwnerType.User => LibrarySource.Mail,
            FileVisibilityResourceTypes.Conversation
                when role is AttachmentRole or VoiceNoteRole
                    && (
                        (owner == OwnerType.Communication && folder == FolderType.Other)
                        || folder == FolderType.VoiceNotes
                    ) => LibrarySource.Chat,
            FileVisibilityResourceTypes.Call
            or FileVisibilityResourceTypes.Meeting
                when role is RecordingRole or TranscriptRole
                    && owner == OwnerType.Communication
                    && folder is FolderType.Recordings or FolderType.Transcripts => LibrarySource.Recordings,
            FileVisibilityResourceTypes.Note
                when role == AttachmentRole
                    && owner is OwnerType.Customer or OwnerType.Tenant
                    && folder == FolderType.Other => LibrarySource.Notes,
            _ => null,
        };

    public static LibrarySource? Classify(LibraryRow row) =>
        Classify(row.OwnerType, row.FolderType, row.SourceResourceType, row.SourceRole);

    public static string KindOf(LibrarySource source, OwnerType owner, string? role, string? resourceType = null) =>
        source switch
        {
            LibrarySource.Documents => owner == OwnerType.Tenant ? "office_document" : "document",
            LibrarySource.Signature => role switch
            {
                CertificateRole => "signature_certificate",
                OriginalRole => "unsigned_document",
                _ => "signed_document",
            },
            LibrarySource.Billing => "invoice_pdf",
            LibrarySource.Financial => "bank_statement",
            LibrarySource.Tasks => "task_attachment",
            LibrarySource.Forms => role == SummaryPdfRole ? "form_summary" : "form_attachment",
            LibrarySource.Mail => role == IncomingAttachmentRole ? "email_attachment_received" : "email_attachment",
            LibrarySource.Chat => role == VoiceNoteRole ? "voice_note" : "chat_attachment",
            LibrarySource.Recordings => $"{resourceType}_{role}",
            _ => "note_attachment",
        };

    /// <summary>Las grabaciones se abren en el reproductor; todo lo demás, en el visor.</summary>
    public static string PreviewOf(LibrarySource source, string? role) =>
        source == LibrarySource.Recordings && role == RecordingRole
            ? LibraryPreviews.Recording
            : LibraryPreviews.Viewer;

    /// <summary>Destino de "Open in": el documento mismo o el recurso que lo declaró.</summary>
    public static LibraryOpenIn OpenInOf(LibrarySource source, Guid fileId, Guid? resourceId, Guid? customerId) =>
        source switch
        {
            LibrarySource.Documents => new LibraryOpenIn("document", fileId, customerId, null),
            LibrarySource.Signature => new LibraryOpenIn(
                SignatureResourceType,
                resourceId ?? Guid.Empty,
                customerId,
                null
            ),
            LibrarySource.Billing => new LibraryOpenIn(InvoiceResourceType, resourceId ?? Guid.Empty, customerId, null),
            _ => new LibraryOpenIn(FinancialResourceType, resourceId ?? Guid.Empty, customerId, null),
        };

    /// <summary>En los orígenes por fila "Open in" lo devuelve el dueño: puede cambiar de una persona a otra.</summary>
    public static LibraryOpenIn OpenInOf(VisibleFile visible) =>
        new(visible.ResourceType, visible.ResourceId, visible.CustomerId, visible.Context);
}
