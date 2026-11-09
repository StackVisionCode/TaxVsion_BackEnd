using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Application.Templates.Commands.Instantiate;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Templates;
using TaxVision.Signature.Domain.Templates.ValueObjects;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>SIG-13. Lo que hace hoy la instanciación de una plantilla, antes de separar su armado.</summary>
public sealed class CreateSignatureRequestFromTemplateHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid BaseFile = Guid.NewGuid();

    private sealed class Scenario(SignatureTemplate? template)
    {
        public List<string> Journal { get; } = [];
        public CapturingBus Bus { get; } = new();
        public ISignatureRequestRepository Repository { get; } =
            DispatchProxy.Create<ISignatureRequestRepository, RecordingRequests>();
        public RecordingRequests Requests => (RecordingRequests)Repository;
        public FixedSignatureLimitsResolver Limits { get; init; } = new();
        public Dictionary<string, Guid> Customers { get; } = [];
        public Result<SignatureProfile>? Effective { get; init; }
        public IRequestDocumentVerifier Verifier { get; init; } = AcceptingDocumentVerifier.Instance;
        public bool? ActorCanSign { get; private set; }

        public async Task<Result<SignatureRequestResponse>> RunAsync(CreateSignatureRequestFromTemplateCommand command)
        {
            Requests.Journal = Journal;
            return await CreateSignatureRequestFromTemplateHandler.Handle(
                command,
                new Templates(template),
                Repository,
                new SignatureRequestFromTemplateBuilder(
                    new CustomerEmails(Customers),
                    new Resolver(this),
                    Limits,
                    new FixedSignaturePlatformSettings()
                ),
                Verifier,
                new UnitOfWork(Journal, Bus),
                Bus,
                new FixedCorrelation(),
                new ListCache(Journal, Bus),
                CancellationToken.None
            );
        }

        private sealed class Resolver(Scenario scenario) : IEffectiveSignatureResolver
        {
            public Task<Result<SignatureProfile>> ResolveAsync(
                Guid tenantId,
                Guid preparerUserId,
                bool actorHasSignOwn,
                CancellationToken ct = default
            )
            {
                scenario.ActorCanSign = actorHasSignOwn;
                return Task.FromResult(
                    scenario.Effective ?? Result.Failure<SignatureProfile>(new Error("Signature.Profile.None", "None."))
                );
            }
        }
    }

    private static SignatureTemplate Draft(
        int documents = 1,
        int slots = 1,
        bool sendPartialCopy = false,
        PartialCopyAudienceKind audience = PartialCopyAudienceKind.All,
        IReadOnlyList<int>? audienceSlots = null
    )
    {
        var limits = TestSignatureLimits.Default;
        var template = SignatureTemplate
            .CreateDraft(
                limits,
                Tenant,
                Actor,
                "Engagement letter",
                "Template description",
                "Fiscal",
                defaultTokenExpirationHours: 72,
                requiresSequentialSigning: true,
                requiresConsent: true,
                generateCertificate: true,
                sendSealedDocumentToSigners: false,
                sendCertificateToSigners: true,
                autoRemindersEnabled: false,
                reminderIntervalHours: 24,
                sendPartialCopyOnEachSignature: sendPartialCopy,
                partialCopyAudienceKind: audience,
                partialCopyAudienceSlotOrders: audienceSlots,
                expirationEnabled: true
            )
            .Value;
        for (var i = 1; i <= documents; i++)
            Assert.True(template.AddDocument(limits, i == 1 ? BaseFile : Guid.NewGuid(), $"Document {i}").IsSuccess);
        for (var i = 1; i <= slots; i++)
            Assert.True(
                template
                    .AddSlot(
                        limits,
                        TestSignatureLimits.MaxRolesPerTemplate,
                        TemplateSlotRole.Create($"Role {i}").Value,
                        i == 1 ? "Es" : "En",
                        i == 1 ? SignerVerificationMethod.EmailOtp : null
                    )
                    .IsSuccess
            );
        foreach (var document in template.Documents)
        foreach (var slot in template.Slots)
            Assert.True(
                template
                    .PlaceField(
                        document.Id,
                        slot.Order,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.2, 0.3, 0.05).Value,
                        "Sign here",
                        true,
                        11
                    )
                    .IsSuccess
            );
        return template;
    }

    private static SignatureTemplate Published(SignatureTemplate template)
    {
        Assert.True(template.Publish().IsSuccess);
        return template;
    }

    private static CreateSignatureRequestFromTemplateCommand Command(
        IReadOnlyList<SlotBinding>? bindings = null,
        Guid? originalFileId = null,
        IReadOnlyList<TemplateDocumentOverride>? documents = null,
        string? descriptionOverride = null,
        bool actorCanSignOwn = false
    ) =>
        new(
            Tenant,
            Actor,
            Guid.NewGuid(),
            originalFileId,
            bindings ?? [new SlotBinding(1, "one@example.test", "Signer One")],
            descriptionOverride,
            actorCanSignOwn,
            documents
        );

    [Fact]
    public async Task Unknown_template_fails()
    {
        var result = await new Scenario(null).RunAsync(Command());

        Assert.Equal("Signature.Template.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task A_draft_template_cannot_be_instantiated()
    {
        var result = await new Scenario(Draft()).RunAsync(Command());

        Assert.Equal("Signature.Template.NotPublished", result.Error.Code);
    }

    [Fact]
    public async Task Happy_path_copies_the_template_defaults_and_saves_before_publishing()
    {
        var scenario = new Scenario(Published(Draft()));
        var customer = Guid.NewGuid();
        scenario.Customers["one@example.test"] = customer;

        var result = await scenario.RunAsync(Command());

        Assert.True(result.IsSuccess);
        Assert.Equal(["add", "save, published 0", "cache, published 0"], scenario.Journal);

        var request = scenario.Requests.Added!;
        Assert.Equal(Tenant, request.TenantId);
        Assert.Equal(Actor, request.CreatedByUserId);
        Assert.Equal("Engagement letter", request.Title);
        Assert.Equal("Template description", request.Description);
        Assert.Equal("Fiscal", request.Category);
        Assert.Equal(72, request.TokenExpirationHours);
        Assert.True(request.RequiresSequentialSigning);
        Assert.True(request.RequiresConsent);
        Assert.True(request.GenerateCertificate);
        Assert.False(request.SendSealedDocumentToSigners);
        Assert.True(request.SendCertificateToSigners);
        Assert.False(request.AutoRemindersEnabled);
        Assert.Equal(24, request.ReminderIntervalHours);
        Assert.False(request.SendPartialCopyOnEachSignature);

        var document = Assert.Single(request.Documents);
        Assert.Equal(BaseFile, document.OriginalFileId);
        Assert.Equal("Document 1", document.Title);

        var signer = Assert.Single(request.Signers);
        Assert.Equal("one@example.test", signer.Email.Value);
        Assert.Equal("Signer One", signer.FullName.Value);
        Assert.Equal(customer, signer.MappedCustomerId);
        Assert.Equal("Es", signer.Language);
        Assert.Equal(SignerVerificationMethod.EmailOtp, signer.RequiredVerificationMethod);

        var field = Assert.Single(signer.Fields);
        Assert.Equal(document.Id, field.DocumentId);
        Assert.Equal(SignatureFieldKind.Signature, field.Kind);
        Assert.Equal("Sign here", field.Label);
        Assert.True(field.IsRequired);
        Assert.Equal(0.2, field.Position.Y);

        var created = Assert.IsType<SignatureRequestCreatedIntegrationEvent>(Assert.Single(scenario.Bus.Messages));
        Assert.Equal(request.Id, created.SignatureRequestId);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(Actor, created.CreatedByUserId);
        Assert.Equal("Engagement letter", created.Title);
        Assert.Equal("Fiscal", created.Category);
        Assert.Equal([BaseFile], created.OriginalFileIds);
        Assert.Equal(1, created.DocumentCount);
        Assert.Equal(1, created.SignerCount);
        Assert.True(created.RequiresSequentialSigning);
        Assert.Equal(request.Id, result.Value.Id);
    }

    [Fact]
    public async Task The_token_life_is_capped_by_the_current_plan()
    {
        var limits = TestSignatureLimits.Default;
        var plan = SignatureLimits.Create(
            limits.MaxDocumentsPerRequest,
            limits.MaxSignersPerRequest,
            limits.MaxPdfBytes,
            limits.MaxPagesPerDocument,
            24,
            limits.MinRetentionYears,
            limits.PurgeAllowed,
            limits.AllowedChannels
        );
        var policy = new EffectiveSignaturePolicy(
            plan,
            plan,
            24,
            RetentionPolicy.DefaultRetentionYears,
            false,
            VerificationChannel.Email,
            RetentionYearsSource.Signature
        );
        var scenario = new Scenario(Published(Draft())) { Limits = new FixedSignatureLimitsResolver(policy) };

        await scenario.RunAsync(Command());

        Assert.Equal(24, scenario.Requests.Added!.TokenExpirationHours);
    }

    [Fact]
    public async Task A_text_field_keeps_its_font_size()
    {
        var template = Draft();
        Assert.True(
            template
                .PlaceField(
                    template.Documents[0].Id,
                    1,
                    SignatureFieldKind.Text,
                    FieldPosition.Create(1, 0.4, 0.5, 0.2, 0.05).Value,
                    "Name",
                    false,
                    11
                )
                .IsSuccess
        );
        var scenario = new Scenario(Published(template));

        await scenario.RunAsync(Command());

        var field = Assert.Single(
            Assert.Single(scenario.Requests.Added!.Signers).Fields,
            candidate => candidate.Kind == SignatureFieldKind.Text
        );
        Assert.Equal(11, field.FontSizePt);
    }

    [Fact]
    public async Task The_description_override_wins()
    {
        var scenario = new Scenario(Published(Draft()));

        await scenario.RunAsync(Command(descriptionOverride: "Override"));

        Assert.Equal("Override", scenario.Requests.Added!.Description);
    }

    [Fact]
    public async Task A_customer_that_does_not_match_stays_unmapped()
    {
        var scenario = new Scenario(Published(Draft()));

        await scenario.RunAsync(Command());

        Assert.Null(Assert.Single(scenario.Requests.Added!.Signers).MappedCustomerId);
    }

    [Theory]
    [InlineData(0, "Signature.Template.BindingMismatch")]
    [InlineData(1, "Signature.Template.BindingUnknownSlot")]
    [InlineData(2, "Signature.Template.BindingDuplicate")]
    public async Task Bindings_must_match_the_slots_one_to_one(int caseIndex, string expected)
    {
        IReadOnlyList<SlotBinding> bindings = caseIndex switch
        {
            0 => [new SlotBinding(1, "one@example.test", "Signer One")],
            1 =>
            [
                new SlotBinding(1, "one@example.test", "Signer One"),
                new SlotBinding(9, "two@example.test", "Signer Two"),
            ],
            _ =>
            [
                new SlotBinding(1, "one@example.test", "Signer One"),
                new SlotBinding(1, "two@example.test", "Signer Two"),
            ],
        };
        var scenario = new Scenario(Published(Draft(slots: 2)));

        var result = await scenario.RunAsync(Command(bindings));

        Assert.Equal(expected, result.Error.Code);
        Assert.Empty(scenario.Journal);
    }

    [Theory]
    [InlineData("not-an-email", "Signer One", null)]
    [InlineData("one@example.test", "", null)]
    [InlineData("one@example.test", "Signer One", "abc")]
    public async Task An_invalid_signer_stops_before_creating_anything(string email, string name, string? phone)
    {
        var scenario = new Scenario(Published(Draft()));

        var result = await scenario.RunAsync(Command([new SlotBinding(1, email, name, phone)]));

        Assert.True(result.IsFailure);
        Assert.Empty(scenario.Journal);
    }

    // SIG-19: el teléfono del slot llega al firmante; sin él, el OTP por SMS no tiene destino.
    [Fact]
    public async Task The_phone_of_the_binding_reaches_the_signer()
    {
        var scenario = new Scenario(Published(Draft()));

        var result = await scenario.RunAsync(
            Command([new SlotBinding(1, "one@example.test", "Signer One", "+13055550142")])
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(Assert.Single(scenario.Requests.Added!.Signers).PhoneNumber);
    }

    [Fact]
    public async Task OriginalFileId_replaces_the_file_of_a_single_document_template()
    {
        var file = Guid.NewGuid();
        var scenario = new Scenario(Published(Draft()));

        await scenario.RunAsync(Command(originalFileId: file));

        Assert.Equal(file, Assert.Single(scenario.Requests.Added!.Documents).OriginalFileId);
    }

    [Fact]
    public async Task OriginalFileId_is_ambiguous_on_a_multi_document_template()
    {
        var result = await new Scenario(Published(Draft(documents: 2))).RunAsync(
            Command(originalFileId: Guid.NewGuid())
        );

        Assert.Equal("Signature.Template.DocumentOverrideAmbiguous", result.Error.Code);
    }

    [Fact]
    public async Task OriginalFileId_and_document_overrides_conflict()
    {
        var template = Published(Draft());

        var result = await new Scenario(template).RunAsync(
            Command(
                originalFileId: Guid.NewGuid(),
                documents: [new TemplateDocumentOverride(template.Documents[0].Id, Guid.NewGuid())]
            )
        );

        Assert.Equal("Signature.Template.DocumentOverrideConflict", result.Error.Code);
    }

    [Fact]
    public async Task A_document_cannot_be_overridden_twice()
    {
        var template = Published(Draft());
        var id = template.Documents[0].Id;

        var result = await new Scenario(template).RunAsync(
            Command(documents: [new TemplateDocumentOverride(id, null), new TemplateDocumentOverride(id, null)])
        );

        Assert.Equal("Signature.Template.DocumentOverrideDuplicate", result.Error.Code);
    }

    [Fact]
    public async Task An_override_of_an_unknown_document_fails()
    {
        var result = await new Scenario(Published(Draft())).RunAsync(
            Command(documents: [new TemplateDocumentOverride(Guid.NewGuid(), Guid.NewGuid())])
        );

        Assert.Equal("Signature.Template.DocumentOverrideUnknown", result.Error.Code);
    }

    [Fact]
    public async Task Document_overrides_change_file_and_title_and_keep_the_order_and_the_fields()
    {
        var template = Published(Draft(documents: 2));
        var second = template.Documents.OrderBy(document => document.Order).Last();
        var file = Guid.NewGuid();
        var scenario = new Scenario(template);

        await scenario.RunAsync(Command(documents: [new TemplateDocumentOverride(second.Id, file, "  Renamed  ")]));

        var request = scenario.Requests.Added!;
        var documents = request.Documents.OrderBy(document => document.Order).ToList();
        Assert.Equal(BaseFile, documents[0].OriginalFileId);
        Assert.Equal("Document 1", documents[0].Title);
        Assert.Equal(file, documents[1].OriginalFileId);
        Assert.Equal("Renamed", documents[1].Title);
        Assert.Equal(
            documents.Select(document => document.Id),
            Assert.Single(request.Signers).Fields.Select(field => field.DocumentId)
        );
    }

    [Fact]
    public async Task Partial_copy_to_everyone_is_set_on_creation()
    {
        var scenario = new Scenario(Published(Draft(sendPartialCopy: true)));

        await scenario.RunAsync(Command());

        Assert.True(scenario.Requests.Added!.SendPartialCopyOnEachSignature);
        Assert.Equal(PartialCopyAudienceKind.All, scenario.Requests.Added.PartialCopyAudience.Kind);
    }

    [Fact]
    public async Task Partial_copy_to_specific_slots_maps_them_to_the_created_signers()
    {
        var scenario = new Scenario(
            Published(
                Draft(slots: 2, sendPartialCopy: true, audience: PartialCopyAudienceKind.Specific, audienceSlots: [2])
            )
        );

        await scenario.RunAsync(
            Command([
                new SlotBinding(1, "one@example.test", "Signer One"),
                new SlotBinding(2, "two@example.test", "Signer Two"),
            ])
        );

        var request = scenario.Requests.Added!;
        Assert.True(request.SendPartialCopyOnEachSignature);
        Assert.Equal(PartialCopyAudienceKind.Specific, request.PartialCopyAudience.Kind);
        Assert.Equal(
            request.Signers.Single(signer => signer.Email.Value == "two@example.test").Id,
            Assert.Single(request.PartialCopyAudience.SpecificSignerIds)
        );
    }

    [Fact]
    public async Task Preparer_fields_are_inherited_with_the_effective_signature()
    {
        var template = Draft();
        var position = FieldPosition.Create(1, 0.5, 0.6, 0.2, 0.05).Value;
        Assert.True(
            template
                .PlacePreparerField(template.Documents[0].Id, SignatureFieldKind.Signature, position, "Preparer")
                .IsSuccess
        );
        var profile = SignatureProfile.Create(Tenant, Actor, Actor, "Mine", Guid.NewGuid(), 200, 80).Value;
        var scenario = new Scenario(Published(template)) { Effective = Result.Success(profile) };

        await scenario.RunAsync(Command(actorCanSignOwn: true));

        var request = scenario.Requests.Added!;
        var field = Assert.Single(request.PreparerFields);
        Assert.Equal(request.Documents[0].Id, field.DocumentId);
        Assert.Equal("Preparer", field.Label);
        Assert.Equal(profile.FileId, request.PreparerSignatureFileId);
        Assert.True(scenario.ActorCanSign);
    }

    [Fact]
    public async Task Without_an_effective_signature_the_preparer_field_stays_and_nothing_fails()
    {
        var template = Draft();
        Assert.True(
            template
                .PlacePreparerField(
                    template.Documents[0].Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.5, 0.6, 0.2, 0.05).Value,
                    "Preparer"
                )
                .IsSuccess
        );
        var scenario = new Scenario(Published(template));

        var result = await scenario.RunAsync(Command());

        Assert.True(result.IsSuccess);
        Assert.Single(scenario.Requests.Added!.PreparerFields);
        Assert.Null(scenario.Requests.Added.PreparerSignatureFileId);
    }

    [Fact]
    public async Task Without_preparer_fields_the_effective_signature_is_not_resolved()
    {
        var scenario = new Scenario(Published(Draft()));

        await scenario.RunAsync(Command(actorCanSignOwn: true));

        Assert.Null(scenario.ActorCanSign);
    }

    [Fact]
    public async Task The_practitioner_pin_of_the_template_is_copied_as_is()
    {
        var template = Draft();
        var pinHash = new string('c', 64);
        Assert.True(template.SetPractitionerPin(pinHash).IsSuccess);
        var scenario = new Scenario(Published(template));

        await scenario.RunAsync(Command());

        Assert.Equal(pinHash, scenario.Requests.Added!.PractitionerPinHash);
    }

    [Fact]
    public async Task A_rejected_file_stops_the_creation_with_the_reason()
    {
        var scenario = new Scenario(Published(Draft())) { Verifier = new RejectingVerifier() };

        var result = await scenario.RunAsync(Command());

        Assert.Equal("Signature.Document.Rejected", result.Error.Code);
        Assert.Contains("'Document 1' can't be used", result.Error.Message);
        Assert.Empty(scenario.Journal);
    }

    [Fact]
    public async Task A_file_that_is_not_ready_does_not_block_the_creation()
    {
        var scenario = new Scenario(Published(Draft())) { Verifier = new DeferringVerifier() };

        var result = await scenario.RunAsync(Command());

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Draft, scenario.Requests.Added!.Status);
    }

    internal class RecordingRequests : DispatchProxy
    {
        public SignatureRequest? Added { get; private set; }
        public List<string> Journal { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISignatureRequestRepository.AddAsync))
                throw new NotSupportedException(targetMethod?.Name);

            Added = (SignatureRequest)args![0]!;
            Journal.Add("add");
            return Task.CompletedTask;
        }
    }

    private sealed class Templates(SignatureTemplate? template) : ISignatureTemplateRepository
    {
        public Task<SignatureTemplate?> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default) =>
            Task.FromResult(template);

        public Task AddAsync(SignatureTemplate value, CancellationToken ct = default) => Task.CompletedTask;

        public void Remove(SignatureTemplate value) { }
    }

    private sealed class CustomerEmails(Dictionary<string, Guid> customers) : ICustomerEmailProjectionRepository
    {
        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                customers.TryGetValue(normalizedEmail, out var customerId)
                    ? CustomerEmailProjection.ForNewCustomer(tenantId, customerId, normalizedEmail, "Customer")
                    : null
            );

        public Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnitOfWork(List<string> journal, CapturingBus bus) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            journal.Add($"save, published {bus.Messages.Count}");
            return Task.FromResult(1);
        }
    }

    private sealed class ListCache(List<string> journal, CapturingBus bus) : ISignatureRequestListCacheInvalidator
    {
        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
        {
            journal.Add($"cache, published {bus.Messages.Count}");
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingVerifier : IRequestDocumentVerifier
    {
        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            CancellationToken ct
        ) =>
            Task.FromResult(
                DocumentVerificationResult.Rejected(
                    DocumentRejection.Create("Signature.Document.Rejected", "bad PDF").Value
                )
            );

        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            VerifiableFile file,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class DeferringVerifier : IRequestDocumentVerifier
    {
        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            CancellationToken ct
        ) => Task.FromResult(DocumentVerificationResult.Deferred);

        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            VerifiableFile file,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }
}
