using BuildingBlocks.Results;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Application.Messages;

/// <summary>Valida la media de un envío contra las capabilities del proveedor. Regla dura: si hay media
/// y el proveedor no la soporta (o excede límites/MIME), FALLA explícitamente — nunca se degrada a texto.</summary>
public static class SmsMediaValidator
{
    /// <summary>Devuelve el <see cref="Error"/> canónico si la media no es válida para el proveedor; null si OK.</summary>
    public static Error? Validate(SmsProviderCapabilities capabilities, IReadOnlyList<SmsMediaPayload> media)
    {
        if (media.Count == 0)
            return null;

        if (!capabilities.SupportsMedia)
            return SmsErrors.MediaNotSupported;

        if (media.Count > 1 && !capabilities.SupportsMultipleMedia)
            return SmsErrors.MultipleMediaNotSupported;

        if (media.Count > capabilities.MaxMediaItems)
            return SmsErrors.MediaCountExceeded;

        foreach (var item in media)
        {
            if (
                item.SizeBytes is { } size
                && capabilities.MaxMediaSizeBytes > 0
                && size > capabilities.MaxMediaSizeBytes
            )
                return SmsErrors.MediaTooLarge;

            if (capabilities.AllowedMediaTypes.Count > 0 && !capabilities.AllowedMediaTypes.Contains(item.ContentType))
                return SmsErrors.MediaTypeNotSupported;
        }

        return null;
    }
}
