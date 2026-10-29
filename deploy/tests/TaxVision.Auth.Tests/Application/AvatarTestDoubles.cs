using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Imaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Doble genérico: responde los métodos que el test configura y lanza en cualquier otro, así un
/// handler que toca algo inesperado rompe el test en vez de pasar en silencio.
/// </summary>
public class StubProxy<T> : DispatchProxy
    where T : class
{
    private Func<MethodInfo, object?[]?, (bool Handled, object? Result)> _handler = (_, _) => (false, null);

    public static T Create(Func<MethodInfo, object?[]?, (bool Handled, object? Result)> handler)
    {
        var proxy = DispatchProxy.Create<T, StubProxy<T>>();
        ((StubProxy<T>)(object)proxy)._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var (handled, result) = _handler(targetMethod!, args);
        return handled
            ? result
            : throw new NotSupportedException($"{typeof(T).Name}.{targetMethod!.Name} was not expected.");
    }
}

internal static class AvatarDoubles
{
    public static User Employee(Guid? tenantId = null) =>
        User.Register(
            tenantId ?? Guid.NewGuid(),
            "Elio",
            "Paz",
            "elio@example.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

    public static User PortalClient(Guid? tenantId = null) =>
        User.Register(
            tenantId ?? Guid.NewGuid(),
            "Ada",
            "Lovelace",
            "ada@cliente.test",
            "hash",
            UserActorType.CustomerPortal,
            customerId: Guid.NewGuid()
        ).Value;

    /// <summary>Repositorio que solo sabe devolver este usuario por id.</summary>
    public static IUserRepository Users(User? user) =>
        StubProxy<IUserRepository>.Create(
            (method, args) =>
                method.Name == nameof(IUserRepository.GetByIdAsync)
                    ? (true, Task.FromResult(user is not null && (Guid)args![0]! == user.Id ? user : null))
                    : (false, null)
        );

    /// <summary>User con la foto ya confirmada (pasó por pendiente → confirmada).</summary>
    public static Guid GiveConfirmedAvatar(User user)
    {
        var fileId = Guid.NewGuid();
        user.BeginAvatarChange(fileId, DateTime.UtcNow);
        user.ConfirmAvatar(fileId);
        return fileId;
    }
}

/// <summary>Cuenta los guardados y recuerda cuántos mensajes se habían publicado al guardar.</summary>
internal sealed class OrderedUnitOfWork(FakeMessageBus bus) : IUnitOfWork
{
    public int Saves { get; private set; }
    public List<int> PublishedAtSave { get; } = [];

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        PublishedAtSave.Add(bus.Published.Count);
        return Task.FromResult(1);
    }
}

internal sealed class RecordingAvatarAudit : IAuthAuditWriter
{
    public List<AuthAuditLog> Logs { get; } = [];

    public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
    {
        Logs.Add(log);
        return Task.CompletedTask;
    }
}

internal sealed class AvatarRequestContext : IRequestContext
{
    public string? IpAddress => "203.0.113.10";
    public string? UserAgent => "test-agent";
}

internal sealed class AvatarCorrelation : ICorrelationContext
{
    public string CorrelationId => "avatar-correlation";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new Scope();

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Normalizador falso: devuelve un WebP fijo o el error configurado.</summary>
internal sealed class FakeAvatarNormalizer(Error? error = null) : IAvatarImageNormalizer
{
    public static readonly byte[] Output = [0x52, 0x49, 0x46, 0x46, 0x01];
    public int Calls { get; private set; }

    public Task<Result<NormalizedAvatar>> NormalizeAsync(Stream input, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(
            error is null
                ? Result.Success(new NormalizedAvatar(Output, "image/webp", 512, 512, "hash"))
                : Result.Failure<NormalizedAvatar>(error)
        );
    }

    public Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input) => throw new NotSupportedException();
}

/// <summary>Almacén falso del bucket temporal: recuerda lo subido o falla.</summary>
internal sealed class FakeAvatarStorage(bool fail = false) : IUserAvatarStorage
{
    public List<(Guid TenantId, Guid UserId, Guid FileId, byte[] Content, string ContentType)> Stored { get; } = [];

    public Task<Result<StoredUserAvatar>> StoreAsync(
        Guid tenantId,
        Guid userId,
        Guid fileId,
        byte[] content,
        string contentType,
        CancellationToken ct = default
    )
    {
        if (fail)
            return Task.FromResult(
                Result.Failure<StoredUserAvatar>(
                    TaxVision.Auth.Application.Users.Avatars.UserAvatarErrors.StorageUnavailable
                )
            );

        Stored.Add((tenantId, userId, fileId, content, contentType));
        return Task.FromResult(
            Result.Success(new StoredUserAvatar(fileId, "taxvision-temp", $"auth/avatars/{fileId:N}"))
        );
    }
}
