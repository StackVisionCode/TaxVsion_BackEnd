using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates.Commands.SetDispatchScope;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Tests.Rendering;

namespace TaxVision.Scribe.Tests.Templates.Commands;

/// <summary>
/// El override del carril, que solo toca un PlatformAdmin. Lo que se prueba acá es la guarda: poner
/// <c>System</c> en una plantilla que vive sobre <c>tenant-base</c> produce un correo que se presenta
/// como de la oficina y sale del buzón de la plataforma.
/// </summary>
public sealed class SetTemplateDispatchScopeTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task The_platform_lane_is_refused_on_an_office_shell()
    {
        var (template, layout) = Build("tenant-base");

        var result = await HandleAsync(template, layout, DispatchScopes.System);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.LaneContradictsLayout", result.Error.Code);
        Assert.Null(template.DispatchScopeOverride);
    }

    [Fact]
    public async Task An_office_lane_can_be_forced_on_a_platform_shell()
    {
        // El caso que justifica que el override exista: algo que el layout no puede saber.
        var (template, layout) = Build("system-base");

        var result = await HandleAsync(template, layout, DispatchScopes.TenantPreferred);

        Assert.True(result.IsSuccess);
        Assert.Equal(DispatchScopes.TenantPreferred, result.Value.EffectiveScope);
        Assert.True(result.Value.IsOverridden);
        Assert.Equal(Actor, template.DispatchScopeOverrideByUserId);
    }

    [Fact]
    public async Task Releasing_the_override_hands_the_decision_back_to_the_layout()
    {
        var (template, layout) = Build("tenant-base");
        Assert.True(template.OverrideDispatchScope(DispatchScopes.TenantPreferred, Actor, DateTime.UtcNow).IsSuccess);

        var result = await HandleAsync(template, layout, scope: null);

        Assert.True(result.IsSuccess);
        Assert.Null(template.DispatchScopeOverride);
        Assert.Null(template.DispatchScopeOverrideByUserId);
        Assert.False(result.Value.IsOverridden);
        Assert.Equal(DispatchScopes.TenantPreferred, result.Value.EffectiveScope);
    }

    [Fact]
    public async Task An_unknown_lane_is_refused()
    {
        var (template, layout) = Build("system-base");

        var result = await HandleAsync(template, layout, "Potato");

        Assert.True(result.IsFailure);
        Assert.Equal("EmailTemplate.UnknownDispatchScope", result.Error.Code);
    }

    private static Task<Result<SetTemplateDispatchScopeResult>> HandleAsync(
        EmailTemplate template,
        EmailLayout layout,
        string? scope
    ) =>
        SetTemplateDispatchScopeHandler.Handle(
            new SetTemplateDispatchScopeCommand(template.TemplateKey.Value, scope, Actor),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            new FakeUnitOfWork(),
            TimeProvider.System,
            CancellationToken.None
        );

    private static (EmailTemplate Template, EmailLayout Layout) Build(string layoutKey)
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("billing.invoice_sent").Value,
                "Invoice sent",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create(layoutKey).Value,
                layoutKey,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var layoutVersion = layout
            .AddDraftVersion(
                $"system/layouts/{layoutKey}/v1/layout.html",
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        layout.PublishVersion(layoutVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var templateVersion = template
            .AddDraftVersion(
                "Invoice",
                "system/templates/billing.invoice_sent/v1/template.html",
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                layout.Id,
                layoutVersion.VersionNumber,
                [],
                DateTime.UtcNow
            )
            .Value;
        template.PublishVersion(templateVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        return (template, layout);
    }
}
