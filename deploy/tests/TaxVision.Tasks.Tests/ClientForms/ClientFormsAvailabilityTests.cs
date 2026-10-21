using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Settings;
using TaxVision.Tasks.Infrastructure.ClientForms;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsAvailabilityTests
{
    [Fact]
    public async Task Global_switch_has_first_precedence()
    {
        var evaluator = Evaluator(enabled: false, tenantEnabled: true, modules: ["planner"]);

        var result = await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId);

        Assert.False(result.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.NotAvailable, result.Reason);
    }

    [Fact]
    public async Task Tenant_must_be_enabled_before_module_is_checked()
    {
        var modules = new MemoryModulesReader(["planner"]);
        var evaluator = Evaluator(enabled: true, tenantEnabled: false, modulesReader: modules);

        var result = await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId);

        Assert.Equal(ClientFormsAvailabilityReasons.NotEnabled, result.Reason);
        Assert.Equal(0, modules.Reads);
    }

    public static TheoryData<string[]?> MissingPlannerCases => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "wallet" },
    };

    [Theory]
    [MemberData(nameof(MissingPlannerCases))]
    public async Task Missing_projection_or_planner_fails_closed(string[]? modules)
    {
        var evaluator = Evaluator(enabled: true, tenantEnabled: true, modules: modules);

        var result = await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId);

        Assert.False(result.Enabled);
        Assert.Equal(ClientFormsAvailabilityReasons.ModuleUnavailable, result.Reason);
    }

    [Fact]
    public async Task Enabled_tenant_with_planner_is_available()
    {
        var evaluator = Evaluator(enabled: true, tenantEnabled: true, modules: ["PLANNER"]);

        Assert.Equal(ClientFormsAvailability.Available, await evaluator.EvaluateAsync(FormTemplateHandlerTestDoubles.TenantId));
    }

    private static ClientFormsAvailabilityEvaluator Evaluator(
        bool enabled,
        bool tenantEnabled,
        string[]? modules = null,
        MemoryModulesReader? modulesReader = null
    )
    {
        var repository = new MemoryFormModuleSettingsRepository();
        if (tenantEnabled)
            repository.Add(FormModuleSettings.Create(
                FormTemplateHandlerTestDoubles.TenantId,
                true,
                null,
                FormTemplateHandlerTestDoubles.UserId,
                DateTime.UtcNow
            ).Value);
        return new ClientFormsAvailabilityEvaluator(
            Options.Create(new ClientFormsOptions { Enabled = enabled }),
            repository,
            modulesReader ?? new MemoryModulesReader(modules)
        );
    }
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
