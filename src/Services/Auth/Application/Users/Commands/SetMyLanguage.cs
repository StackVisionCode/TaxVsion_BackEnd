using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

/// <summary>El propio usuario elige el idioma de sus avisos ("en", "es" o null = sin preferencia).</summary>
public sealed record SetMyLanguageCommand(Guid UserId, string? Language);

/// <summary>Guarda el idioma y publica la foto del perfil.</summary>
public static class SetMyLanguageHandler
{
    public static async Task<Result> Handle(
        SetMyLanguageCommand command,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        var result = user.SetLanguage(command.Language);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.UserProfileUpdated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Foto del perfil: repetir el mismo idioma publica una foto igual, inofensivo.
        await bus.PublishAsync(UserAvatarMessages.ProfileUpdated(user, correlation.CorrelationId));
        return Result.Success();
    }
}
