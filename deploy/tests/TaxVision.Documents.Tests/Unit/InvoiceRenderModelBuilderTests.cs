using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Domain.Branding;
using Xunit;
using static TaxVision.Documents.Tests.Unit.ProcessGenerationTestKit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>DOC-7. El diccionario de render de la factura, tal como salía del handler antes de extraerlo.</summary>
public sealed class InvoiceRenderModelBuilderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static InvoicePayload Full() =>
        new(
            "USD",
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 12, 3),
            new InvoiceParty(
                "Issuer LLC",
                "12-3456789",
                "1 Main St",
                " 555-0100 ",
                " billing@office.test ",
                " office.test "
            ),
            new InvoiceParty("Client", "", null),
            [new InvoiceLine("Tax prep", 1.5m, 1350.5m, 2025.75m), new InvoiceLine("Filing", 2m, 10m, 20m)],
            2045.75m,
            10.1m,
            2055.85m,
            "Thanks",
            Status: "Pending",
            PaymentUrl: "https://office.taxoffix.io/pay/abc",
            ReceiptNumber: "R-1",
            ReceiptHash: "abc123",
            Discount: 5m,
            Adjustments: [new InvoiceAdjustment("Referral", -5m)],
            SettlementType: "Mixed",
            PaymentTerms: " Net 30 ",
            PaymentInstructions: " Zelle ",
            AmountPaid: 100m,
            AmountDue: 1955.85m
        );

    private static InvoicePayload Minimal() =>
        new(
            "USD",
            new DateOnly(2026, 11, 3),
            null,
            new InvoiceParty("Issuer LLC", "12-3456789", null),
            new InvoiceParty("Client", "", null),
            [],
            0m,
            0m,
            0m,
            null,
            Status: " "
        );

    private static ProcessInvoiceGenerationCommand Command(
        InvoicePayload invoice,
        BrandingPayload? branding,
        Guid generationId
    ) =>
        new(
            generationId,
            Tenant,
            "INV-2026-00042",
            "billing.invoice.v1",
            1,
            "Invoice",
            Guid.NewGuid(),
            1,
            2026,
            "invoice.pdf",
            "corr",
            invoice,
            branding
        );

    // Pasa por el handler: lo que llega al renderer es lo que se imprime.
    private static async Task<string> RenderedDataAsync(InvoicePayload invoice, BrandingPayload? branding)
    {
        var generation = NewRequested(Tenant, "Invoice", "billing.invoice.v1", "Invoice");
        var journal = new Journal();
        var renderer = new Renderer();

        await ProcessInvoiceGenerationHandler.Handle(
            Command(invoice, branding, generation.Id),
            new Generations(generation),
            new Branding(),
            new Logos(),
            renderer,
            new Pdf(),
            new Storage(),
            new Qr(),
            new UnitOfWork(journal),
            new Bus(journal),
            new Correlation(),
            TimeProvider.System,
            TestPlatform.Settings,
            NullLogger<ProcessInvoiceGenerationCommand>.Instance,
            CancellationToken.None
        );

        return Snapshot(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(renderer.Data));
    }

    [Fact]
    public async Task Build_SameInput_ProducesSameDictionaryAsBefore()
    {
        var branding = new BrandingPayload("Office", "data:image/png;base64,AAAA", "#123abc", "Branding footer");

        Assert.Equal(
            FullSnapshot.ReplaceLineEndings(),
            (await RenderedDataAsync(Full(), branding)).ReplaceLineEndings()
        );
    }

    [Fact]
    public async Task Build_MinimalInput_UsesTheDefaults()
    {
        Assert.Equal(
            MinimalSnapshot.ReplaceLineEndings(),
            (await RenderedDataAsync(Minimal(), null)).ReplaceLineEndings()
        );
    }

    [Fact]
    public async Task Build_PaidInvoice_OffersNoPayment_AndRejectsUnsafeBranding()
    {
        var invoice = Full() with
        {
            Status = "Paid",
            PaidDate = new DateOnly(2026, 11, 5),
            InvoiceFooter = " Thanks & bye\nSee you ",
        };
        var branding = new BrandingPayload(" ", "https://evil.test/logo.png", "red", "Branding footer");

        Assert.Equal(
            PaidSnapshot.ReplaceLineEndings(),
            (await RenderedDataAsync(invoice, branding)).ReplaceLineEndings()
        );
    }

    [Fact]
    public async Task Build_NonHttpPaymentUrl_DrawsNoButton()
    {
        var snapshot = await RenderedDataAsync(Full() with { PaymentUrl = "javascript:alert(1)" }, null);

        Assert.Contains("\"paymentUrl\": \"\"", snapshot);
        Assert.Contains("\"paymentQr\": \"\"", snapshot);
    }

    [Fact]
    public void Build_CalledDirectly_MatchesTheSnapshot()
    {
        var branding = new BrandingPayload("Office", "data:image/png;base64,AAAA", "#123abc", "Branding footer");

        var data = InvoiceRenderModelBuilder.Build(
            Command(Full(), branding, Guid.NewGuid()),
            branding,
            "TaxOffix",
            new Qr()
        );

        Assert.Equal(FullSnapshot.ReplaceLineEndings(), Snapshot(data).ReplaceLineEndings());
    }

    [Fact]
    public void MergeBranding_RequestWinsFieldByField_AndStoredFillsTheRest()
    {
        var stored = DocumentBranding
            .Create(Tenant, "Stored", "data:image/png;base64,S", "#111111", "Stored footer", DateTime.UtcNow)
            .Value;

        var merged = InvoiceRenderModelBuilder.MergeBranding(
            new BrandingPayload(DisplayName: "Request", FooterText: " "),
            stored
        );

        Assert.Equal(new BrandingPayload("Request", "data:image/png;base64,S", "#111111", "Stored footer"), merged);
        Assert.Null(InvoiceRenderModelBuilder.MergeBranding(null, null));
    }

    private const string FullSnapshot = """
        {
          "invoice": {
            "number": "INV-2026-00042",
            "taxYear": 2026,
            "currency": "USD",
            "issueDate": "2026-11-03",
            "dueDate": "2026-12-03",
            "issuer": {
              "name": "Issuer LLC",
              "taxId": "12-3456789",
              "address": "1 Main St",
              "phone": "555-0100",
              "email": "billing@office.test",
              "website": "office.test"
            },
            "customer": {
              "name": "Client",
              "taxId": "",
              "address": "",
              "phone": "",
              "email": "",
              "website": ""
            },
            "lines": [
              {
                "description": "Tax prep",
                "quantity": "1.5",
                "unitPrice": "1,350.50",
                "amount": "2,025.75"
              },
              {
                "description": "Filing",
                "quantity": "2",
                "unitPrice": "10.00",
                "amount": "20.00"
              }
            ],
            "subtotal": "2,045.75",
            "taxAmount": "10.10",
            "total": "2,055.85",
            "notes": "Thanks",
            "adjustments": [
              {
                "label": "Referral",
                "amount": "-5.00"
              }
            ],
            "discount": "5.00",
            "hasDiscount": true,
            "settlementType": "Mixed",
            "status": "Pending",
            "paidDate": "",
            "paymentUrl": "https://office.taxoffix.io/pay/abc",
            "paymentQr": "data:image/png;base64,QR(https://office.taxoffix.io/pay/abc)",
            "receiptNumber": "R-1",
            "receiptHash": "abc123",
            "paymentTerms": "Net 30",
            "paymentInstructions": "Zelle",
            "hasPayments": true,
            "amountPaid": "100.00",
            "amountDue": "1,955.85",
            "logo": "data:image/png;base64,AAAA",
            "brandColor": "#123abc",
            "displayName": "Office",
            "footer": "Branding footer"
          }
        }
        """;
    private const string MinimalSnapshot = """
        {
          "invoice": {
            "number": "INV-2026-00042",
            "taxYear": 2026,
            "currency": "USD",
            "issueDate": "2026-11-03",
            "dueDate": "",
            "issuer": {
              "name": "Issuer LLC",
              "taxId": "12-3456789",
              "address": "",
              "phone": "",
              "email": "",
              "website": ""
            },
            "customer": {
              "name": "Client",
              "taxId": "",
              "address": "",
              "phone": "",
              "email": "",
              "website": ""
            },
            "lines": [],
            "subtotal": "0.00",
            "taxAmount": "0.00",
            "total": "0.00",
            "notes": "",
            "adjustments": [],
            "discount": "0.00",
            "hasDiscount": false,
            "settlementType": "",
            "status": "Pending",
            "paidDate": "",
            "paymentUrl": "",
            "paymentQr": "",
            "receiptNumber": "",
            "receiptHash": "",
            "paymentTerms": "",
            "paymentInstructions": "",
            "hasPayments": false,
            "amountPaid": "0.00",
            "amountDue": "0.00",
            "logo": "",
            "brandColor": "#2563eb",
            "displayName": "Issuer LLC",
            "footer": "Document generated by TaxOffix · Invoice INV-2026-00042"
          }
        }
        """;
    private const string PaidSnapshot = """
        {
          "invoice": {
            "number": "INV-2026-00042",
            "taxYear": 2026,
            "currency": "USD",
            "issueDate": "2026-11-03",
            "dueDate": "2026-12-03",
            "issuer": {
              "name": "Issuer LLC",
              "taxId": "12-3456789",
              "address": "1 Main St",
              "phone": "555-0100",
              "email": "billing@office.test",
              "website": "office.test"
            },
            "customer": {
              "name": "Client",
              "taxId": "",
              "address": "",
              "phone": "",
              "email": "",
              "website": ""
            },
            "lines": [
              {
                "description": "Tax prep",
                "quantity": "1.5",
                "unitPrice": "1,350.50",
                "amount": "2,025.75"
              },
              {
                "description": "Filing",
                "quantity": "2",
                "unitPrice": "10.00",
                "amount": "20.00"
              }
            ],
            "subtotal": "2,045.75",
            "taxAmount": "10.10",
            "total": "2,055.85",
            "notes": "Thanks",
            "adjustments": [
              {
                "label": "Referral",
                "amount": "-5.00"
              }
            ],
            "discount": "5.00",
            "hasDiscount": true,
            "settlementType": "Mixed",
            "status": "Paid",
            "paidDate": "2026-11-05",
            "paymentUrl": "",
            "paymentQr": "",
            "receiptNumber": "R-1",
            "receiptHash": "abc123",
            "paymentTerms": "Net 30",
            "paymentInstructions": "Zelle",
            "hasPayments": true,
            "amountPaid": "100.00",
            "amountDue": "1,955.85",
            "logo": "",
            "brandColor": "#2563eb",
            "displayName": "Issuer LLC",
            "footer": "Thanks &amp; bye<br />See you"
          }
        }
        """;
}
