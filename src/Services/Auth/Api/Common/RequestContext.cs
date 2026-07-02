using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Api.Common;

/// <summary>Implementación de IRequestContext sobre HttpContext (IP y user-agent).</summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? IpAddress =>
        accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent =>
        accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } value
            ? value
            : null;
}
