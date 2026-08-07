namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Headers de seguridad para toda respuesta que sale del Gateway. Estaba como lambda inline en
/// Program.cs; se extrajo para poder testearlo (GW-13).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // HSTS solo fuera de desarrollo: en local se sirve por http y el header dejaría el
        // navegador forzando https contra localhost durante los 2 años del max-age.
        if (!environment.IsDevelopment())
            headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";

        await next(context);
    }
}
