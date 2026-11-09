using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence;

// UPDLOCK dura lo que la transacción del handler: el siguiente intento lee el contador ya guardado.
public sealed class SqlUserLoginLock(AuthDbContext db) : IUserLoginLock
{
    public async Task AcquireAsync(User user, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The user login lock requires an open transaction.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM [Users] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {user.Id}",
            ct
        );
        await db.Entry(user).ReloadAsync(ct);
    }
}
