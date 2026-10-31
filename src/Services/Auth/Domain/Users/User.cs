using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using TaxVision.Auth.Domain.Users.Events;

namespace TaxVision.Auth.Domain.Users;

public sealed class User : AggregateRoot
{
    private readonly List<string> _roles = [];

    private User() { }

    public string Name { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserActorType ActorType { get; private set; }

    /// <summary>Staff o Portal: el email es único por oficina dentro de cada tipo de cuenta.</summary>
    public UserAccountKind AccountKind => UserAccountKinds.Of(ActorType);
    public Guid? CustomerId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? SlotPriorityAt { get; private set; }
    public bool IsOwner => ActorType == UserActorType.TenantAdmin && SlotPriorityAt == DateTime.MinValue;

    /// <summary>Estado del ciclo de vida. IsActive sigue siendo la compuerta de runtime; esto solo
    /// añade la distinción terminal Offboarded (retirado, no reversible). Invariante: Offboarded ⇒ !IsActive.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Cuándo se retiró (offboard) al usuario del tenant. Null salvo estado Offboarded.</summary>
    public DateTime? RemovedAtUtc { get; private set; }
    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    // Perfil y credenciales
    public string? TimeZoneId { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool EmailVerified { get; private set; }
    public bool PhoneVerified { get; private set; }
    public DateTime? PasswordChangedAtUtc { get; private set; }

    // Foto de perfil (solo staff): la confirmada y la que espera el antivirus de CloudStorage.
    public Guid? AvatarFileId { get; private set; }
    public Guid? PendingAvatarFileId { get; private set; }
    public DateTime? PendingAvatarRequestedAtUtc { get; private set; }

    // Moderación de contenido (T-20) de la última foto subida: en revisión o bloqueada.
    public Guid? AvatarModerationFileId { get; private set; }
    public AvatarModerationState? AvatarModerationState { get; private set; }

    /// <summary>Pasado este tiempo sin resultado del antivirus, la foto pendiente se da por fallida.</summary>
    public static readonly TimeSpan AvatarPendingTimeout = TimeSpan.FromMinutes(10);

    // MFA
    public bool MfaEnabled { get; private set; }

    // Protección contra fuerza bruta
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutEndUtc { get; private set; }

    // Invalidación de permisos en JWT emitidos
    public int PermissionsVersion { get; private set; }

    /// <summary>Fase 3 del plan de notificaciones dinámicas — se llena cuando el job de
    /// auto-reparación (<c>PermissionsBackfillService</c>) re-publica <c>UserRolesChangedIntegrationEvent</c>
    /// para este usuario. Null = todavía no se reparó (o nunca tuvo un cambio de rol,
    /// ver <see cref="PermissionsVersion"/>). Mismo patrón que <c>TenantDomainBackfillService</c>: la
    /// condición de "qué falta" se recalcula con esta columna, sin flag global separado.</summary>
    public DateTime? PermissionsBackfilledAt { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    /// <summary>PayFlow (Fase 16) — presente solo para el TenantAdmin creado por la Saga de
    /// onboarding (<c>internal/tenants/{tenantId}/owners</c>). Único índice filtrado: protege
    /// contra que un reintento del mismo comando M2M cree un segundo TenantAdmin.</summary>
    public Guid? OnboardingId { get; private set; }

    public static Result<User> Register(
        Guid tenantId,
        string name,
        string lastName,
        string email,
        string passwordHash,
        UserActorType actorType,
        Guid? customerId = null,
        Guid? onboardingId = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<User>(new Error("User.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<User>(new Error("User.Name", "Name is required."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<User>(new Error("User.LastName", "Last name is required."));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Result.Failure<User>(new Error("User.Email", "Email is invalid."));

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<User>(new Error("User.Password", "Password hash is required."));

        var isPlatformTenant = tenantId == PlatformTenant.Id;
        if (actorType == UserActorType.PlatformAdmin && !isPlatformTenant)
        {
            return Result.Failure<User>(
                new Error("User.PlatformScope", "Platform administrators must belong to the reserved platform tenant.")
            );
        }

        if (actorType != UserActorType.PlatformAdmin && isPlatformTenant)
        {
            return Result.Failure<User>(
                new Error(
                    "User.PlatformScope",
                    "Only platform administrators can belong to the reserved platform tenant."
                )
            );
        }

        if (actorType == UserActorType.CustomerPortal && (!customerId.HasValue || customerId.Value == Guid.Empty))
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is required for customer portal users.")
            );
        }

        if (actorType != UserActorType.CustomerPortal && customerId.HasValue)
        {
            return Result.Failure<User>(
                new Error("User.Customer", "CustomerId is only valid for customer portal users.")
            );
        }

        var nowUtc = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            ActorType = actorType,
            CustomerId = customerId,
            IsActive = true,
            SlotPriorityAt = ResolveInitialSlotPriority(actorType, onboardingId, nowUtc),
            CreatedAtUtc = nowUtc,
            OnboardingId = onboardingId,
        };

        user.SetTenant(tenantId);
        user._roles.Add(UserActorRoles.For(actorType));
        return Result.Success(user);
    }

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } end && end > utcNow;

    public void RegisterFailedLogin(DateTime utcNow, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutEndUtc = utcNow.Add(lockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }

    public Result ChangePassword(string newPasswordHash, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(new Error("User.Password", "Password hash is required."));

        PasswordHash = newPasswordHash;
        PasswordChangedAtUtc = utcNow;
        return Result.Success();
    }

    public Result UpdateProfile(string name, string lastName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("User.Name", "Name is required."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure(new Error("User.LastName", "Last name is required."));

        Name = name.Trim();
        LastName = lastName.Trim();
        return Result.Success();
    }

    public Result SetTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            TimeZoneId = null;
            return Result.Success();
        }

        if (!IanaTimeZone.TryNormalize(timeZoneId, out var normalized))
        {
            return Result.Failure(new Error("User.TimeZone", "Time zone must be a valid IANA identifier."));
        }

        TimeZoneId = normalized;
        return Result.Success();
    }

    public void ChangeEmail(string newEmail)
    {
        Email = newEmail.Trim().ToLowerInvariant();
        EmailVerified = true;
    }

    public void SetPhoneNumber(string phoneNumber)
    {
        PhoneNumber = phoneNumber.Trim();
        PhoneVerified = true;
    }

    /// <summary>
    /// Teléfono de contacto puesto por el propio usuario, sin OTP: vacío lo quita. No se marca verificado;
    /// la MFA por SMS usa su propio destino confirmado (MfaMethod), no este campo.
    /// </summary>
    public Result UpdatePhoneNumber(string? phoneNumber)
    {
        var phone = phoneNumber?.Trim() ?? string.Empty;
        if (phone.Length == 0)
        {
            PhoneNumber = null;
            PhoneVerified = false;
            return Result.Success();
        }

        if (phone.Length is < 7 or > 20 || !phone.All(c => char.IsDigit(c) || c is '+' or '-' or ' '))
            return Result.Failure(new Error("User.Phone", "Phone number is invalid."));

        PhoneNumber = phone;
        PhoneVerified = false;
        return Result.Success();
    }

    /// <summary>
    /// Deja la foto nueva pendiente del antivirus. Reemplaza a otra pendiente (la última subida gana);
    /// la confirmada sigue visible hasta que llegue el resultado.
    /// </summary>
    public Result BeginAvatarChange(Guid fileId, DateTime nowUtc)
    {
        if (fileId == Guid.Empty)
            return Result.Failure(new Error("User.AvatarFile", "Avatar file is required."));

        if (ActorType == UserActorType.CustomerPortal)
            return Result.Failure(
                new Error("User.AvatarNotAllowed", "Portal clients manage their photo from their client profile.")
            );

        PendingAvatarFileId = fileId;
        PendingAvatarRequestedAtUtc = nowUtc;
        ClearAvatarModeration();
        return Result.Success();
    }

    /// <summary>Hay una foto pendiente y todavía no venció (en revisión de contenido no vence).</summary>
    public bool HasLivePendingAvatar(DateTime nowUtc) =>
        IsPendingAvatarUnderReview
        || (
            PendingAvatarFileId.HasValue
            && PendingAvatarRequestedAtUtc is { } requestedAt
            && nowUtc - requestedAt <= AvatarPendingTimeout
        );

    public bool IsPendingAvatarUnderReview =>
        PendingAvatarFileId is { } pending
        && AvatarModerationFileId == pending
        && AvatarModerationState == Users.AvatarModerationState.UnderReview;

    /// <summary>La moderación dejó la pendiente en revisión humana; true si era esa foto.</summary>
    public bool HoldPendingAvatarForReview(Guid fileId)
    {
        if (PendingAvatarFileId != fileId)
            return false;
        AvatarModerationFileId = fileId;
        AvatarModerationState = Users.AvatarModerationState.UnderReview;
        return true;
    }

    /// <summary>La foto pendiente viola la política de contenido: se descarta y queda constancia.</summary>
    public bool RejectPendingAvatarForPolicy(Guid fileId)
    {
        if (!DiscardPendingAvatar(fileId))
            return false;
        AvatarModerationFileId = fileId;
        AvatarModerationState = Users.AvatarModerationState.Blocked;
        return true;
    }

    /// <summary>
    /// Confirma la pendiente si es esa foto. Devuelve la foto anterior (para borrarla), o null si no
    /// había o si el evento no corresponde a la pendiente (viejo o repetido): en ese caso no cambia nada.
    /// </summary>
    public Guid? ConfirmAvatar(Guid fileId)
    {
        if (PendingAvatarFileId != fileId)
            return null;

        var previous = AvatarFileId;
        AvatarFileId = fileId;
        PendingAvatarFileId = null;
        PendingAvatarRequestedAtUtc = null;
        ClearAvatarModeration();
        return previous == fileId ? null : previous;
    }

    /// <summary>Descarta la pendiente solo si es esa foto; true si la descartó.</summary>
    public bool DiscardPendingAvatar(Guid fileId)
    {
        if (PendingAvatarFileId != fileId)
            return false;

        PendingAvatarFileId = null;
        PendingAvatarRequestedAtUtc = null;
        return true;
    }

    /// <summary>Quita la foto confirmada y la pendiente. Devuelve las que había, para pedir su borrado.</summary>
    public IReadOnlyList<Guid> RemoveAvatar()
    {
        var removed = new List<Guid>(2);
        if (AvatarFileId is { } current)
            removed.Add(current);
        if (PendingAvatarFileId is { } pending && pending != AvatarFileId)
            removed.Add(pending);

        AvatarFileId = null;
        PendingAvatarFileId = null;
        PendingAvatarRequestedAtUtc = null;
        ClearAvatarModeration();
        return removed;
    }

    private void ClearAvatarModeration()
    {
        AvatarModerationFileId = null;
        AvatarModerationState = null;
    }

    public void VerifyEmail() => EmailVerified = true;

    public void EnableMfa() => MfaEnabled = true;

    public void DisableMfa() => MfaEnabled = false;

    public void BumpPermissionsVersion() => PermissionsVersion++;

    public void MarkPermissionsBackfilled(DateTime nowUtc) => PermissionsBackfilledAt = nowUtc;

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        ReleaseSlotPriority();
        DeactivatedAtUtc = utcNow;
        // No degradar un retiro terminal a "desactivado" (Offboarded ya implica inactivo).
        if (Status != UserStatus.Offboarded)
            Status = UserStatus.Deactivated;
    }

