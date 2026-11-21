namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Enciende la compuerta de lectura por origen (<see cref="FileReadGate"/>) para el personal. Encendida por defecto.</summary>
public sealed class CloudStorageSourceReadGateOptions
{
    public const string SectionName = "CloudStorage:SourceReadGate";

    public bool Enabled { get; set; } = true;
}
