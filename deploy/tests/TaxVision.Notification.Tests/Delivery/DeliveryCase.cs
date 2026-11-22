using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

/// <summary>Datos y dobles comunes de las caracterizaciones de entrega (antes y después del punto único).</summary>
internal static class DeliveryCase
{
    public static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid StaffUserId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid PortalUserId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid CustomerId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    public static readonly Guid ResourceId = Guid.Parse("50000000-0000-0000-0000-000000000005");
    public static readonly Guid EventId = Guid.Parse("60000000-0000-0000-0000-000000000006");
    public static readonly Guid OtherUserId = Guid.Parse("70000000-0000-0000-0000-000000000007");
    public const string StaffEmail = "staff@example.com";
    public const string OtherEmail = "other@example.com";
    public static readonly DateTime OccurredOn = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    public static DeliveryRecorder Recorder(string language = "en")
    {
        var recorder = new DeliveryRecorder(language, PortalUserId)
            .Alias(TenantId, "tenant")
            .Alias(StaffUserId, "staff")
            .Alias(PortalUserId, "portal")
            .Alias(CustomerId, "customer")
            .Alias(ResourceId, "resource")
            .Alias(EventId, "evt")
            .Alias(OtherUserId, "other")
            .WithDevice(TenantId, StaffUserId)
            .WithDevice(TenantId, PortalUserId)
            .WithDevice(TenantId, OtherUserId);
        recorder.Directory.Seed(TenantId, StaffUserId, StaffEmail);
        recorder.Directory.Seed(TenantId, OtherUserId, OtherEmail);
        return recorder;
    }

    public static void Same(string[] expected, string[] actual)
    {
        if (!expected.SequenceEqual(actual))
            Assert.Fail(
                "La entrega cambió. Líneas actuales:\n" + string.Join("\n", actual.Select(line => $"\"{line}\","))
            );
    }

    public static readonly IOptions<PortalOptions> Portal = Options.Create(new PortalOptions());
    public static readonly ITenantHostResolver Host = new FakeTenantHostResolver(TestPushLinks.Host);

    /// <summary>Correo del directorio local del recorder (sin pull a Auth).</summary>
    public static UserEmailResolver Emails(DeliveryRecorder recorder) =>
        new(recorder.Directory, new NoContact(), new NoOpUnitOfWork(), NullLogger<UserEmailResolver>.Instance);

    internal sealed class OneCustomer : ICustomerEmailDirectoryRepository
    {
        public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<CustomerEmailDirectoryEntry?>(
                customerId == CustomerId
                    ? CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client Name")
                    : null
            );

        public Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>La cuenta de portal es <see cref="PortalUserId"/>; cualquier otra audiencia, los usuarios dados.</summary>
    internal sealed class PortalResolver(params Guid[] staff) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(staff);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<Guid>>(audience is CustomerPortal ? [PortalUserId] : staff));
    }

    private sealed class NoContact : IUserContactSnapshotClient
    {
        public Task<RemoteUserContact?> FetchContactAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<RemoteUserContact?>(null);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
