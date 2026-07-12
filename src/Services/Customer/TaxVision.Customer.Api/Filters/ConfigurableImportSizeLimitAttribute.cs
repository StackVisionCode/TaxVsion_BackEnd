using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Imports.Configuration;

namespace TaxVision.Customer.Api.Filters;

/// <summary>
/// Aplica el limite de tamano del request de importacion tomando el valor de
/// <see cref="CustomerImportOptions.MaxFileBytes"/> resuelto desde DI en cada request,
/// reemplazando al atributo estatico <c>[RequestSizeLimit(const)]</c> (que exige una
/// constante en tiempo de compilacion).
///
/// Al implementarse como <see cref="IFilterFactory"/> con <see cref="IsReusable"/> = false,
/// las opciones se re-resuelven por request, por lo que un cambio de configuracion (global
/// del SaaS hoy; por tenant en el futuro, si el resolver pasa a considerar el tenant actual)
/// surte efecto sin recompilar. El limite se fija antes del model binding, en la fase de
/// autorizacion, igual que el filtro nativo de ASP.NET Core.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ConfigurableImportSizeLimitAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<CustomerImportOptions>>();
        return new Filter(options.CurrentValue.MaxFileBytes);
    }

    private sealed class Filter(long maxBytes) : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is not null && !feature.IsReadOnly)
                feature.MaxRequestBodySize = maxBytes;
        }
    }
}
