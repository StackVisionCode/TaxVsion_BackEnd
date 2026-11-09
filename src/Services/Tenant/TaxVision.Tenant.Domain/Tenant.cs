using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Events;

namespace TaxVision.Tenant.Domain;

public partial class Tenant : TenantlessAggregateRoot
{
    /// <summary>Tope del nombre; lo comparte la columna (TenantConfiguration).</summary>
    public const int NameMaxLength = 200;

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public TenantKind Kind { get; private set; }
    public string DefaultTimeZoneId { get; private set; } = default!;
    public EnumTenantStatus.TenantStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Último cambio de nombre o zona horaria. Viaja en <c>TenantUpdatedIntegrationEvent</c>
    /// para que cada proyección descarte un evento más viejo que lo que ya tiene.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>PayFlow (Fase 16) — presente solo para tenants creados vía
    /// <c>internal/tenants/from-onboarding</c>. Único índice filtrado: idempotencia de ese endpoint
    /// contra reintentos del mismo comando M2M.</summary>
    public Guid? OnboardingId { get; private set; }

    /// <summary>Sube en cada cambio real de estado, nombre, zona o subdominio. Viaja en los avisos para que cada
    /// proyección descarte uno más viejo, y es el token de concurrencia de la fila.</summary>
    public long Version { get; private set; }

    private Tenant() { }

    public static Result<Tenant> Create(
        string name,
        string subdomain,
        string defaultTimeZoneId,
        DateTime nowUtc,
        Guid? onboardingId = null
    )
    {
        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
            return Result.Failure<Tenant>(nameResult.Error);

        var sub = (subdomain ?? "").Trim().ToLower();

        if (!MyRegex().IsMatch(sub))
            return Result.Failure<Tenant>(
                new Error(
                    "Tenant.Subdomain",
                    "Subdomain must be 3-40 characters long and contain only lowercase letters and numbers (a-z, 0-9) and hyphens (-)."
                )
            );

        var timeZoneResult = ValidateTimeZone(defaultTimeZoneId);
        if (timeZoneResult.IsFailure)
            return Result.Failure<Tenant>(timeZoneResult.Error);

        var tenant = new Tenant
        {
            Name = nameResult.Value,
            SubDomain = sub,
            Kind = TenantKind.Customer,
            DefaultTimeZoneId = timeZoneResult.Value,
            Status = EnumTenantStatus.TenantStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            OnboardingId = onboardingId,
        };

        return Result.Success(tenant);
    }

    /// <summary>
    /// La oficina renombró su subdominio primario. El dueño del dominio es Auth (lo valida y lo publica);
    /// acá se refleja el slug con la misma regla de formato que al crear la oficina.
    /// </summary>
    public Result ChangeSubDomain(string subdomain)
    {
        var sub = (subdomain ?? "").Trim().ToLower();
        if (!MyRegex().IsMatch(sub))
            return Result.Failure(
                new Error(
                    "Tenant.Subdomain",
                    "Subdomain must be 3-40 characters long and contain only lowercase letters and numbers (a-z, 0-9) and hyphens (-)."
                )
            );

        if (SubDomain != sub)
            Version++;
        SubDomain = sub;
        return Result.Success();
    }

    /// <summary>Renombra la oficina. Devuelve <c>true</c> si el nombre cambió; sin cambio no se toca
    /// <see cref="UpdatedAtUtc"/> y no hay nada que publicar.</summary>
    public Result<bool> Rename(string name, DateTime nowUtc)
    {
        var guard = GuardEditable();
        if (guard.IsFailure)
            return Result.Failure<bool>(guard.Error);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
            return Result.Failure<bool>(nameResult.Error);

        var changed = ApplyName(nameResult.Value, nowUtc);
        if (changed)
            RaiseUpdated();
        return Result.Success(changed);
    }

    /// <summary>Cambia la zona horaria por defecto (IANA). Devuelve <c>true</c> si cambió.</summary>
    public Result<bool> ChangeTimeZone(string ianaTimeZoneId, DateTime nowUtc)
    {
        var guard = GuardEditable();
        if (guard.IsFailure)
            return Result.Failure<bool>(guard.Error);

        var timeZoneResult = ValidateTimeZone(ianaTimeZoneId);
        if (timeZoneResult.IsFailure)
            return Result.Failure<bool>(timeZoneResult.Error);

        var changed = ApplyTimeZone(timeZoneResult.Value, nowUtc);
        if (changed)
            RaiseUpdated();
        return Result.Success(changed);
    }

