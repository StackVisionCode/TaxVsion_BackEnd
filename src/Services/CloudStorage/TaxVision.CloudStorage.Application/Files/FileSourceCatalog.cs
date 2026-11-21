using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files;

/// <summary>
/// Pares (recurso, papel) que un servicio dueño puede declarar, con los dueños y la carpeta que admiten.
/// Una declaración que no encaja se descarta: así no saca un archivo del origen al que pertenece.
/// </summary>
public static class FileSourceCatalog
{
    private sealed record Entry(
        string Service,
        string ResourceType,
        IReadOnlySet<string> Roles,
        IReadOnlySet<OwnerType> Owners,
        FolderType? Folder
    );

    private static readonly Entry[] Entries =
    [
        new(
            "signature",
            "signature_request",
            new HashSet<string> { "sealed", "certificate", "sealed_master", "certificate_master", "partial_copy" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.Signature },
            FolderType.Signatures
        ),
        // Original a firmar: el CRM (y la copia de un formulario) lo deja en Signature/Signatures. El reemplazado o
        // quitado de la solicitud se re-declara como original_superseded y no se lista.
        new(
            "signature",
            "signature_request",
            new HashSet<string> { "original", "original_superseded" },
            new HashSet<OwnerType> { OwnerType.Signature },
            FolderType.Signatures
        ),
        new(
            "billing",
            "invoice",
            new HashSet<string> { "invoice_pdf", "invoice_pdf_superseded" },
            new HashSet<OwnerType> { OwnerType.Invoice },
            FolderType.Invoices
        ),
        // El estado de cuenta es un archivo que el usuario subió: vive en cualquier carpeta del cliente.
        new(
            "financial_insights",
            "financial_import_batch",
            new HashSet<string> { "statement" },
            new HashSet<OwnerType> { OwnerType.Customer },
            null
        ),
        new(
            "tasks",
            "task",
            new HashSet<string> { "attachment" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.User },
            FolderType.Tasks
        ),
        new(
            "tasks",
            "form_request",
            new HashSet<string> { "attachment", "summary_pdf" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.FormParticipant },
            FolderType.ClientForms
        ),
        new(
            "correspondence",
            "email_message",
            new HashSet<string> { "outgoing_attachment" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.User },
            FolderType.EmailOutgoing
        ),
        // Adjunto entrante ya descargado: con cliente en su carpeta Email, sin cliente a nombre de quien lo abrió.
        new(
            "correspondence",
            "incoming_email",
            new HashSet<string> { "incoming_attachment" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.User },
            FolderType.EmailIncoming
        ),
        // Chat: cada papel con su dueño y su carpeta (las notas de voz antiguas viven en Tenant/VoiceNotes).
        new(
            "communication",
            "conversation",
            new HashSet<string> { "attachment" },
            new HashSet<OwnerType> { OwnerType.Communication },
            FolderType.Other
        ),
        new(
            "communication",
            "conversation",
            new HashSet<string> { "voice_note" },
            new HashSet<OwnerType> { OwnerType.Tenant, OwnerType.Communication },
            FolderType.VoiceNotes
        ),
        // Grabaciones: el archivo en Recordings; los transcripts (.txt y .json) en Transcripts.
        .. RecordingEntries("call"),
        .. RecordingEntries("meeting"),
        // Adjunto de nota: el CRM lo sube al cliente de la nota o a la oficina, carpeta Other.
        new(
            "notes",
            "note",
            new HashSet<string> { "attachment" },
            new HashSet<OwnerType> { OwnerType.Customer, OwnerType.Tenant },
            FolderType.Other
        ),
    ];

    private static Entry[] RecordingEntries(string resourceType) =>
        [
            new(
                "communication",
                resourceType,
                new HashSet<string> { "recording" },
                new HashSet<OwnerType> { OwnerType.Communication },
                FolderType.Recordings
            ),
            new(
                "communication",
                resourceType,
                new HashSet<string> { "transcript", "transcript_data" },
                new HashSet<OwnerType> { OwnerType.Communication },
                FolderType.Transcripts
            ),
        ];

    public static bool Accepts(string? resourceType, string? role, OwnerType owner, FolderType folder) =>
        role is not null
        && Entries.Any(entry =>
            string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal)
            && entry.Roles.Contains(role)
            && entry.Owners.Contains(owner)
            && (entry.Folder is null || entry.Folder == folder)
        );

    /// <summary>Servicio dueño de un tipo de recurso, o null si no se conoce.</summary>
    public static string? ServiceOf(string? resourceType) => Find(resourceType)?.Service;

    private static Entry? Find(string? resourceType) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal));
}
