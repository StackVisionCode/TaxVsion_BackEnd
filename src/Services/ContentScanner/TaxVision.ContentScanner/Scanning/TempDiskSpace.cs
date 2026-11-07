namespace TaxVision.ContentScanner.Scanning;

/// <summary>Espacio libre donde se escribe el cuerpo; separado para poder simular un disco lleno.</summary>
public interface ITempDiskSpace
{
    long AvailableBytes(string directory);
}

internal sealed class DriveTempDiskSpace : ITempDiskSpace
{
    public long AvailableBytes(string directory) => new DriveInfo(Path.GetFullPath(directory)).AvailableFreeSpace;
}
