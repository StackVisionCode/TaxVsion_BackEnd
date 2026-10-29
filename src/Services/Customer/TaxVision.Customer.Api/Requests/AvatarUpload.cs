using BuildingBlocks.Imaging;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Api.Requests;

/// <summary>
/// Lectura del multipart de la foto (campo <c>file</c>). El tope de la imagen vive en
/// <see cref="AvatarImageOptions"/>; el de la petición le suma margen para el sobre multipart.
/// </summary>
internal static class AvatarUpload
{
    public const long MaxRequestBytes = AvatarImageOptions.DefaultMaxInputBytes + 512 * 1024;

    public static async Task<Result<byte[]>> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Result.Failure<byte[]>(AvatarImageErrors.Empty);
        if (file.Length > AvatarImageOptions.DefaultMaxInputBytes)
            return Result.Failure<byte[]>(AvatarImageErrors.TooLarge);

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return Result.Success(buffer.ToArray());
    }
}
