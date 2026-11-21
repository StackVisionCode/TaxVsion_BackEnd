namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Oculta a las personas los archivos que solo entrega su dueño (<see cref="FileReadGate.IsOwnerMediated(TaxVision.CloudStorage.Domain.Files.FileObject)"/>). Encendida por defecto.</summary>
public sealed class CloudStorageOwnerMediatedGateOptions
{
    public const string SectionName = "CloudStorage:OwnerMediatedGate";

    public bool Enabled { get; set; } = true;
}
