namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>Ciclo de vida técnico de una generación documental. Estado TÉCNICO (no comercial):
/// pertenece a Documents, nunca contamina el estado de negocio del servicio dueño (Billing, etc.).</summary>
public enum DocumentGenerationStatus
{
    Requested = 1,
    Queued = 2,
    Validating = 3,
    Rendering = 4,
    Transforming = 5,
    Packaging = 6,
    Uploading = 7,
    Stored = 8,
    Completed = 9,
    PartiallyCompleted = 10,
    Failed = 11,
    Cancelled = 12,
}

/// <summary>Formato de salida del documento. Extensible; MVP = Pdf.</summary>
public enum DocumentOutputFormat
{
    Pdf = 1,
    Zip = 2,
    Docx = 3,
    Xlsx = 4,
    Xml = 5,
}

/// <summary>Prioridad de la generación. No todos los servicios pueden pedir Critical sin autorización.</summary>
public enum DocumentPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Modo de empaquetado de un lote.</summary>
public enum DocumentPackageMode
{
    IndividualFiles = 1,
    ZipPackage = 2,
    MergedPdf = 3,
}

/// <summary>Estado de un lote de generación.</summary>
public enum DocumentBatchStatus
{
    Requested = 1,
    Processing = 2,
    Completed = 3,
    PartiallyCompleted = 4,
    Failed = 5,
    Cancelled = 6,
}
