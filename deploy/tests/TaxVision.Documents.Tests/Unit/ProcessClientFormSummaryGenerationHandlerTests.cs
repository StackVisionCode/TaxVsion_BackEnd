using System.Security.Cryptography;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.ClientFormSummary;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Xunit;
using static TaxVision.Documents.Tests.Unit.ProcessGenerationTestKit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>
/// El pipeline del Submission Summary: payload cifrado → render → subida → SaveFileRequested, con sus fallos
/// terminales, la copia para Signature y el orden guardar-publicar.
/// </summary>
public sealed class ProcessClientFormSummaryGenerationHandlerTests
{
    private const string TemplateKey = ClientFormSubmissionSummaryRequestedConsumer.TemplateKey;
    private const string DocumentType = ClientFormSubmissionSummaryRequestedConsumer.DocumentType;
    private const string StartedEvent = "publish:DocumentGenerationStartedIntegrationEvent";
    private const string FailedEvent = "publish:DocumentGenerationFailedIntegrationEvent";
    private const string SaveFileEvent = "publish:SaveFileRequestedIntegrationEvent";

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid FormRequest = Guid.NewGuid();

    private sealed class Scenario
    {
        public DocumentGeneration Generation { get; } =
            NewRequested(Tenant, DocumentType, TemplateKey, GenerateClientFormSummaryDocumentHandler.OwnerTypeCustomer);
        public Journal Journal { get; } = new();
        public Logos Logos { get; } = new();
        public Renderer Renderer { get; init; } = new();
        public Pdf Pdf { get; init; } = new();
        public Storage Storage { get; init; } = new();
        public Pages Pages { get; init; } = new(3);
        public Codec Codec { get; init; } = new(Payload());
        public DocumentBranding? StoredBranding { get; init; }
        public SignatureCopyRequest? SignatureCopy { get; init; }
        public Bus? PublishedTo { get; private set; }
        public DocumentGeneration? Lookup { get; private set; }
        public bool Missing { get; init; }

        public IReadOnlyList<object> Published => PublishedTo!.Published;

        public async Task RunAsync()
        {
            var bus = new Bus(Journal);
            PublishedTo = bus;
            await ProcessClientFormSummaryGenerationHandler.Handle(
                new ProcessClientFormSummaryGenerationCommand(
                    Generation.Id,
                    Tenant,
                    FormRequest,
                    2,
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
                    SignatureCopy
                ),
                new Generations(Missing ? null : Generation),
                Codec,
                new Branding(StoredBranding),
                Logos,
                Renderer,
                Pdf,
                Storage,
                Pages,
                new UnitOfWork(Journal),
                bus,
                new Correlation(),
                TestPlatform.Settings,
                TimeProvider.System,
                NullLogger<ProcessClientFormSummaryGenerationCommand>.Instance,
                CancellationToken.None
            );
        }
    }

    internal sealed class Codec(SubmissionSummaryPayload? payload) : IProtectedPayloadCodec
    {
        public ProtectedPayloadBinding? Binding { get; private set; }
        public string? Schema { get; private set; }

        public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
            throw new NotSupportedException();

        public T Unprotect<T>(
            ProtectedPayload protectedPayload,
            string expectedSchema,
            ProtectedPayloadBinding expectedBinding
        )
        {
            Binding = expectedBinding;
            Schema = expectedSchema;
            return payload is null ? throw new CryptographicException("Invalid envelope.") : (T)(object)payload;
        }
    }

