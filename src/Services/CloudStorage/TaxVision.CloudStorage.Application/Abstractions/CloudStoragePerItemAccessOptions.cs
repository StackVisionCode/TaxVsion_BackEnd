namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>
/// Flag per-item: cuando <see cref="Enabled"/> es true el portal del cliente precarga sus
/// <c>CustomerDocumentAccessRule</c> y las aplica como deny en lectura/mutación; staff no se toca.
/// Default FALSE — cero reglas aplicadas hasta encender.
/// </summary>
public sealed class CloudStoragePerItemAccessOptions
{
    public const string SectionName = "CloudStorage:PerItemAccess";

    public bool Enabled { get; set; }
}
