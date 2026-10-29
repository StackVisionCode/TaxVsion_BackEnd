using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.RateLimiting;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Settings;
using TaxVision.Tasks.ClientForms.Domain.Settings;
using TaxVision.Tasks.Infrastructure.ClientForms;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsAvailabilityTests
{
    [Fact]
    public async Task Tenant_with_planner_is_available_without_any_settings_row()
    {
        var evaluator = new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(["PLANNER"]));

        Assert.Equal(
            ClientFormsAvailability.Available,
            await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId)
        );
    }

    public static TheoryData<string[]?> MissingPlannerCases =>
        new() { null, Array.Empty<string>(), new[] { "wallet" } };

    // null = sin proyección de módulos: deniega igual (fail-closed).
    [Theory]
    [MemberData(nameof(MissingPlannerCases))]
    public async Task Missing_projection_or_planner_fails_closed(string[]? modules)
    {
        var evaluator = new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(modules));

        var result = await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId);

        Assert.False(result.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, result.Reason);
    }

    [Fact]
    public async Task Empty_tenant_is_denied_without_reading_modules()
    {
        var modules = new MemoryModulesReader(["planner"]);

        var result = await new ClientFormsAvailabilityEvaluator(modules).EvaluateAsync(Guid.Empty);

        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, result.Reason);
        Assert.Equal(0, modules.Reads);
    }

    [Fact]
    public async Task Office_without_settings_row_gets_external_participants_and_intake_links()
    {
        var capabilities = Capabilities(new MemoryFormModuleSettingsRepository(), ["planner"]);

        Assert.True(await capabilities.IsExternalParticipantAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
        Assert.True(await capabilities.IsIntakeLinkAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
    }

    [Fact]
    public async Task Intake_links_stay_unavailable_until_turnstile_is_configured()
    {
        var capabilities = new ClientFormsPublicCapabilityEvaluator(
            WithModules("planner"),
            new MemoryFormModuleSettingsRepository(),
            new UnavailableBotProtection()
        );

        Assert.False(await capabilities.IsIntakeLinkAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
    }

    [Fact]
    public async Task Office_flags_apply_only_inside_the_planner_module()
    {
        var settings = FormModuleSettings
            .Create(FormTemplateHandlerTestDoubles.TenantId, null, FormTemplateHandlerTestDoubles.UserId)
            .Value;
        settings.SetCapabilities(true, true, true, false, false, FormTemplateHandlerTestDoubles.UserId);
        var repository = new MemoryFormModuleSettingsRepository(settings);

        var withPlanner = Capabilities(repository, ["planner"]);
        var withoutPlanner = Capabilities(repository, ["wallet"]);

        Assert.True(await withPlanner.IsIntakeLinkAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
        Assert.False(await withoutPlanner.IsIntakeLinkAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
        Assert.False(await withoutPlanner.IsExternalParticipantAvailableAsync(FormTemplateHandlerTestDoubles.TenantId));
    }

    [Fact]
    public async Task Platform_put_on_a_new_office_creates_the_row_with_defaults_and_reports_real_availability()
    {
        var repository = new MemoryFormModuleSettingsRepository();
        var (bus, recorder) = OutboxMessageBus.Create();

        var result = await SetFormModuleAvailabilityHandler.Handle(
            new SetFormModuleAvailabilityCommand(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                "pilot",
                SignatureStepEnabled: true
            ),
            repository,
            new RecordingFormAuditLog(),
            new AvailableBotProtection(),
            WithModules("planner"),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
        Assert.True(result.Value.ExternalParticipantsEnabled);
        Assert.True(result.Value.AutoCustomerCreationEnabled);
        Assert.True(result.Value.SignatureStepEnabled);
        Assert.True(result.Value.IntakeLinksEnabled);
        Assert.True(result.Value.PushNotificationsEnabled);
        Assert.Single(repository.Items);
        Assert.Empty(recorder.Pending);
    }

    [Fact]
    public async Task Staff_availability_returns_everything_on_for_an_office_without_row()
    {
        var result = await GetStaffFormAvailabilityHandler.Handle(
            new GetStaffFormAvailabilityQuery(FormTemplateHandlerTestDoubles.TenantId),
            new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(["planner"])),
            new MemoryFormModuleSettingsRepository(),
            new AvailableBotProtection(),
            default
        );

        Assert.True(result.Enabled);
        Assert.Null(result.Reason);
        Assert.Equal(new FormCapabilitiesResponse(true, true, true, true, true), result.Capabilities);
    }

    [Fact]
    public async Task Staff_availability_reports_intake_links_off_while_turnstile_is_not_configured()
    {
        var result = await GetStaffFormAvailabilityHandler.Handle(
            new GetStaffFormAvailabilityQuery(FormTemplateHandlerTestDoubles.TenantId),
            new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(["planner"])),
            new MemoryFormModuleSettingsRepository(),
            new UnavailableBotProtection(),
            default
        );

        Assert.Equal(new FormCapabilitiesResponse(true, false, true, true, true), result.Capabilities);
    }

    [Fact]
    public async Task Staff_availability_returns_the_office_flags_when_it_has_a_row()
    {
        var settings = FormModuleSettings
            .Create(FormTemplateHandlerTestDoubles.TenantId, "private", FormTemplateHandlerTestDoubles.UserId)
            .Value;
        settings.SetCapabilities(false, true, false, true, true, FormTemplateHandlerTestDoubles.UserId);

        var result = await GetStaffFormAvailabilityHandler.Handle(
            new GetStaffFormAvailabilityQuery(FormTemplateHandlerTestDoubles.TenantId),
            new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(["planner"])),
            new MemoryFormModuleSettingsRepository(settings),
            new AvailableBotProtection(),
            default
        );

        Assert.Equal(new FormCapabilitiesResponse(false, true, false, true, true), result.Capabilities);
    }

    [Fact]
    public async Task Staff_availability_without_planner_has_no_capabilities()
    {
        var result = await GetStaffFormAvailabilityHandler.Handle(
            new GetStaffFormAvailabilityQuery(FormTemplateHandlerTestDoubles.TenantId),
            new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(["wallet"])),
            new MemoryFormModuleSettingsRepository(),
            new AvailableBotProtection(),
            default
        );

        Assert.False(result.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, result.Reason);
        Assert.Null(result.Capabilities);
    }

    // Contrato JSON del CRM: sin nota ni EnabledByUserId.
    [Fact]
    public void Staff_availability_json_exposes_only_the_effective_flags()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new StaffFormAvailabilityResponse(
                true,
                null,
                FormCapabilitiesResponse.From(FormModuleCapabilities.Defaults)
            ),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        );

        Assert.Equal(
            """{"enabled":true,"reason":null,"capabilities":{"externalParticipantsEnabled":true,"intakeLinksEnabled":true,"autoCustomerCreationEnabled":true,"signatureStepEnabled":true,"pushNotificationsEnabled":true}}""",
            json
        );
    }

    [Fact]
    public async Task Platform_put_with_enabled_false_is_rejected_without_saving()
    {
        var repository = new MemoryFormModuleSettingsRepository();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await SetAsync(
            repository,
            unitOfWork,
            new(FormTemplateHandlerTestDoubles.TenantId, FormTemplateHandlerTestDoubles.UserId, null, Enabled: false)
        );

        Assert.Equal(FormModuleSettingsErrors.EnabledNotSupported, result.Error);
        Assert.Empty(repository.Items);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Platform_put_without_note_keeps_it_and_an_empty_note_clears_it()
    {
        var settings = FormModuleSettings
            .Create(FormTemplateHandlerTestDoubles.TenantId, "pilot", FormTemplateHandlerTestDoubles.UserId)
            .Value;
        var repository = new MemoryFormModuleSettingsRepository(settings);

        var kept = await SetAsync(
            repository,
            new RecordingUnitOfWork(),
            new(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                null,
                SignatureStepEnabled: true,
                Enabled: true
            )
        );
        var cleared = await SetAsync(
            repository,
            new RecordingUnitOfWork(),
            new(FormTemplateHandlerTestDoubles.TenantId, FormTemplateHandlerTestDoubles.UserId, "")
        );

        Assert.Equal("pilot", kept.Value.Note);
        Assert.True(kept.Value.SignatureStepEnabled);
        Assert.Null(cleared.Value.Note);
        Assert.True(cleared.Value.SignatureStepEnabled);
    }

    [Fact]
    public async Task Platform_put_on_an_office_without_planner_saves_flags_but_reports_unavailable()
    {
        var result = await SetAsync(
            new MemoryFormModuleSettingsRepository(),
            new RecordingUnitOfWork(),
            new(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                null,
                ExternalParticipantsEnabled: false
            ),
            "wallet"
        );

        Assert.False(result.Value.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, result.Value.Reason);
        Assert.True(result.Value.Configured);
        Assert.False(result.Value.ExternalParticipantsEnabled);
    }

    [Fact]
    public async Task Platform_list_includes_planner_offices_without_row_and_real_availability_of_rows()
    {
        var withoutPlanner = Guid.NewGuid();
        var plannerWithoutRow = Guid.NewGuid();
        var settings = FormModuleSettings
            .Create(withoutPlanner, "old pilot", FormTemplateHandlerTestDoubles.UserId)
            .Value;

        var list = await ListFormModuleSettingsHandler.Handle(
            new ListFormModuleSettingsQuery(),
            new MemoryFormModuleSettingsRepository(settings),
            new FixedTenantDirectory(plannerWithoutRow),
            default
        );

        var noRow = Assert.Single(list, item => item.TenantId == plannerWithoutRow);
        Assert.True(noRow.Enabled);
        Assert.False(noRow.Configured);
        Assert.Null(noRow.Note);
        Assert.True(noRow.ExternalParticipantsEnabled);
        Assert.True(noRow.AutoCustomerCreationEnabled);
        Assert.True(noRow.IntakeLinksEnabled);
        var row = Assert.Single(list, item => item.TenantId == withoutPlanner);
        Assert.False(row.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, row.Reason);
        Assert.True(row.Configured);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Intake_links_cannot_be_enabled_without_ready_bot_protection()
    {
        var repository = new MemoryFormModuleSettingsRepository();
        var audit = new RecordingFormAuditLog();
        var unitOfWork = new RecordingUnitOfWork();
        var (bus, _) = OutboxMessageBus.Create();

        var result = await SetFormModuleAvailabilityHandler.Handle(
            new SetFormModuleAvailabilityCommand(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                null,
                IntakeLinksEnabled: true
            ),
            repository,
            audit,
            new UnavailableBotProtection(),
            WithModules("planner"),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FormModuleSettingsErrors.IntakeLinksNotReady, result.Error);
        Assert.Empty(repository.Items);
        Assert.Empty(audit.Actions);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Push_setting_is_saved_and_sent_to_notification_as_a_directed_sync_command()
    {
        var repository = new MemoryFormModuleSettingsRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var (bus, recorder) = OutboxMessageBus.Create();

        var result = await SetFormModuleAvailabilityHandler.Handle(
            new SetFormModuleAvailabilityCommand(
                FormTemplateHandlerTestDoubles.TenantId,
                FormTemplateHandlerTestDoubles.UserId,
                null,
                PushNotificationsEnabled: true
            ),
            repository,
            new RecordingFormAuditLog(),
            new AvailableBotProtection(),
            WithModules("planner"),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.Saves);
        var command = Assert.IsType<ClientFormNotificationSettingsSyncRequestedIntegrationEvent>(
            Assert.Single(recorder.Pending)
        );
        Assert.True(command.PushNotificationsEnabled);
        Assert.Equal("tests", command.CorrelationId);
    }

    private static ClientFormsAvailabilityEvaluator WithModules(params string[] modules) =>
        new(new MemoryModulesReader(modules));

    private static Task<BuildingBlocks.Results.Result<FormModuleSettingsResponse>> SetAsync(
        MemoryFormModuleSettingsRepository repository,
        RecordingUnitOfWork unitOfWork,
        SetFormModuleAvailabilityCommand command,
        params string[] modules
    ) =>
        SetFormModuleAvailabilityHandler.Handle(
            command,
            repository,
            new RecordingFormAuditLog(),
            new AvailableBotProtection(),
            WithModules(modules.Length == 0 ? ["planner"] : modules),
            unitOfWork,
            OutboxMessageBus.Create().Item1,
            new TestCorrelationContext(),
            default
        );

    private static ClientFormsPublicCapabilityEvaluator Capabilities(
        IFormModuleSettingsRepository repository,
        string[] modules
    ) =>
        new(
            new ClientFormsAvailabilityEvaluator(new MemoryModulesReader(modules)),
            repository,
            new AvailableBotProtection()
        );
}

internal sealed class UnavailableBotProtection : IFormIntakeBotProtection
{
    public bool IsReady => false;

    public Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default) =>
        Task.FromResult(false);
}

internal sealed class AvailableBotProtection : IFormIntakeBotProtection
{
    public bool IsReady => true;

    public Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default) =>
        Task.FromResult(true);
}

internal sealed class MemoryModulesReader(IReadOnlyList<string>? modules) : ITenantEntitlementModulesReader
{
    internal int Reads { get; private set; }

    public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(modules);
    }
}

internal sealed class MemoryFormModuleSettingsRepository(params FormModuleSettings[] seed)
    : IFormModuleSettingsRepository
{
    internal List<FormModuleSettings> Items { get; } = [.. seed];

    public Task<FormModuleSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(settings => settings.TenantId == tenantId));

    public Task<IReadOnlyList<FormModuleSettings>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormModuleSettings>>(Items.OrderBy(settings => settings.TenantId).ToArray());

    public void Add(FormModuleSettings settings) => Items.Add(settings);
}

internal sealed class FixedTenantDirectory(params Guid[] tenants) : IClientFormsTenantDirectory
{
    public Task<IReadOnlySet<Guid>> ListAvailableTenantIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(tenants.ToHashSet());
}