    public void Reactivate(DateTime? nowUtc = null)
    {
        IsActive = true;
        AssignSlotPriorityIfMissing(nowUtc ?? DateTime.UtcNow);
        DeactivatedAtUtc = null;
        Status = UserStatus.Active;
    }

    /// <summary>Retira al usuario del tenant: estado TERMINAL, no reversible. Idempotente.</summary>
    public Result Offboard(DateTime utcNow)
    {
        if (Status == UserStatus.Offboarded)
            return Result.Success();

        Status = UserStatus.Offboarded;
        IsActive = false;
        ReleaseSlotPriority();
        RemovedAtUtc = utcNow;
        DeactivatedAtUtc ??= utcNow;
        return Result.Success();
    }

    public Result AssignSlotPriorityIfMissing(DateTime nowUtc)
    {
        if (ActorType == UserActorType.CustomerPortal)
        {
            SlotPriorityAt = null;
            return Result.Success();
        }

        if (ActorType is not (UserActorType.TenantAdmin or UserActorType.TenantEmployee))
            return Result.Success();

        if (SlotPriorityAt.HasValue)
            return Result.Success();

        SlotPriorityAt = nowUtc;
        AddDomainEvent(new UserSlotPriorityAssigned(TenantId, Id, nowUtc));
        return Result.Success();
    }

