using ClosedXML.Excel;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Plantilla XLSX del import (D-8): hoja de instrucciones, hoja Clients con listas desplegables y los ejemplos de
/// <see cref="CustomerImportTemplate"/>, y hoja Lists con los valores válidos. Las listas son estrictas, salvo la
/// ocupación: avisa y deja escribir otra, que la revisión del CRM empareja con el catálogo.
/// </summary>
public sealed class XlsxCustomerImportTemplate(ICustomerReadService readService) : ICustomerImportXlsxTemplate
{
    private const int LastDataRow = 10_001;

    // Columnas obligatorias según el tipo (se marcan en el encabezado y se explican en Instructions).
    private static readonly HashSet<string> Required =
    [
        "Kind",
        "FirstName",
        "LastName",
        "LegalName",
        "BusinessStructure",
    ];

    private static readonly string[] YesNo = ["yes", "no"];

    // Mismos sinónimos que acepta el import (FilingStatusNames).
    private static readonly string[] FilingStatusSynonyms =
    [
        "Married Filing Jointly",
        "MFJ",
        "Married Filing Separately",
        "MFS",
        "Head of Household",
        "HOH",
        "Qualifying Surviving Spouse",
        "QSS",
        "Qualifying Widow(er)",
        "Soltero",
    ];

    public async Task<byte[]> BuildAsync(CancellationToken ct)
    {
        var occupations = await readService.ListOccupationsAsync(null, ct);
        return Build(occupations.Select(o => o.Name).ToList());
    }

    public static byte[] Build(IReadOnlyList<string> occupations)
    {
        using var workbook = new XLWorkbook();
        AddInstructions(workbook.AddWorksheet("Instructions"));
        var clients = workbook.AddWorksheet("Clients");
        var lists = workbook.AddWorksheet("Lists");

        var columns = new Dictionary<string, (IReadOnlyList<string> Values, XLErrorStyle Style)>
        {
            ["Kind"] = (Enum.GetNames<CustomerKind>(), XLErrorStyle.Stop),
            ["BusinessStructure"] = (Enum.GetNames<BusinessStructure>(), XLErrorStyle.Stop),
            ["Language"] = (
                CustomerImportTemplate.OfferedLanguages.Select(l => l.ToString()).ToList(),
                XLErrorStyle.Stop
            ),
            ["FilingStatus"] = (Enum.GetNames<FilingStatus>(), XLErrorStyle.Stop),
            ["OccupationName"] = (occupations, XLErrorStyle.Warning),
            ["EmailOptIn"] = (YesNo, XLErrorStyle.Stop),
            ["SmsOptIn"] = (YesNo, XLErrorStyle.Stop),
        };

        WriteClients(clients);
        var listColumn = 1;
        foreach (var (header, (values, style)) in columns)
        {
            var range = WriteList(lists, listColumn++, header, values);
            AddDropdown(clients, header, range, style);
        }
        WriteList(lists, listColumn, "FilingStatus also accepts", FilingStatusSynonyms);

        clients.SheetView.FreezeRows(1);
        clients.Columns().AdjustToContents();
        lists.Columns().AdjustToContents();
        clients.SetTabActive();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteClients(IXLWorksheet sheet)
    {
        var headers = CustomerImportTemplate.Headers;
        for (var col = 0; col < headers.Count; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = Required.Contains(headers[col])
                ? XLColor.FromHtml("#FDE68A")
                : XLColor.FromHtml("#E5E7EB");
        }

        // Todo como texto: Excel no convierte fechas, SSN ni teléfonos.
        sheet.Range(2, 1, LastDataRow, headers.Count).Style.NumberFormat.Format = "@";
        for (var row = 0; row < CustomerImportTemplate.Examples.Count; row++)
        {
            var example = CustomerImportTemplate.Examples[row];
            for (var col = 0; col < example.Count; col++)
                if (example[col].Length > 0)
                    sheet.Cell(row + 2, col + 1).SetValue(example[col]);
        }
    }

    private static IXLRange WriteList(IXLWorksheet sheet, int column, string header, IReadOnlyList<string> values)
    {
        var title = sheet.Cell(1, column);
        title.Value = header;
        title.Style.Font.Bold = true;
        for (var i = 0; i < values.Count; i++)
            sheet.Cell(i + 2, column).SetValue(values[i]);
        return sheet.Range(2, column, Math.Max(2, values.Count + 1), column);
    }

    private static void AddDropdown(IXLWorksheet clients, string header, IXLRange list, XLErrorStyle style)
    {
        var column = CustomerImportTemplate.Headers.ToList().IndexOf(header) + 1;
        var validation = clients.Range(2, column, LastDataRow, column).CreateDataValidation();
        validation.List(list, true);
        validation.ErrorStyle = style;
        validation.ShowErrorMessage = true;
        validation.ErrorTitle = header;
        validation.ErrorMessage =
            style == XLErrorStyle.Stop
                ? "Pick a value from the list."
                : "This value is not in the catalog. You can keep it and match it during the review.";
    }

    private static void AddInstructions(IXLWorksheet sheet)
    {
        string[] lines =
        [
            "How to import your clients",
            "",
            "1. Fill the Clients sheet: one client per row. Keep the column names as they are.",
            "2. Required (yellow): Kind. Individual needs FirstName and LastName. Business needs LegalName and BusinessStructure.",
            "3. Email: optional. Without email, an Individual needs a Phone or a DateOfBirth, and a Business needs a Phone or a TaxIdentifier (EIN).",
            "4. Formats: dates as yyyy-MM-dd; phone with country code (+13055551234) or 10 US digits; SSN/ITIN/EIN with 9 digits.",
            "5. Spouse: fill SpouseFirstName and SpouseLastName (and optionally the rest) for a married individual.",
            "6. Lists: Kind, BusinessStructure, Language, FilingStatus and the opt-in columns only take the listed values.",
            "   Occupation suggests the catalog; another value is matched during the review or imported without it.",
            "7. Contact permissions: EmailOptIn and SmsOptIn take yes or no. Empty means email yes and SMS no.",
            "8. Optional values that can't be read are imported empty and reported as warnings.",
            "",
            "When a client already exists:",
            "- Skip: keep the existing client untouched and report the row as skipped.",
            "- Merge: fill in only what the existing client is missing. Never changes their email, contact permissions or tax ID on file; adds a spouse if they have none.",
            "- Overwrite: update the existing client with every value in the file. Empty cells never erase data, and the email is never removed.",
        ];
        for (var i = 0; i < lines.Length; i++)
            sheet.Cell(i + 1, 1).Value = lines[i];
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(13, 1).Style.Font.Bold = true;
        sheet.Column(1).Width = 120;
    }
}
