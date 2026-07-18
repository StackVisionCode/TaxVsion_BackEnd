using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;

namespace TaxVision.Scribe.Tests.ErrorMapping;

/// <summary>
/// Fase 10.5 (hardening): antes de este fix, ninguno de los 5 códigos <c>EventTemplateMapping.*</c>
/// (ver <c>EventTemplateMapping.cs</c>/<c>EventTemplateMappingRepository.cs</c>/los 3 commands en
/// <c>Application/EventMappings/Commands</c>) tenía entrada en <see cref="ErrorHttpMapping"/> — los
/// 5 caían al <c>default</c> (400) en vez de los 404/403 reales que sus equivalentes de Templates/
/// Layouts ya tenían. Se agregaron 5 entradas explícitas, no solo las 2 que cambian de status
/// respecto al default: NotFound→404 y Forbidden→403 son el fix real (antes 400, incorrecto);
/// Tenant/TenantRequired/TenantNotAllowed ya resolvían a 400 vía el catch-all y se hicieron
/// explícitas al mismo valor (400) para documentar 1:1 los 5 códigos en el mapping — no se mapean a
/// 403 pese a que el "Qué hacer" original del plan lo sugería, porque verificado contra el código
/// real, ninguno de los equivalentes EmailTemplate.Tenant/TenantRequired/TenantNotAllowed ni
/// EmailLayout.Tenant/TenantRequired/TenantNotAllowed está mapeado a 403 en este archivo — son
/// errores de validación de contexto de tenant (falta o exceso de TenantId para el Scope), no de
/// autorización, y ya caían correctamente al default 400 antes de este cambio.
/// </summary>
public sealed class EventTemplateMappingErrorHttpMappingTests
{
    [Fact]
    public void EventTemplateMapping_NotFound_maps_to_404()
    {
        var error = new Error("EventTemplateMapping.NotFound", "Event template mapping was not found.");

        Assert.Equal(StatusCodes.Status404NotFound, error.ToHttpStatusCode());
    }

    [Fact]
    public void EventTemplateMapping_Forbidden_maps_to_403()
    {
        var error = new Error("EventTemplateMapping.Forbidden", "This mapping does not belong to your tenant.");

        Assert.Equal(StatusCodes.Status403Forbidden, error.ToHttpStatusCode());
    }

    [Fact]
    public void EventTemplateMapping_Tenant_maps_to_400()
    {
        var error = new Error("EventTemplateMapping.Tenant", "A tenant context is required for tenant mappings.");

        Assert.Equal(StatusCodes.Status400BadRequest, error.ToHttpStatusCode());
    }

    [Fact]
    public void EventTemplateMapping_TenantRequired_maps_to_400()
    {
        var error = new Error(
            "EventTemplateMapping.TenantRequired",
            "TenantId is required for Tenant-scoped mappings."
        );

        Assert.Equal(StatusCodes.Status400BadRequest, error.ToHttpStatusCode());
    }

    [Fact]
    public void EventTemplateMapping_TenantNotAllowed_maps_to_400()
    {
        var error = new Error(
            "EventTemplateMapping.TenantNotAllowed",
            "TenantId must be null for System-scoped mappings."
        );

        Assert.Equal(StatusCodes.Status400BadRequest, error.ToHttpStatusCode());
    }

    [Fact]
    public void Unmapped_code_still_falls_through_to_400_default()
    {
        // Control: confirma que el "default" del switch sigue vivo y no se rompió al agregar las
        // 5 entradas nuevas — un código inventado que nunca va a existir debe seguir cayendo a 400.
        var error = new Error("EventTemplateMapping.SomeCodeThatDoesNotExist", "n/a");

        Assert.Equal(StatusCodes.Status400BadRequest, error.ToHttpStatusCode());
    }
}
