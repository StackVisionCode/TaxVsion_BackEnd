using BuildingBlocks.Common;
using BuildingBlocks.Imaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

// Dobles compartidos por los tests de la foto de perfil del cliente.
internal static class CustomerAvatarTestDoubles
{
    public static readonly Guid TenantId = Guid.Parse("6d1c2a8e-0f6b-4b1e-9d2a-1c0e7f3a5b11");
    public static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    public static DomainCustomer NewCustomer(Guid? createdBy = null) =>
        DomainCustomer
            .Register(
                TenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ana", "Client").Value,
                null,
                EmailAddress.Create($"ana-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                createdBy ?? Guid.NewGuid()
            )
            .Value;

    /// <summary>Cliente con una foto vigente ya confirmada.</summary>
    public static DomainCustomer WithPicture(out Guid fileId)
    {
        var customer = NewCustomer();
        fileId = Guid.NewGuid();
        customer.BeginProfilePictureChange(fileId, Guid.NewGuid(), Now);
        customer.ConfirmProfilePicture(fileId);
        return customer;
    }

    public static CustomerProfileUpdateFromFormRequestedConsumerTests.InMemoryCustomers Repo(
        params DomainCustomer[] customers
    )
    {
        var repo = new CustomerProfileUpdateFromFormRequestedConsumerTests.InMemoryCustomers();
        repo.All.AddRange(customers);
        return repo;
    }
}

/// <summary>Cuenta los SaveChanges y registra cuántos mensajes había publicados en el primero.</summary>
internal sealed class RecordingUnitOfWork(FakeMessageBus bus) : IUnitOfWork
{
    public int Saves { get; private set; }
    public int? PublishedAtFirstSave { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        PublishedAtFirstSave ??= bus.Published.Count;
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class StubAvatarNormalizer : IAvatarImageNormalizer
{
    public static readonly byte[] NormalizedBytes = [1, 2, 3, 4, 5];

    public Error? FailWith { get; init; }
    public int Calls { get; private set; }

    public Task<Result<NormalizedAvatar>> NormalizeAsync(Stream input, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Normalize([]));
    }

    public Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input) =>
        FailWith is { } error
            ? Result.Failure<NormalizedAvatar>(error)
            : Result.Success(new NormalizedAvatar(NormalizedBytes, "image/webp", 512, 512, "abc"));
}

internal sealed class StubAvatarStorage : ICustomerAvatarStorage
{
    public bool Fail { get; init; }
    public List<(Guid TenantId, Guid FileId, byte[] Content, string ContentType)> Stored { get; } = [];

    public Task<Result<CustomerAvatarSourceObject>> StoreAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct
    )
    {
        if (Fail)
            return Task.FromResult(
                Result.Failure<CustomerAvatarSourceObject>(new Error("Customer.Avatar.Storage.Upload", "down"))
            );

        Stored.Add((tenantId, fileId, content, contentType));
        return Task.FromResult(
            Result.Success(new CustomerAvatarSourceObject("taxvision-temp", $"customer/avatars/{fileId:N}.webp"))
        );
    }
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}

internal sealed class FixedCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "corr-avatar";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new Scope();

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}
