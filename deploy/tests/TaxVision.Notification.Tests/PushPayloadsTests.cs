using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Contrato único de <c>Data</c> y <c>Link</c> del push: mismo recurso que el in-app.</summary>
public sealed class PushPayloadsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    [Fact]
    public async Task Staff_link_points_to_n_open_with_resource_and_event()
    {
        var baseUrl = await TestPushLinks.Create().StaffBaseAsync(TenantId, default);

        var link = PushPayloads.Link(
            baseUrl,
            new PushTarget(NotificationKinds.TasksTaskOverdue, TaskId.ToString(), SourceEventId: EventId)
        );

        Assert.Equal($"https://{TestPushLinks.Host}/n/open?rt=task&rid={TaskId}&ev={EventId}", link);
    }

    [Fact]
    public async Task Client_link_uses_the_portal_prefix()
    {
        var portalUser = Guid.NewGuid();
        var links = TestPushLinks.Create(portalUserId: portalUser);

        Assert.Equal($"https://{TestPushLinks.Host}/portal", await links.ClientBaseAsync(TenantId, default));
        Assert.Equal(
            $"https://{TestPushLinks.Host}/portal",
            await links.BaseForUserAsync(TenantId, portalUser, default)
        );
        Assert.Equal($"https://{TestPushLinks.Host}", await links.BaseForUserAsync(TenantId, UserId, default));
    }

    [Fact]
    public void Empty_values_are_omitted()
    {
        var link = PushPayloads.Link("https://x", new PushTarget(NotificationKinds.TasksTaskOverdue));

        Assert.Equal("https://x/n/open?rt=task", link);
    }

    [Fact]
    public void Data_repeats_the_link()
    {
        var customerId = Guid.NewGuid();
        var target = new PushTarget(NotificationKinds.TasksTaskOverdue, TaskId.ToString(), customerId, EventId);
        var link = PushPayloads.Link("https://x", target);

        var data = PushPayloads.Data(target, TenantId, UserId, link);

        Assert.Equal(NotificationKinds.TasksTaskOverdue, data["kind"]);
        Assert.Equal("task", data["resourceType"]);
        Assert.Equal(TaskId.ToString(), data["resourceId"]);
        Assert.Equal(customerId.ToString(), data["customerId"]);
        Assert.Equal(TenantId.ToString(), data["tenantId"]);
        Assert.Equal(UserId.ToString(), data["recipientUserId"]);
        Assert.Equal(EventId.ToString(), data["sourceEventId"]);
        Assert.Equal(link, data["link"]);
    }

    [Fact]
    public void Without_link_or_event_their_keys_are_absent()
    {
        var data = PushPayloads.Data(new PushTarget(NotificationKinds.TasksTaskOverdue), TenantId, UserId, link: null);

        Assert.DoesNotContain("link", data.Keys);
        Assert.DoesNotContain("sourceEventId", data.Keys);
        Assert.DoesNotContain("customerId", data.Keys);
        Assert.DoesNotContain("resourceId", data.Keys);
    }

    [Fact]
    public void Client_forms_link_is_unchanged()
    {
        var formRequestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var link = PushPayloads.Link(
            "https://office.taxproffice.com",
            new PushTarget(NotificationKinds.TasksFormRequestAssigned, formRequestId.ToString(), customerId, EventId)
        );

        Assert.Equal(
            $"https://office.taxproffice.com/n/open?rt=form_request&rid={formRequestId:D}&cid={customerId:D}&ev={EventId:D}",
            link
        );
    }
}
