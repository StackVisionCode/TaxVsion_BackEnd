using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Visibility;
using TaxVision.Tasks.ClientForms.Application.Actions;
using Xunit;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class SubmissionSummaryTests
{
    [Fact]
    public void Snapshot_masks_taxid_excludes_hidden_and_non_printable_and_labels_rows()
    {
        var definition = Definition();
        var answers = Answers();
        var visibility = Visibility();
        var masks = new[] { new SubmissionSummarySnapshotBuilder.SensitiveMask("ssn", null, "6789") };
        var attachments = new[] { new SubmissionSummarySnapshotBuilder.AttachmentRef("idDoc", null, "id.pdf") };
        var header = new SubmissionSummarySnapshotBuilder.Header(
            "2026 Intake",
            "John Smith",
            3,
            1,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

        var snapshot = SubmissionSummarySnapshotBuilder.Build(
            definition,
            answers,
            visibility,
            masks,
            attachments,
            header
        );

        var section = Assert.Single(snapshot.Pages).Sections;
        var about = section.Single(s => s.Title == "Personal information");
        // Printable text con su label y su valor.
        Assert.Contains(about.Items, i => i is { Label: "Legal first name", Value: "John" });
        // El taxId va enmascarado con los últimos 4.
        Assert.Contains(about.Items, i => i is { Label: "SSN", Value: "•••-••-6789" });
        // El encabezado (layout) nunca entra.
        Assert.DoesNotContain(about.Items, i => i.Label == "Welcome");
        // El campo oculto no entra.
        Assert.DoesNotContain(about.Items, i => i.Label == "Hidden field");
        // El archivo sale como adjunto por nombre, no como item.
        Assert.DoesNotContain(about.Items, i => i.Label == "Photo ID");
        Assert.Contains(snapshot.Attachments, a => a is { Label: "Photo ID", FileName: "id.pdf" });

        // Las filas del repeater quedan rotuladas.
        var dependents = section.Single(s => s.Title == "Dependents");
        var row = Assert.Single(dependents.Rows);
        Assert.Equal("Dependent 1", row.Label);
        Assert.Contains(row.Items, i => i is { Label: "First name", Value: "Mary" });
    }

    [Fact]
    public void The_queued_message_carries_no_cleartext_only_the_encrypted_envelope()
    {
        var codec = Codec();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        const string secretAnswer = "SECRET_ANSWER_VALUE";

        var participant = new ClientFormParticipant { Kind = "Customer", CustomerId = customerId };
        var idempotencyKey = SubmissionSummaryCommandFactory.IdempotencyKeyFor(requestId, 1, Guid.NewGuid());
        var binding = new ProtectedPayloadBinding(tenantId, requestId, participant, IdempotencyKey: idempotencyKey);
        var payload = new SubmissionSummaryPayload
        {
            TenantId = tenantId,
            CustomerId = customerId,
            FormRequestId = requestId,
            Snapshot = new SubmissionSummarySnapshot
            {
                Title = "2026 Intake",
                VersionNumber = 1,
                RevisionNumber = 1,
                Pages =
                [
                    new SubmissionSummaryPage(
                        "About you",
                        [
                            new SubmissionSummarySection(
                                "Personal",
                                [new SubmissionSummaryItem("Name", secretAnswer)],
                                []
                            ),
                        ]
                    ),
                ],
            },
        };

        var evt = new FormSubmissionSummaryRequestedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId,
            GenerationId = Guid.NewGuid(),
            FormRequestId = requestId,
            RevisionNumber = 1,
            Participant = participant,
            IdempotencyKey = idempotencyKey,
            Payload = codec.Protect(ClientFormProtectedPayloadSchemas.SubmissionSummary, binding, payload),
        };

        var wire = JsonSerializer.Serialize(evt);
        Assert.DoesNotContain(secretAnswer, wire, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(evt.Payload.Cipher));

        var restored = codec.Unprotect<SubmissionSummaryPayload>(
            evt.Payload,
            ClientFormProtectedPayloadSchemas.SubmissionSummary,
            binding
        );
        Assert.Equal(secretAnswer, restored.Snapshot.Pages[0].Sections[0].Items[0].Value);
    }

    private static JsonProtectedPayloadCodec Codec() =>
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                        MasterKeyVersion = 1,
                    }
                )
            )
        );

    private static FormDefinition Definition() =>
        new()
        {
            SchemaVersion = 1,
            Title = "2026 Intake",
            Purpose = FormPurpose.ClientIntake,
            Presentation = FormPresentation.Wizard,
            Pages =
            [
                new FormPage
                {
                    Id = "p1",
                    Title = "About you",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "personal",
                            Title = "Personal information",
                            Components =
                            [
                                Component("welcome", "heading", "Welcome"),
                                Component("firstName", "text", "Legal first name"),
                                Component("ssn", "taxId", "SSN"),
                                Component("hidden", "text", "Hidden field"),
                                Component("idDoc", "file", "Photo ID"),
                            ],
                        },
                        new FormSection
                        {
                            Id = "dependents",
                            Title = "Dependents",
                            Repeat = new FormRepeat
                            {
                                RowLabel = "Dependent",
                                MinRows = 0,
                                MaxRows = 5,
                            },
                            Components = [Component("depFirst", "text", "First name")],
                        },
                    ],
                },
            ],
        };

    private static FormComponent Component(string id, string type, string label) =>
        new()
        {
            Id = id,
            Type = type,
            Label = label,
        };

    private static FormAnswers Answers()
    {
        var values = new Dictionary<string, JsonElement>
        {
            ["firstName"] = Json("John"),
            ["hidden"] = Json("should not appear"),
            ["dependents"] = JsonSerializer.SerializeToElement(
                new
                {
                    rows = new[]
                    {
                        new { rowId = "r1", values = new Dictionary<string, object> { ["depFirst"] = "Mary" } },
                    },
                }
            ),
        };
        return new FormAnswers { SchemaVersion = 1, Values = values };
    }

    private static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);

    private static VisibilityMap Visibility()
    {
        var visible = new[] { "p1", "personal", "dependents", "welcome", "firstName", "ssn", "idDoc", "depFirst" };
        var rows = new Dictionary<(string, string), bool> { [("dependents", "r1")] = true };
        return new VisibilityMap(visible, rows);
    }
}