    public Result ReleaseSlotPriority()
    {
        if (!SlotPriorityAt.HasValue)
            return Result.Success();

        if (IsOwner)
            return Result.Failure(new Error("User.OwnerSlot", "The account owner cannot release their slot."));

        SlotPriorityAt = null;
        AddDomainEvent(new UserSlotPriorityReleased(TenantId, Id));
        return Result.Success();
    }

    public Result OverrideSlotPriority(DateTime? value, Guid actorAdminId, string reason)
    {
        if (ActorType == UserActorType.CustomerPortal)
            return Result.Failure(new Error("User.PortalSlot", "Portal users do not occupy staff slots."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("User.SlotOverrideReason", "A reason is required."));

        if (actorAdminId == Guid.Empty)
            return Result.Failure(new Error("User.SlotOverrideActor", "The acting administrator is required."));

        if (IsOwner && value != DateTime.MinValue)
            return Result.Failure(new Error("User.OwnerSlot", "The account owner must remain in the first slot."));

        var previous = SlotPriorityAt;
        if (previous == value)
            return Result.Success();

        SlotPriorityAt = value;
        AddDomainEvent(new UserSlotPriorityOverridden(TenantId, Id, previous, value, actorAdminId, reason.Trim()));
        return Result.Success();
    }

    /// <summary>
    /// Repara una cuenta que quedó como PlatformAdmin dentro de una oficina: un estado que
    /// <see cref="Register"/> nunca permite (solo aparece si se cambió a mano en la base). Vuelve a
    /// TenantAdmin; si nació por onboarding, como dueña (primer asiento). Solo cubre ese caso: no es
    /// un cambio de tipo de actor general.
    /// </summary>
    public Result RestoreAsTenantAdmin(Guid restoredByUserId, DateTime nowUtc)
    {
        if (ActorType != UserActorType.PlatformAdmin || TenantId == PlatformTenant.Id)
            return Result.Failure(
                new Error(
                    "User.NotMisplacedPlatformAdmin",
                    "Only a platform administrator outside the platform tenant can be restored as tenant administrator."
                )
            );

        if (restoredByUserId == Guid.Empty)
            return Result.Failure(new Error("User.SlotOverrideActor", "The acting administrator is required."));

        ActorType = UserActorType.TenantAdmin;
        _roles.Clear();
        _roles.Add(UserActorRoles.For(ActorType));

        // El evento de override dispara el recálculo de asientos del tenant.
        var previous = SlotPriorityAt;
        SlotPriorityAt = OnboardingId.HasValue ? DateTime.MinValue : nowUtc;
        AddDomainEvent(
            new UserSlotPriorityOverridden(
                TenantId,
                Id,
                previous,
                SlotPriorityAt,
                restoredByUserId,
                "Restored as tenant administrator"
            )
        );
        return Result.Success();
    }

    private static DateTime? ResolveInitialSlotPriority(UserActorType actorType, Guid? onboardingId, DateTime nowUtc) =>
        actorType switch
        {
            UserActorType.CustomerPortal or UserActorType.PlatformAdmin => null,
            UserActorType.TenantAdmin when onboardingId.HasValue => DateTime.MinValue,
            UserActorType.TenantAdmin or UserActorType.TenantEmployee => nowUtc,
            _ => null,
        };
}
