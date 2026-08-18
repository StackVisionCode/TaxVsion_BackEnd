namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Borra <c>X-Tenant-Id</c> de toda petición entrante, para que un cliente no pueda inyectarlo y
/// hacerlo pasar por una señal de confianza aguas abajo.
///
/// <para>
/// GW-11 — el middleware también <b>propagaba</b> el tenant del JWT a ese mismo header, y eso se
/// quitó: ningún servicio lo consume. Verificado en los 17 .NET y en los 2 Node — las únicas
/// menciones al header son doc-comments que declaran justo lo contrario ("<c>X-Tenant-Id</c> se
/// ignora por completo"), porque todos derivan el tenant del JWT ya validado vía
/// <c>JwtTenantContextMiddleware</c>. Propagar un valor que nadie lee no es inofensivo: deja
/// preparado el día en que alguien lo lea "porque ya viene puesto" y convierta un header en
/// autoridad de tenant. El borrado se queda; la propagación no.
/// </para>
/// </summary>
public sealed class TenantPropagationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Request.Headers.Remove("X-Tenant-Id");

        await next(ctx);
    }
}