    /// <summary>
    /// Edición de estado completo (nombre + zona horaria) como una sola operación: valida ambos antes
    /// de tocar nada, así un dato inválido no deja la mitad aplicada. Devuelve <c>true</c> si algo cambió.
    /// </summary>
    public Result<bool> UpdateNameAndTimeZone(string name, string ianaTimeZoneId, DateTime nowUtc)
    {
        var guard = GuardEditable();
        if (guard.IsFailure)
            return Result.Failure<bool>(guard.Error);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
            return Result.Failure<bool>(nameResult.Error);

        var timeZoneResult = ValidateTimeZone(ianaTimeZoneId);
        if (timeZoneResult.IsFailure)
            return Result.Failure<bool>(timeZoneResult.Error);

        var renamed = ApplyName(nameResult.Value, nowUtc);
        var rezoned = ApplyTimeZone(timeZoneResult.Value, nowUtc);
        if (renamed || rezoned)
            RaiseUpdated();
        return Result.Success(renamed || rezoned);
    }

    public Result Suspend()
    {
        if (Status != EnumTenantStatus.TenantStatus.Active)
            return Result.Failure(new Error("Tenant.Status", "Only active tenants can be suspended."));

        ApplyStatus(EnumTenantStatus.TenantStatus.Suspended);
        RaiseStatusChanged();
        return Result.Success();
    }

    /// <summary>Auditoría F09 — método específico para <c>OnboardingCancelRequestedConsumer</c> en
    /// vez del setter genérico <see cref="ChangeStatus"/>. Misma guarda de <c>Platform</c>; el
    /// destino es siempre <c>Closed</c>, así que la guarda anti-reactivación de
    /// <see cref="ChangeStatus"/> no aplica — Closed→Closed es el propio caso idempotente que este
    /// consumer necesita ante un replay del evento.</summary>
    public Result CloseForOnboardingCancellation()
    {
        if (Kind == TenantKind.Platform)
        {
            return Result.Failure(
                new Error("Tenant.PlatformProtected", "The reserved platform tenant cannot change status.")
            );
        }

        ApplyStatus(EnumTenantStatus.TenantStatus.Closed);
        // También en un replay: los consumidores fijan Closed, que es terminal.
        RaiseStatusChanged();
        return Result.Success();
    }

    public Result ChangeStatus(EnumTenantStatus.TenantStatus status)
    {
        if (Kind == TenantKind.Platform)
        {
            return Result.Failure(
                new Error("Tenant.PlatformProtected", "The reserved platform tenant cannot change status.")
            );
        }

        if (Status == EnumTenantStatus.TenantStatus.Closed && status != EnumTenantStatus.TenantStatus.Closed)
        {
            return Result.Failure(new Error("Tenant.Status", "A closed tenant cannot be reactivated."));
        }

        ApplyStatus(status);
        RaiseStatusChanged();
        return Result.Success();
    }

    private bool ApplyName(string name, DateTime nowUtc)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal))
            return false;

        Name = name;
        Touch(nowUtc);
        return true;
    }

    private bool ApplyTimeZone(string ianaTimeZoneId, DateTime nowUtc)
    {
        if (string.Equals(DefaultTimeZoneId, ianaTimeZoneId, StringComparison.Ordinal))
            return false;

        DefaultTimeZoneId = ianaTimeZoneId;
        Touch(nowUtc);
        return true;
    }

    private void ApplyStatus(EnumTenantStatus.TenantStatus status)
    {
        if (Status != status)
            Version++;
        Status = status;
    }

    // Una sola subida por edición, aunque cambien nombre y zona.
    private void RaiseUpdated()
    {
        Version++;
        AddDomainEvent(new TenantUpdatedDomainEvent(Id, Name, DefaultTimeZoneId, UpdatedAtUtc, Version));
    }

    private void RaiseStatusChanged() =>
        AddDomainEvent(
            new TenantStatusChangedDomainEvent(Id, Status, Status == EnumTenantStatus.TenantStatus.Active, Version)
        );

    // El tenant de plataforma es reservado: su nombre lo fija el seed.
    private Result GuardEditable() =>
        Kind == TenantKind.Platform
            ? Result.Failure(new Error("Tenant.PlatformProtected", "The reserved platform tenant cannot be edited."))
            : Result.Success();

    // Nunca retrocede: el orden de los eventos depende de este sello.
    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc > UpdatedAtUtc ? nowUtc : UpdatedAtUtc;

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<string>(new Error("Tenant.Name", "Name is required."));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            return Result.Failure<string>(
                new Error("Tenant.Name", $"Name must be at most {NameMaxLength} characters long.")
            );

        return Result.Success(trimmed);
    }

    private static Result<string> ValidateTimeZone(string? ianaTimeZoneId) =>
        IanaTimeZone.TryNormalize(ianaTimeZoneId, out var normalized)
            ? Result.Success(normalized)
            : Result.Failure<string>(
                new Error(
                    "Tenant.DefaultTimeZoneId",
                    "DefaultTimeZoneId must be a valid IANA time zone identifier, for example America/Santo_Domingo."
                )
            );

    private static Regex MyRegex()
    {
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
        return new Regex("^[a-z0-9-]{3,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
    }
}
