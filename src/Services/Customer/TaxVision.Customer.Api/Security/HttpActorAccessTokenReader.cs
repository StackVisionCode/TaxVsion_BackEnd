using Microsoft.Net.Http.Headers;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Api.Security;

/// <summary>Lee el bearer de la petición entrante (el mismo que validó el JwtBearer) para reenviarlo a Auth.</summary>
public sealed class HttpActorAccessTokenReader(IHttpContextAccessor accessor) : IActorAccessTokenReader
{
    private const string BearerPrefix = "Bearer ";

    public string? Current
    {
        get
        {
            var context = accessor.HttpContext;
            if (context?.User.Identity is not { IsAuthenticated: true })
                return null;
            var header = context.Request.Headers[HeaderNames.Authorization].ToString();
            return
                header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                && header[BearerPrefix.Length..].Trim() is { Length: > 0 } token
                ? token
                : null;
        }
    }
}
