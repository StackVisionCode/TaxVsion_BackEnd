using System.Globalization;
using System.Text;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Resultado de agregar el bloque incremental al PDF: bytes finales + posiciones
/// exactas (offset y longitud) de <c>/ByteRange [...]</c> y <c>/Contents &lt;...&gt;</c>
/// dentro del array, listas para reescritura byte-level.
/// </summary>
public readonly record struct IncrementalSignatureLayout(
    byte[] PdfWithPlaceholders,
    int ByteRangeOffset,
    int ByteRangeLength,
    int ContentsOffset,
    int ContentsLength
);

/// <summary>
/// Toma un PDF ya generado por PdfSharp y le agrega un incremental update con:
/// (1) un objeto Signature Dictionary con placeholders <c>/ByteRange</c> y
/// <c>/Contents</c>, (2) un Widget Field vacio referenciando esa sig, (3) el
/// AcroForm root, (4) el Catalog actualizado apuntando al nuevo AcroForm, (5) una
/// tabla xref nueva subseccion, y (6) un trailer con <c>/Prev</c> al xref anterior.
///
/// <para>
/// El PDF resultante es un incremental update valido segun PDF 32000-1 §7.5.6 —
/// los verificadores reconstruyen la revision "sellada" siguiendo la cadena
/// <c>/Prev</c>. La responsabilidad de este componente es *solo* materializar los
/// bytes; el calculo de ByteRange y CMS-signing viven en otras clases.
/// </para>
/// </summary>
public sealed class IncrementalSignatureAppender(PadesOptions options)
{
    private const int ObjectsAdded = 4;

    public IncrementalSignatureLayout Append(byte[] originalPdfBytes, PdfTrailerInfo trailer)
    {
        ArgumentNullException.ThrowIfNull(originalPdfBytes);

        var truncatedLength = (int)trailer.EofOffset;
        var contentsReserved = options.ContentsReservedBytes;
        var baseObjectNumber = trailer.PrevSize > 0 ? (int)trailer.PrevSize : 100;

        var sigObj = baseObjectNumber;
        var fieldObj = baseObjectNumber + 1;
        var acroFormObj = baseObjectNumber + 2;
        var catalogObj = baseObjectNumber + 3;

        var writer = new IncrementalWriter(originalPdfBytes, truncatedLength);

        var sigLayout = writer.WriteSignatureObject(sigObj, options, contentsReserved);
        var fieldOffset = writer.WriteFieldObject(fieldObj, sigObj);
        var acroFormOffset = writer.WriteAcroFormObject(acroFormObj, fieldObj);
        var catalogOffset = writer.WriteCatalogObject(catalogObj, acroFormObj);

        var xrefOffset = writer.Position;
        writer.WriteXref(
            baseObjectNumber,
            [sigLayout.ObjectOffset, fieldOffset, acroFormOffset, catalogOffset],
            trailer,
            catalogObj
        );
        writer.WriteTrailerAndEof(baseObjectNumber + ObjectsAdded, trailer, xrefOffset);

        var finalBytes = writer.ToArray();
        return new IncrementalSignatureLayout(
            PdfWithPlaceholders: finalBytes,
            ByteRangeOffset: sigLayout.ByteRangeOffset,
            ByteRangeLength: sigLayout.ByteRangeLength,
            ContentsOffset: sigLayout.ContentsOffset,
            ContentsLength: sigLayout.ContentsLength
        );
    }

    // ------------------------------------------------------------------

    private sealed class IncrementalWriter
    {
        private readonly MemoryStream _stream;

        public IncrementalWriter(byte[] originalPdf, int truncatedLength)
        {
            _stream = new MemoryStream(originalPdf.Length + 32 * 1024);
            _stream.Write(originalPdf, 0, truncatedLength);
            EnsureNewline();
        }

        public int Position => (int)_stream.Position;

