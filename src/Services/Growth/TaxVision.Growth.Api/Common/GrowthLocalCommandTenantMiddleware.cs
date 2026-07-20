using BuildingBlocks.Tenancy;
using Wolverine;

namespace TaxVision.Growth.Api.Common;

/// <summary>
/// Restores tenant identity inside the DI scope Wolverine creates to run a message handler.
/// Wolverine gives every handled message (including local commands invoked in-process via
/// <c>bus.InvokeAsync</c>) a fresh scope disconnected from the HTTP request scope where
/// <see cref="JwtTenantContextMiddleware"/> already resolved the tenant from the JWT — so
/// <see cref="TenantContext"/> starts empty again for local command handlers, tripping the
/// fail-closed tenant checks (idempotency, EF query filters) before any domain logic runs.
///
/// <see cref="JwtTenantContextMiddleware"/> stamps the resolved tenant onto
/// <c>IMessageBus.TenantId</c> before the controller calls <c>InvokeAsync</c>; Wolverine copies
/// that onto <see cref="Envelope.TenantId"/> for the resulting envelope. This middleware — wired
/// globally in Program.cs with no message-type filter, so it covers every local command as well
/// as consumed integration events — reads it back into the handler's scoped
/// <see cref="TenantContext"/>. For inbound integration events whose producer already sets
/// <see cref="Envelope.TenantId"/> or not, this is a no-op fallback: <see cref="GrowthTenantMessageMiddleware"/>
/// remains the source of truth for those, since it also runs.
/// </summary>
public static class GrowthLocalCommandTenantMiddleware
{
    public static void Before(Envelope envelope, TenantContext tenantContext)
    {
        if (Guid.TryParse(envelope.TenantId, out var tenantId) && tenantId != Guid.Empty)
            tenantContext.SetTenant(tenantId);
    }
}
