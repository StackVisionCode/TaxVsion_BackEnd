using Microsoft.AspNetCore.Http;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Api.Middleware;

/// <summary>
/// Defensa en profundidad anti-phishing del flujo público de firma: si el Gateway resolvió un tenant
/// por el subdominio (header <c>X-Resolved-Tenant</c>) y NO coincide con el tenant del token de firma,
/// la petición se rechaza con 403 <c>tenant_host_mismatch</c>. Sin el header (host de sistema,
/// localhost/dev) no valida — tolerante. El tenant destino de la escritura ya sale del token, así que
/// esto no cambia a dónde se escribe; solo evita que la firma opere bajo el subdominio de otra oficina.
/// </summary>
public sealed class PublicSignatureHostGuardMiddleware(RequestDelegate next)
{
    // Mismo header que fija TaxVision.Gateway.Middleware.TenantHostGuardMiddleware.
    private const string ResolvedTenantHeader = "X-Resolved-Tenant";
    private const string PublicPrefix = "/signature/public/";

    public async Task InvokeAsync(HttpContext context, ISigningTokenService tokenService)
    {
        var path = context.Request.Path.Value;
        if (path is null || !path.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Sin tenant resuelto por Host (host de sistema, localhost/dev) no se valida.
        var header = context.Request.Headers[ResolvedTenantHeader].ToString();
        if (!Guid.TryParse(header, out var hostTenantId))
        {
            await next(context);
            return;
        }

        var token = ExtractToken(path);
        if (token is null)
        {
            await next(context);
            return;
        }

        // Token inválido/expirado: no enmascarar con 403 — que el handler devuelva su error de token.
        var verification = tokenService.Verify(token);
        if (verification.IsFailure)
        {
            await next(context);
            return;
        }

        if (verification.Value.TenantId != hostTenantId)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    error = "tenant_host_mismatch",
                    message = "This signature request belongs to a different office.",
                }
            );
            return;
        }

        await next(context);
    }

    // /signature/public/{token}[/accion] -> {token}
    private static string? ExtractToken(string path)
    {
        var rest = path[PublicPrefix.Length..];
        if (rest.Length == 0)
            return null;
        var slash = rest.IndexOf('/');
        var token = slash < 0 ? rest : rest[..slash];
        return token.Length == 0 ? null : token;
    }
}
