using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Users.Login;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

/// <summary>
/// <see cref="AccountKind"/>: el CRM entra con la cuenta Staff y el portal con la Portal (la misma persona
/// puede tener ambas con el mismo email). Sin indicarlo se usa Staff si existe y, si no, Portal.
/// </summary>
public sealed record LoginCommand(
    Guid TenantId,
    string Email,
    string Password,
    string? DeviceName = null,
    string? DeviceToken = null,
    UserAccountKind? AccountKind = null
);

public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string? DeviceToken = null
);

/// <summary>
/// Respuesta polimórfica del login: tokens directos, o desafío MFA pendiente
/// (loginTicket + métodos disponibles), o aviso de enrolamiento MFA requerido.
/// </summary>
public sealed record LoginResponse(
    bool MfaRequired,
    bool MfaSetupRequired,
    AuthTokensResponse? Tokens,
    string? LoginTicket,
    string[]? MfaMethods,
    int? TicketExpiresInSeconds,
    bool TakeoverRequired = false,
    string? TakeoverTicket = null,
    int? TakeoverTicketExpiresInSeconds = null,
    // Actor del propio usuario autenticado (ya viaja en su JWT). Deja que el frontend rechace temprano
    // un login en la superficie equivocada (cliente en el CRM / staff en el portal) ANTES del takeover,
    // sin tocarle la sesión existente.
    string? ActorType = null
)
{
    public static LoginResponse ForTokens(
        AuthTokensResponse tokens,
        bool mfaSetupRequired = false,
        string? actorType = null
    ) => new(false, mfaSetupRequired, tokens, null, null, null, ActorType: actorType);

    public static LoginResponse ForMfaChallenge(string loginTicket, string[] methods, int ticketSeconds) =>
        new(true, false, null, loginTicket, methods, ticketSeconds);

    // Sesión única: el usuario ya tiene una sesión activa. No hay tokens todavía — el frontend
    // muestra el interstitial y canjea el vale en POST /auth/session/takeover si el usuario confirma.
    public static LoginResponse ForTakeover(
        string takeoverTicket,
        int ticketSeconds,
        bool mfaSetupRequired = false,
        string? actorType = null
    ) =>
        new(
            false,
            mfaSetupRequired,
            null,
            null,
            null,
            null,
            TakeoverRequired: true,
            TakeoverTicket: takeoverTicket,
            TakeoverTicketExpiresInSeconds: ticketSeconds,
            ActorType: actorType
        );
}

public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MfaTicketValidity = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TakeoverTicketValidity = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Orquesta los pasos del login (<see cref="LoginDiscovery"/>, <see cref="LoginPasswordCheck"/>,
/// <see cref="LoginMfaStep"/> y <see cref="LoginSessionStep"/>). Guardar y publicar se queda aquí.
/// </summary>
public static class LoginHandler
{
    public static async Task<Result<LoginResponse>> Handle(
        LoginCommand command,
        LoginDiscovery discovery,
        LoginPasswordCheck passwordCheck,
        LoginMfaStep mfaStep,
        LoginSessionStep sessionStep,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;

        var (attempt, refusal) = await discovery.FindAsync(command, ct);
        if (attempt is null)
            return await CloseAsync(refusal!, unitOfWork, bus, ct);

        var decision =
            await passwordCheck.VerifyAsync(attempt, command.Password, now, ct)
            ?? await mfaStep.DecideAsync(attempt, command.DeviceToken, now, ct)
            ?? await sessionStep.OpenAsync(attempt, LoginSessionEntry.Password, ct);

        return await CloseAsync(decision, unitOfWork, bus, ct);
    }

    private static async Task<Result<LoginResponse>> CloseAsync(
        LoginDecision decision,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (decision.MustSave)
            await unitOfWork.SaveChangesAsync(ct);
        if (decision.Notice is not null)
            await bus.PublishAsync(decision.Notice);
        return decision.Outcome;
    }
}
