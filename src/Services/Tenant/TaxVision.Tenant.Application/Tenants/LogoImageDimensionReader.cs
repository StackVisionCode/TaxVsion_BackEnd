using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TaxVision.Tenant.Application.Tenants;

/// <summary>
/// Lee ancho/alto en pixeles de un logo (PNG/JPEG/SVG — los 3 tipos que
/// Tenant.AllowedLogoContentTypes permite) parseando solo la cabecera, sin decodificar la imagen
/// completa y sin ninguna dependencia externa (usuario pidio explicitamente evitar una libreria
/// pesada tipo ImageSharp para esto). Best-effort a proposito: cualquier byte inesperado, formato
/// no reconocido o SVG sin width/height/viewBox devuelve (null, null) en vez de lanzar — esto es
/// metadata derivada opcional, jamas debe bloquear un upload que ya paso la validacion de
/// contentType/tamaño del aggregate (Tenant.ValidateLogo). Ver UploadTenantLogoHandler.
/// </summary>
public static class LogoImageDimensionReader
{
    public static (int? Width, int? Height) TryRead(byte[] content, string contentType)
    {
        try
        {
            return contentType switch
            {
                "image/png" => ReadPng(content),
                "image/jpeg" => ReadJpeg(content),
                "image/svg+xml" => ReadSvg(content),
                _ => (null, null),
            };
        }
        catch
        {
            // Nunca debe propagar — ver doc-comment de la clase.
            return (null, null);
        }
    }

    /// <summary>
    /// PNG: firma de 8 bytes + el chunk IHDR (siempre el primero, obligatorio por spec) con
    /// width/height como uint32 big-endian en los offsets 16-19 / 20-23.
    /// </summary>
    private static (int?, int?) ReadPng(byte[] data)
    {
        if (data.Length < 24)
            return (null, null);

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!data.AsSpan(0, 8).SequenceEqual(signature))
            return (null, null);

        if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
            return (null, null);

        return (ReadUInt32BigEndian(data, 16), ReadUInt32BigEndian(data, 20));
    }

    private static int ReadUInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    /// <summary>
    /// JPEG: camina los marcadores (FF xx) desde el SOI hasta encontrar un SOF (Start Of Frame —
    /// 0xC0-0xCF salvo 0xC4/0xC8/0xCC, que son DHT/JPG-reservado/DAC, no frames). El segmento SOF
    /// trae precision(1) + height(2 BE) + width(2 BE). Si el archivo termina antes de un SOF (raro,
    /// JPEG invalido/truncado), devuelve null sin reventar.
    /// </summary>
    private static (int?, int?) ReadJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return (null, null);

        var i = 2;
        while (i + 1 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker == 0xFF) // byte de relleno entre marcadores
            {
                i++;
                continue;
            }
            if (marker is 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) // SOI/EOI/RSTn: sin campo de longitud
            {
                i += 2;
                continue;
            }
            if (marker == 0xDA) // SOS: empieza la data entropica, ya no hay mas marcadores utiles antes
                break;
            if (i + 4 > data.Length)
                break;

            var segmentLength = (data[i + 2] << 8) | data[i + 3];
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                if (i + 9 > data.Length)
                    return (null, null);
                var height = (data[i + 5] << 8) | data[i + 6];
                var width = (data[i + 7] << 8) | data[i + 8];
                return (width, height);
            }

            i += 2 + segmentLength;
        }

        return (null, null);
    }

    /// <summary>
    /// SVG: XML, se lee el elemento raiz. Prioriza width/height explicitos (ignora "%", que no da
    /// un pixel real); si faltan, cae al viewBox ("minX minY width height"). XmlResolver=null +
    /// DtdProcessing.Prohibit por las dudas — es contenido subido por el usuario, no hay motivo para
    /// resolver DTDs/entidades externas (mismo criterio de higiene que cualquier parseo de XML no
    /// confiable, aunque acá no se usa el resultado para nada sensible mas alla de dos numeros).
    /// </summary>
    private static (int?, int?) ReadSvg(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var stringReader = new StringReader(text);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        var root = XDocument.Load(xmlReader).Root;
        if (root is null)
            return (null, null);

        var width = ParseSvgLength((string?)root.Attribute("width"));
        var height = ParseSvgLength((string?)root.Attribute("height"));
        if (width is not null && height is not null)
            return (width, height);

        var viewBox = (string?)root.Attribute("viewBox");
        if (string.IsNullOrWhiteSpace(viewBox))
            return (width, height);

        var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (
            parts.Length == 4
            && double.TryParse(parts[2], CultureInfo.InvariantCulture, out var viewBoxWidth)
            && double.TryParse(parts[3], CultureInfo.InvariantCulture, out var viewBoxHeight)
        )
            return ((int)Math.Round(viewBoxWidth), (int)Math.Round(viewBoxHeight));

        return (width, height);
    }

    private static int? ParseSvgLength(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('%'))
            return null;

        var numericPart = new string(raw.TrimStart().TakeWhile(c => char.IsAsciiDigit(c) || c is '.' or '-').ToArray());
        return double.TryParse(numericPart, CultureInfo.InvariantCulture, out var value)
            ? (int)Math.Round(value)
            : null;
    }
}