        public (
            int ObjectOffset,
            int ByteRangeOffset,
            int ByteRangeLength,
            int ContentsOffset,
            int ContentsLength
        ) WriteSignatureObject(int objectNumber, PadesOptions options, int contentsReserved)
        {
            var objOffset = Position;
            WriteAscii(
                $"{objectNumber} 0 obj\n<<\n/Type /Sig\n/Filter /{options.Filter}\n/SubFilter /{options.SubFilter}\n"
            );
            WriteOptionalString("/Reason", options.Reason);
            WriteOptionalString("/Location", options.Location);
            WriteOptionalString("/ContactInfo", options.ContactInfo);
            WriteAscii($"/M (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z)\n");

            WriteAscii("/ByteRange [");
            var byteRangeOffset = Position;
            // Placeholder anchura fija de 40 chars: cuatro enteros "0000000000".
            const string placeholder = "0000000000 0000000000 0000000000 0000000000";
            WriteAscii(placeholder);
            var byteRangeLength = Position - byteRangeOffset;
            WriteAscii("]\n/Contents <");

            var contentsOffset = Position;
            WriteZeroHex(contentsReserved * 2);
            var contentsLength = Position - contentsOffset;
            WriteAscii(">\n>>\nendobj\n");

            return (objOffset, byteRangeOffset, byteRangeLength, contentsOffset, contentsLength);
        }

        public int WriteFieldObject(int objectNumber, int sigObj)
        {
            var offset = Position;
            WriteAscii(
                $"{objectNumber} 0 obj\n<<\n/Type /Annot\n/Subtype /Widget\n/FT /Sig\n/T (TaxVision.Signature)\n/F 132\n/Rect [0 0 0 0]\n/V {sigObj} 0 R\n>>\nendobj\n"
            );
            return offset;
        }

        public int WriteAcroFormObject(int objectNumber, int fieldObj)
        {
            var offset = Position;
            WriteAscii($"{objectNumber} 0 obj\n<<\n/Fields [{fieldObj} 0 R]\n/SigFlags 3\n>>\nendobj\n");
            return offset;
        }

        public int WriteCatalogObject(int objectNumber, int acroFormObj)
        {
            var offset = Position;
            WriteAscii($"{objectNumber} 0 obj\n<<\n/Type /Catalog\n/AcroForm {acroFormObj} 0 R\n>>\nendobj\n");
            return offset;
        }

        public void WriteXref(int firstObject, int[] offsets, PdfTrailerInfo trailer, int catalogObject)
        {
            _ = trailer;
            _ = catalogObject;
            WriteAscii($"xref\n{firstObject} {offsets.Length}\n");
            foreach (var offset in offsets)
                WriteAscii($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        public void WriteTrailerAndEof(int newSize, PdfTrailerInfo trailer, int xrefOffset)
        {
            var prevSize = Math.Max(newSize, trailer.PrevSize);
            WriteAscii(
                $"trailer\n<<\n/Size {prevSize}\n/Prev {trailer.StartXref}\n/Root {newSize - 1} 0 R\n>>\nstartxref\n{xrefOffset}\n%%EOF\n"
            );
        }

        public byte[] ToArray() => _stream.ToArray();

        private void EnsureNewline()
        {
            var pos = Position;
            if (pos == 0)
                return;
            var last = _stream.GetBuffer()[pos - 1];
            if (last is not ((byte)'\n' or (byte)'\r'))
                _stream.WriteByte((byte)'\n');
        }

        private void WriteAscii(string s)
        {
            var bytes = Encoding.Latin1.GetBytes(s);
            _stream.Write(bytes, 0, bytes.Length);
        }

        private void WriteOptionalString(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var escaped = value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            WriteAscii($"{key} ({escaped})\n");
        }

        private void WriteZeroHex(int chars)
        {
            const int block = 1024;
            Span<byte> zeros = stackalloc byte[block];
            zeros.Fill((byte)'0');
            var remaining = chars;
            while (remaining > 0)
            {
                var toWrite = Math.Min(block, remaining);
                _stream.Write(zeros[..toWrite]);
                remaining -= toWrite;
            }
        }
    }
}