    private static SubmissionSummaryPayload Payload(string title = "Organizer 2026", Guid? tenantId = null) =>
        new()
        {
            TenantId = tenantId ?? Tenant,
            FormRequestId = FormRequest,
            Snapshot = new SubmissionSummarySnapshot
            {
                Title = title,
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
                        ]
                    ),
                ],
                Attachments = [new SubmissionSummaryAttachment("ID", "id.pdf")],
            },
        };

    [Fact]
    public async Task Unknown_generation_does_nothing()
    {
        var scenario = new Scenario { Missing = true };

        await scenario.RunAsync();

        Assert.Empty(scenario.Journal.Entries);
        Assert.Null(scenario.Codec.Schema);
    }

    [Fact]
    public async Task Redelivery_of_a_started_generation_does_nothing()
    {
        var scenario = new Scenario();
        var now = DateTime.UtcNow;
        scenario.Generation.Queue(now);
        scenario.Generation.StartRendering(now);

        await scenario.RunAsync();

        Assert.Empty(scenario.Journal.Entries);
        Assert.Null(scenario.Renderer.Data);
    }

    [Fact]
    public async Task Payload_is_opened_against_the_command_binding_and_schema()
    {
        var scenario = new Scenario();

        await scenario.RunAsync();

        Assert.Equal(ClientFormProtectedPayloadSchemas.SubmissionSummary, scenario.Codec.Schema);
        Assert.Equal(Tenant, scenario.Codec.Binding!.TenantId);
        Assert.Equal(FormRequest, scenario.Codec.Binding.FormRequestId);
        Assert.Equal("idem-key", scenario.Codec.Binding.IdempotencyKey);
    }

    [Fact]
    public async Task An_invalid_payload_is_a_terminal_failure_and_renders_nothing()
    {
        var scenario = new Scenario { Codec = new Codec(null) };

        await scenario.RunAsync();

        AssertFailed(scenario, "Documents.ClientFormSummary.InvalidPayload");
        Assert.Null(scenario.Renderer.Data);
    }

    [Fact]
    public async Task A_payload_of_another_tenant_is_a_terminal_failure()
    {
        var scenario = new Scenario { Codec = new Codec(Payload(tenantId: Guid.NewGuid())) };

        await scenario.RunAsync();

        AssertFailed(scenario, "Documents.ClientFormSummary.InvalidPayload");
    }

    [Fact]
    public async Task A_render_failure_fails_the_generation_before_any_upload()
    {
        var scenario = new Scenario { Renderer = new Renderer(new Error("Documents.Render", "Broken.")) };

        await scenario.RunAsync();

        AssertFailed(scenario, "Documents.Render");
        Assert.Empty(scenario.Storage.Uploads);
    }

    [Fact]
    public async Task A_pdf_failure_fails_the_generation_before_any_upload()
    {
        var scenario = new Scenario { Pdf = new Pdf(new Error("Documents.Pdf", "Broken.")) };

        await scenario.RunAsync();

        AssertFailed(scenario, "Documents.Pdf");
        Assert.Empty(scenario.Storage.Uploads);
    }

    [Fact]
    public async Task A_failed_upload_fails_the_generation_and_asks_nothing_of_CloudStorage()
    {
        var scenario = new Scenario { Storage = new Storage { FailOnUpload = 1 } };

        await scenario.RunAsync();

        Assert.Equal(DocumentGenerationStatus.Failed, scenario.Generation.Status);
        Assert.Empty(scenario.Published.OfType<SaveFileRequestedIntegrationEvent>());
        Assert.Equal("Documents.Storage.Upload", Assert.Single(Failures(scenario)).ErrorCode);
        Assert.Equal(["save", StartedEvent, "save", FailedEvent], scenario.Journal.Entries);
    }

    [Fact]
    public async Task Happy_path_saves_the_file_id_first_and_hands_the_pdf_to_CloudStorage()
    {
        var scenario = new Scenario();

        await scenario.RunAsync();

        Assert.Equal(["save", StartedEvent, SaveFileEvent], scenario.Journal.Entries);
        Assert.Equal(DocumentGenerationStatus.Uploading, scenario.Generation.Status);
        Assert.NotNull(scenario.Generation.ContentHash);
        Assert.Equal(TemplateKey, scenario.Renderer.TemplateKey);
        Assert.Equal(1, scenario.Renderer.TemplateVersion);
        Assert.Equal(Guid.Empty, scenario.Renderer.TenantId);

        var started = Assert.Single(scenario.Published.OfType<DocumentGenerationStartedIntegrationEvent>());
        Assert.Equal(DocumentType, started.DocumentType);
        Assert.Equal("corr", started.CorrelationId);

        var save = Assert.Single(scenario.Published.OfType<SaveFileRequestedIntegrationEvent>());
        Assert.Equal(scenario.Generation.FileId, save.FileId);
        Assert.Equal("Organizer 2026 - Submission summary (rev 2).pdf", save.OriginalName);
        Assert.Equal(scenario.Generation.Owner.OwnerType, save.OwnerType);
        Assert.Equal(scenario.Generation.Owner.OwnerId, save.OwnerId);
        Assert.Equal("ClientForms", save.FolderType);
        Assert.Equal(2026, save.TaxYear);
        Assert.Equal("application/pdf", save.ContentType);
        Assert.Equal(Tenant, save.ActorId);
    }

    [Theory]
    [InlineData("  ", "Form - Submission summary (rev 2).pdf")]
    [InlineData("a/b:c?", "a_b_c_ - Submission summary (rev 2).pdf")]
    public async Task The_file_name_is_safe(string title, string expected)
    {
        var scenario = new Scenario { Codec = new Codec(Payload(title)) };

        await scenario.RunAsync();

        Assert.Equal(
            expected,
            Assert.Single(scenario.Published.OfType<SaveFileRequestedIntegrationEvent>()).OriginalName
        );
    }

    [Fact]
    public async Task A_long_title_is_cut_to_120_characters()
    {
        var scenario = new Scenario { Codec = new Codec(Payload(new string('x', 200))) };

        await scenario.RunAsync();

        Assert.Equal(
            new string('x', 120) + " - Submission summary (rev 2).pdf",
            Assert.Single(scenario.Published.OfType<SaveFileRequestedIntegrationEvent>()).OriginalName
        );
    }

    [Fact]
    public async Task The_signature_copy_gets_its_own_file_and_every_publish_goes_after_the_last_save()
    {
        var scenario = new Scenario
        {
            SignatureCopy = new SignatureCopyRequest
            {
                OwnerType = "SignatureRequest",
                FolderType = "Signature",
                TaxYear = 2025,
            },
        };

        await scenario.RunAsync();

        Assert.Equal(["save", StartedEvent, "save", SaveFileEvent, SaveFileEvent], scenario.Journal.Entries);
        Assert.Equal(2, scenario.Storage.Uploads.Count);
        Assert.Equal(scenario.Storage.Uploads[1], scenario.Generation.SignatureCopyFileId);
        Assert.Equal(3, scenario.Generation.SignatureAnchorPage);
        Assert.Equal("SignatureRequest", scenario.Generation.SignatureCopyOwnerType);
        Assert.Equal("Signature", scenario.Generation.SignatureCopyFolderType);

        var saves = scenario.Published.OfType<SaveFileRequestedIntegrationEvent>().ToArray();
        Assert.Equal("ClientForms", saves[0].FolderType);
        Assert.Equal(scenario.Storage.Uploads[0], saves[0].FileId);
        Assert.Equal("Signature", saves[1].FolderType);
        Assert.Equal("SignatureRequest", saves[1].OwnerType);
        Assert.Equal(scenario.Generation.Owner.OwnerId, saves[1].OwnerId);
        Assert.Equal(2025, saves[1].TaxYear);
        Assert.Equal(scenario.Storage.Uploads[1], saves[1].FileId);
        Assert.Contains("\"showSignatureBox\": true", Snapshot(scenario.Renderer.Data!));
    }

    [Fact]
    public async Task Without_its_own_tax_year_the_signature_copy_uses_the_command_one()
    {
        var scenario = new Scenario
        {
            SignatureCopy = new SignatureCopyRequest { OwnerType = "SignatureRequest", FolderType = "Signature" },
        };

        await scenario.RunAsync();

        Assert.Equal(2026, scenario.Published.OfType<SaveFileRequestedIntegrationEvent>().Last().TaxYear);
    }

    [Fact]
    public async Task A_failed_signature_copy_is_not_announced_but_the_summary_still_goes_out()
    {
        var scenario = new Scenario
        {
            Storage = new Storage { FailOnUpload = 2 },
            SignatureCopy = new SignatureCopyRequest { OwnerType = "SignatureRequest", FolderType = "Signature" },
        };

        await scenario.RunAsync();

        Assert.Equal(["save", StartedEvent, SaveFileEvent], scenario.Journal.Entries);
        Assert.Null(scenario.Generation.SignatureCopyFileId);
        Assert.Equal(DocumentGenerationStatus.Uploading, scenario.Generation.Status);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    public async Task A_page_count_that_fails_or_is_empty_anchors_the_signature_on_page_one(int pages, bool throws)
    {
        var scenario = new Scenario
        {
            Pages = new Pages(pages, throws),
            SignatureCopy = new SignatureCopyRequest { OwnerType = "SignatureRequest", FolderType = "Signature" },
        };

        await scenario.RunAsync();

        Assert.Equal(1, scenario.Generation.SignatureAnchorPage);
    }

    [Fact]
    public async Task The_stored_logo_wins_and_no_resolver_is_called()
    {
        var scenario = new Scenario { StoredBranding = Stored("data:image/png;base64,STORED") };

        await scenario.RunAsync();

        Assert.Empty(scenario.Logos.Calls);
        Assert.Contains("\"logo\": \"data:image/png;base64,STORED\"", Snapshot(scenario.Renderer.Data!));
    }

    [Fact]
    public async Task Without_a_stored_logo_it_uses_the_client_logo_of_the_office()
    {
        var scenario = new Scenario();
        scenario.Logos.ByTenant[Tenant] = "data:image/png;base64,OFFICE";

        await scenario.RunAsync();

        Assert.Equal((Tenant, LogoAudience.Client), Assert.Single(scenario.Logos.Calls));
        Assert.Contains("\"logo\": \"data:image/png;base64,OFFICE\"", Snapshot(scenario.Renderer.Data!));
    }

    [Fact]
    public async Task If_the_office_logo_fails_it_falls_back_to_the_system_logo()
    {
        var scenario = new Scenario();
        scenario.Logos.Failing.Add(Tenant);
        scenario.Logos.ByTenant[PlatformTenant.Id] = "data:image/png;base64,SYSTEM";

        await scenario.RunAsync();

        Assert.Equal([(Tenant, LogoAudience.Client), (PlatformTenant.Id, LogoAudience.Staff)], scenario.Logos.Calls);
        Assert.Contains("\"logo\": \"data:image/png;base64,SYSTEM\"", Snapshot(scenario.Renderer.Data!));
    }

    [Fact]
    public async Task If_no_logo_can_be_resolved_the_summary_still_renders()
    {
        var scenario = new Scenario();
        scenario.Logos.Failing.Add(Tenant);
        scenario.Logos.Failing.Add(PlatformTenant.Id);

        await scenario.RunAsync();

        Assert.Contains("\"logo\": \"\"", Snapshot(scenario.Renderer.Data!));
        Assert.Equal(DocumentGenerationStatus.Uploading, scenario.Generation.Status);
    }

    private static DocumentBranding Stored(string logo)
    {
        var branding = DocumentBranding.Create(Tenant, "Office", logo, "#123abc", "Footer", DateTime.UtcNow);
        return branding.Value;
    }

    private static IEnumerable<DocumentGenerationFailedIntegrationEvent> Failures(Scenario scenario) =>
        scenario.Published.OfType<DocumentGenerationFailedIntegrationEvent>();

    private static void AssertFailed(Scenario scenario, string errorCode)
    {
        Assert.Equal(DocumentGenerationStatus.Failed, scenario.Generation.Status);
        Assert.Equal(["save", FailedEvent], scenario.Journal.Entries);

        var failed = Assert.Single(Failures(scenario));
        Assert.Equal(errorCode, failed.ErrorCode);
        Assert.Equal(Tenant, failed.TenantId);
        Assert.Equal("corr", failed.CorrelationId);
        Assert.Equal(scenario.Generation.Id, failed.GenerationId);
        Assert.Equal(DocumentType, failed.DocumentType);
        Assert.Equal(scenario.Generation.Owner.OwnerType, failed.OwnerType);
        Assert.Equal(scenario.Generation.Owner.OwnerId, failed.OwnerId);
    }
}
