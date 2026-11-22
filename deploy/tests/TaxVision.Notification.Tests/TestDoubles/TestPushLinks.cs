using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary><see cref="PushLinks"/> con un host fijo y, opcionalmente, una cuenta de portal.</summary>
internal static class TestPushLinks
{
    public const string Host = "office.example.com";

    public static PushLinks Create(Guid? portalUserId = null, string? host = Host) =>
        new(
            new FakeTenantHostResolver(host),
            Options.Create(new PortalOptions { ClientPathPrefix = "/portal" }),
            new PortalAccountsStub(portalUserId)
        );
}
