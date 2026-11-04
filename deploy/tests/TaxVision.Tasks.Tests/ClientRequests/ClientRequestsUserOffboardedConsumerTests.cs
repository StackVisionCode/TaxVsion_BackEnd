using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Consumers;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Permissions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.ClientRequests;

/// <summary>
/// Al retirar a quien recibía los avisos de un pedido abierto: pasan al asignado de la tarea vinculada (si
/// no es el retirado), si no al sucesor, si no a quien ejecutó el retiro. Solo usuarios activos.
/// </summary>
public sealed class ClientRequestsUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Notices_go_to_the_assignee_of_the_linked_task()
    {
        var assignee = Guid.NewGuid();
        var task = NewTask(assignee);
        var request = NewRequest(task.Id);

        await RunAsync([request], [task], [assignee, Successor], Successor);

        Assert.Equal(assignee, request.StaffRecipientUserId);
        Assert.Equal(Leaver, request.RequestedByUserId);
    }

    [Fact]
    public async Task A_task_still_assigned_to_the_leaver_falls_back_to_the_successor()
    {
        var task = NewTask(Leaver);
        var request = NewRequest(task.Id);

        await RunAsync([request], [task], [Successor], Successor);

        Assert.Equal(Successor, request.StaffRecipientUserId);
    }

    [Fact]
    public async Task Without_task_nor_successor_notices_go_to_the_admin_who_offboarded()
    {
        var request = NewRequest(null);

        await RunAsync([request], [], [Admin], null);

        Assert.Equal(Admin, request.StaffRecipientUserId);
    }

    [Fact]
    public async Task Inactive_candidates_are_skipped_and_nothing_changes_if_none_is_active()
    {
        var request = NewRequest(null);

        await RunAsync([request], [], [], Successor);

        Assert.Equal(Leaver, request.StaffRecipientUserId);
    }

    [Fact]
    public async Task Closed_requests_and_requests_of_others_are_left_alone()
    {
        var closed = NewRequest(null);
        closed.Cancel(Leaver, null, Now);
        var others = ClientRequest
            .Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), null, "1099", null, null, Now)
            .Value;

        await RunAsync([closed, others], [], [Successor], Successor);

        Assert.Equal(Leaver, closed.StaffRecipientUserId);
        Assert.NotEqual(Successor, others.StaffRecipientUserId);
    }

    private static Task RunAsync(ClientRequest[] requests, TaskItem[] tasks, Guid[] activeUsers, Guid? successor) =>
        ClientRequestsUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = Leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                OffboardedByUserId = Admin,
                RemovedAtUtc = Now,
            },
            new InMemoryClientRequestRepository(requests),
            new InMemoryTaskRepository(tasks),
            new ActiveUsers(activeUsers),
            new RecordingUnitOfWork(),
            new NoOpCorrelationContext(),
            NullLogger<ClientRequest>.Instance,
            CancellationToken.None
        );

    private static ClientRequest NewRequest(Guid? taskId) =>
        ClientRequest.Create(TenantId, Guid.NewGuid(), Leaver, taskId, "W-2 2025", null, null, Now).Value;

    private static TaskItem NewTask(Guid assignee) =>
        TaskItem
            .Create(
                TenantId,
                Leaver,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                assignee,
                Now
            )
            .Value;

    private sealed class ActiveUsers(Guid[] active) : IUserPermissionsProjectionRepository
    {
        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            var projection = UserPermissionsProjection.Create(tenantId, userId, 1, [], []);
            if (!active.Contains(userId))
                projection.MarkInactive();
            return Task.FromResult<UserPermissionsProjection?>(projection);
        }

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserPermissionsProjection>>([]);
    }
}
