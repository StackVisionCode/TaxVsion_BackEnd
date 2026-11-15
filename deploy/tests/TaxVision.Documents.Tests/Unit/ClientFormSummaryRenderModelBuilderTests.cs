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
        SignatureCopyRequest? signatureCopy = null,
        string zone = "Etc/UTC"
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
            new ProcessClientFormSummaryGenerationHandlerTests.OfficeZone(zone),
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
            WithoutNow(Snapshot(await RenderedDataAsync(Full(), branding))).ReplaceLineEndings()
        );
    }

    [Fact]
    public async Task Build_MinimalInput_UsesTheDefaults()
    {
        var snapshot = Snapshot(await RenderedDataAsync(Minimal(), null));

        // generatedOn es la hora real de generación.
        Assert.Equal(MinimalSnapshot.ReplaceLineEndings(), WithoutNow(snapshot).ReplaceLineEndings());
    }

    /// <summary>TZ08-H3: el resumen dice las horas en la zona de la oficina (si se firma, queda sellado así).</summary>
    [Fact]
    public async Task The_summary_dates_are_in_the_office_zone()
    {
        var snapshot = Snapshot(await RenderedDataAsync(Full(), null, zone: "America/New_York"));

        Assert.Contains("\"submittedAt\": \"Wed, Oct 28, 2026, 7:07 PM · New York time, GMT-4\"", snapshot);
        Assert.Contains("\"completedAt\": \"Wed, Oct 28, 2026, 7:09 PM · New York time, GMT-4\"", snapshot);
    }

    private static string WithoutNow(string snapshot) =>
        System.Text.RegularExpressions.Regex.Replace(
            snapshot,
            "\"generatedOn\": \"[^\"]+\"",
            "\"generatedOn\": \"<now>\""
        );

    [Fact]
    public void Build_CalledDirectly_UsesTheGivenClockWithoutACompletionDate()
    {
        var now = new DateTime(2026, 11, 6, 8, 30, 0, DateTimeKind.Utc);

        var data = ClientFormSummaryRenderModelBuilder.Build(
            Minimal(),
            null,
            "TaxOffix",
            false,
            now,
            BuildingBlocks.TimeZones.ResolvedTimeZone.Utc
        );

        Assert.Contains("\"generatedOn\": \"Fri, Nov 6, 2026, 8:30 AM · UTC\"", Snapshot(data));
    }

    private const string FullSnapshot = """
        {
          "summary": {
            "title": "Organizer 2026",
            "customerName": "Client Name",
            "versionNumber": 1,
            "revisionNumber": 2,
            "submittedAt": "Wed, Oct 28, 2026, 11:07 PM · UTC",
            "completedAt": "Wed, Oct 28, 2026, 11:09 PM · UTC",
            "generatedOn": "<now>",
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
