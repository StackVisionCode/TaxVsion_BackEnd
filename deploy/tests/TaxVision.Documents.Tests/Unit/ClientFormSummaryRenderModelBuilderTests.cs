using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Generations.ClientFormSummary;
using TaxVision.Documents.Domain.Branding;
using Xunit;
using static TaxVision.Documents.Tests.Unit.ProcessGenerationTestKit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>DOC-7. El diccionario de render del resumen, tal como salía del handler antes de extraerlo.</summary>
public sealed class ClientFormSummaryRenderModelBuilderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid FormRequest = Guid.NewGuid();

    private static SubmissionSummarySnapshot Full() =>
        new()
        {
            Title = "Organizer 2026",
            CustomerDisplayName = "Client Name",
            VersionNumber = 1,
            RevisionNumber = 2,
            SubmittedAtUtc = new DateTime(2026, 10, 28, 23, 7, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 10, 28, 23, 9, 0, DateTimeKind.Utc),
            Pages =
            [
                new SubmissionSummaryPage(
                    "Income",
                    [
                        new SubmissionSummarySection(
                            "W-2",
                            [new SubmissionSummaryItem("Employer", "ACME")],
                            [new SubmissionSummaryRow("Row 1", [new SubmissionSummaryItem("Wages", "100")])]
                        ),
                        new SubmissionSummarySection(null, [], []),
                    ]
                ),
                new SubmissionSummaryPage(null, []),
            ],
            Attachments = [new SubmissionSummaryAttachment("ID", "id.pdf")],
        };

    private static SubmissionSummarySnapshot Minimal() =>
        new()
        {
            Title = "Organizer 2026",
            VersionNumber = 1,
            RevisionNumber = 1,
        };

    private static async Task<IReadOnlyDictionary<string, object?>> RenderedDataAsync(
        SubmissionSummarySnapshot snapshot,
        DocumentBranding? branding,
        SignatureCopyRequest? signatureCopy = null
    )
    {
        var generation = NewRequested(
            Tenant,
            ClientFormSubmissionSummaryRequestedConsumer.DocumentType,
            ClientFormSubmissionSummaryRequestedConsumer.TemplateKey,
            GenerateClientFormSummaryDocumentHandler.OwnerTypeCustomer
        );
        var journal = new Journal();
        var renderer = new Renderer();

        await ProcessClientFormSummaryGenerationHandler.Handle(
            new ProcessClientFormSummaryGenerationCommand(
                generation.Id,
                Tenant,
                FormRequest,
                snapshot.RevisionNumber,
                new ClientFormParticipant { Kind = "Customer", CustomerId = Guid.NewGuid() },
                2026,
                "idem-key",
                "corr",
                new ProtectedPayload
                {
                    Cipher = "cipher",
                    KeyVersion = 1,
                    Schema = ClientFormProtectedPayloadSchemas.SubmissionSummary,
                },
                signatureCopy
            ),
            new Generations(generation),
            new ProcessClientFormSummaryGenerationHandlerTests.Codec(
                new SubmissionSummaryPayload
                {
                    TenantId = Tenant,
                    FormRequestId = FormRequest,
                    Snapshot = snapshot,
                }
            ),
            new Branding(branding),
            new Logos(),
            renderer,
            new Pdf(),
            new Storage(),
            new Pages(1),
            new UnitOfWork(journal),
            new Bus(journal),
            new Correlation(),
            TestPlatform.Settings,
            TimeProvider.System,
            NullLogger<ProcessClientFormSummaryGenerationCommand>.Instance,
            CancellationToken.None
        );

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(renderer.Data);
    }

    [Fact]
    public async Task Build_SameInput_ProducesSameDictionaryAsBefore()
    {
        var branding = DocumentBranding
            .Create(Tenant, "Office", "data:image/png;base64,AAAA", "#123abc", null, DateTime.UtcNow)
            .Value;

        Assert.Equal(
            FullSnapshot.ReplaceLineEndings(),
            Snapshot(await RenderedDataAsync(Full(), branding)).ReplaceLineEndings()
        );
    }

    [Fact]
    public async Task Build_MinimalInput_UsesTheDefaults()
    {
        var snapshot = Snapshot(await RenderedDataAsync(Minimal(), null));

        // generatedOn sale del reloj cuando el formulario no tiene fecha de cierre.
        var generatedOn = System.Text.RegularExpressions.Regex.Replace(
            snapshot,
            "\"generatedOn\": \"\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2} UTC\"",
            "\"generatedOn\": \"<now>\""
        );
        Assert.Equal(MinimalSnapshot.ReplaceLineEndings(), generatedOn.ReplaceLineEndings());
    }

    [Fact]
    public void Build_CalledDirectly_UsesTheGivenClockWithoutACompletionDate()
    {
        var now = new DateTime(2026, 11, 6, 8, 30, 0, DateTimeKind.Utc);

        var data = ClientFormSummaryRenderModelBuilder.Build(Minimal(), null, "TaxOffix", false, now);

        Assert.Contains("\"generatedOn\": \"2026-11-06 08:30 UTC\"", Snapshot(data));
    }

    private const string FullSnapshot = """
        {
          "summary": {
            "title": "Organizer 2026",
            "customerName": "Client Name",
            "versionNumber": 1,
            "revisionNumber": 2,
            "submittedAt": "2026-10-28 23:07 UTC",
            "completedAt": "2026-10-28 23:09 UTC",
            "generatedOn": "2026-10-28 23:09 UTC",
            "pages": [
              {
                "title": "Income",
                "sections": [
                  {
                    "title": "W-2",
                    "items": [
                      {
                        "label": "Employer",
                        "value": "ACME"
                      }
                    ],
                    "rows": [
                      {
                        "label": "Row 1",
                        "items": [
                          {
                            "label": "Wages",
                            "value": "100"
                          }
                        ]
                      }
                    ]
                  },
                  {
                    "title": "",
                    "items": [],
                    "rows": []
                  }
                ]
              },
              {
                "title": "",
                "sections": []
              }
            ],
            "attachments": [
              {
                "label": "ID",
                "fileName": "id.pdf"
              }
            ],
            "hasAttachments": true,
            "logo": "data:image/png;base64,AAAA",
            "brandColor": "#123abc",
            "displayName": "Office",
            "showSignatureBox": false
          }
        }
        """;
    private const string MinimalSnapshot = """
        {
          "summary": {
            "title": "Organizer 2026",
            "customerName": "",
            "versionNumber": 1,
            "revisionNumber": 1,
            "submittedAt": "",
            "completedAt": "",
            "generatedOn": "<now>",
            "pages": [],
            "attachments": [],
            "hasAttachments": false,
            "logo": "",
            "brandColor": "#0074d4",
            "displayName": "TaxOffix",
            "showSignatureBox": false
          }
        }
        """;
}
