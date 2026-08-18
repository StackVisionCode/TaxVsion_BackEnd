using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Feeds;

/// <summary>
/// El permiso de un usuario para leer su agenda por una URL sin sesión.
///
/// <para>
/// Uno activo por usuario: emitir otro revoca el anterior, que es lo que hace útil el botón de
/// «regenerar» — quien pegó la URL vieja en un calendario ajeno deja de verla.
/// </para>
/// </summary>
public sealed class CalendarFeedToken : TenantEntity
{
    private CalendarFeedToken() { }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = default!;

    /// <summary>Para que la UI pueda decir cuál es sin poder reconstruirlo.</summary>
    public string TokenLast4 { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public DateTime? LastAccessedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static (CalendarFeedToken Token, string PlainValue) Issue(Guid tenantId, Guid userId, DateTime nowUtc)
    {
        var token = FeedToken.Create();
        var entity = new CalendarFeedToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = token.Hash,
            TokenLast4 = token.Last4,
            CreatedAtUtc = nowUtc,
        };

        entity.SetTenant(tenantId);
        return (entity, token.Value);
    }

    public Result Revoke(DateTime nowUtc)
    {
        if (RevokedAtUtc is not null)
            return Result.Failure(FeedErrors.AlreadyRevoked);

        RevokedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Para saber si alguien sigue suscrito antes de que nadie pregunte por qué no se actualiza.</summary>
    public void RegisterAccess(DateTime nowUtc) => LastAccessedAtUtc = nowUtc;
}
