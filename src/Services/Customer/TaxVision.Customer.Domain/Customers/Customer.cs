using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Assignments;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;

namespace TaxVision.Customer.Domain.Customers;

public sealed class Customer : TenantEntity
{
    private readonly List<CustomerAddress> _addresses = [];
    private readonly List<CustomerContactPoint> _contactPoints = [];
    private readonly List<CustomerRelation> _relations = [];
    private readonly List<CustomerAssignment> _assignments = [];

    // El origen se persiste en columnas de la raíz: el índice único necesita TenantId en la misma entidad.
    private string? _originSourceService;
    private Guid? _originSourceReferenceId;

    private Customer() { }

    public CustomerKind Kind { get; private set; }
    public CustomerStatus Status { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public PersonalName? PersonalName { get; private set; }
    public BusinessIdentity? BusinessIdentity { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }

    /// <summary>
    /// Política por canal. Default ambos ON (opt-out). Lo lee Notification vía proyección para
    /// decidir fan-out por canal. OTP/transaccionales siempre salen (no se gatean por esto).
    /// </summary>
    public ContactPolicy ContactPolicy { get; private set; } = ContactPolicy.Default;
    public Language Language { get; private set; }
    public EmailAddress PrimaryEmail { get; private set; } = default!;
    public PhoneNumber? PrimaryPhone { get; private set; }

    /// <summary>Foto de perfil vigente (ya pasó el antivirus). La sirve CloudStorage en <c>/storage/avatars/{id}</c>.</summary>
    public Guid? ProfilePictureFileId { get; private set; }

    /// <summary>Foto subida que espera el resultado del escaneo; no se muestra hasta confirmarse.</summary>
    public Guid? PendingProfilePictureFileId { get; private set; }
    public DateTime? PendingProfilePictureRequestedAtUtc { get; private set; }

    /// <summary>Moderación de contenido (T-20) de la última foto subida: en revisión o bloqueada.</summary>
    public Guid? ProfilePictureModerationFileId { get; private set; }
    public ProfilePictureModerationState? ProfilePictureModerationState { get; private set; }
    public Guid? OccupationId { get; private set; }
    public Guid? AssignedPreparerUserId { get; private set; }
    public CustomerFiscalProfile? FiscalProfile { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? LastModifiedByUserId { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }

    public CustomerOrigin? Origin =>
        _originSourceReferenceId is { } referenceId
            ? CustomerOrigin.Create(_originSourceService!, referenceId).Value
            : null;

    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses.AsReadOnly();
    public IReadOnlyCollection<CustomerContactPoint> ContactPoints => _contactPoints.AsReadOnly();
    public IReadOnlyCollection<CustomerRelation> Relations => _relations.AsReadOnly();
    public IReadOnlyCollection<CustomerAssignment> Assignments => _assignments.AsReadOnly();

    public static Result<Customer> Register(
        Guid tenantId,
        CustomerKind kind,
        PersonalName? personalName,
        BusinessIdentity? businessIdentity,
        EmailAddress primaryEmail,
        PhoneNumber? primaryPhone,
        Language language,
        Guid createdByUserId,
        DateOnly? dateOfBirth = null,
        Guid? occupationId = null,
        ContactPolicy? contactPolicy = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Customer>(new Error("Customer.Tenant", "Tenant is required."));
        if (tenantId == PlatformTenant.Id)
            return Result.Failure<Customer>(
                new Error("Customer.PlatformTenant", "Customers cannot belong to the reserved platform tenant.")
            );
        if (createdByUserId == Guid.Empty)
            return Result.Failure<Customer>(new Error("Customer.CreatedBy", "CreatedByUserId is required."));

        switch (kind)
        {
            case CustomerKind.Individual when personalName is null:
                return Result.Failure<Customer>(
                    new Error("Customer.PersonalName", "Individual customers require a personal name.")
                );
            case CustomerKind.Individual when businessIdentity is not null:
                return Result.Failure<Customer>(
                    new Error("Customer.IndividualExtras", "Individual customers cannot have a business identity.")
                );
            case CustomerKind.Business when businessIdentity is null:
                return Result.Failure<Customer>(
                    new Error("Customer.BusinessIdentity", "Business customers require a business identity.")
                );
        }

        var displayName = kind == CustomerKind.Individual ? personalName!.DisplayName : businessIdentity!.LegalName;

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Status = CustomerStatus.Active,
            DisplayName = displayName,
            PersonalName = personalName,
            BusinessIdentity = businessIdentity,
            DateOfBirth = dateOfBirth,
            OccupationId = occupationId,
            Language = language,
            ContactPolicy = contactPolicy ?? ContactPolicy.Default,
            PrimaryEmail = primaryEmail,
            PrimaryPhone = primaryPhone,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
        };
        customer.SetTenant(tenantId);
        return Result.Success(customer);
    }

    /// <summary>Se fija una sola vez, en la misma transacción del alta; nunca se reescribe.</summary>
    public Result RecordOrigin(CustomerOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (_originSourceReferenceId is not null)
            return Result.Failure(
                new Error("Customer.OriginAlreadyRecorded", "The customer origin is already recorded.")
            );

        _originSourceService = origin.SourceService;
        _originSourceReferenceId = origin.SourceReferenceId;
        return Result.Success();
    }

    private void Touch(Guid byUserId)
    {
        UpdatedAtUtc = DateTime.UtcNow;
        LastModifiedByUserId = byUserId;
    }

    public Result ChangePrimaryEmail(EmailAddress newEmail, Guid byUserId)
    {
        EnsureActive();
        PrimaryEmail = newEmail;
        Touch(byUserId);
        return Result.Success();
    }

    public Result ChangePrimaryPhone(PhoneNumber? newPhone, Guid byUserId)
    {
        EnsureActive();
        PrimaryPhone = newPhone;
        Touch(byUserId);
        return Result.Success();
    }

    public Result ChangeLanguage(Language language, Guid byUserId)
    {
        EnsureActive();
        Language = language;
        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>
    /// Cambia la política de contacto por canal. Notification la lee vía proyección para gatear
    /// email/SMS no-críticos (OTP/transaccionales siempre salen).
    /// </summary>
    public Result ChangeContactPolicy(bool emailEnabled, bool smsEnabled, Guid byUserId)
    {
        EnsureActive();
        ContactPolicy = new ContactPolicy(emailEnabled, smsEnabled);
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Foto de perfil ==============

    /// <summary>Pasado este tiempo sin resultado del escaneo, la foto pendiente se da por fallida.</summary>
    public static readonly TimeSpan PendingProfilePictureTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registra la foto recién subida como pendiente. Reemplaza una pendiente anterior: si esa llega
    /// después, ya no coincide y se trata como huérfana.
    /// </summary>
    public Result BeginProfilePictureChange(Guid fileId, Guid byUserId, DateTime nowUtc)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure(CustomerProfilePictureErrors.Archived);
        if (fileId == Guid.Empty)
            return Result.Failure(CustomerProfilePictureErrors.FileIdRequired);

        PendingProfilePictureFileId = fileId;
        PendingProfilePictureRequestedAtUtc = nowUtc;
        ClearProfilePictureModeration();
        Touch(byUserId);
        return Result.Success();
    }

    public bool IsPendingProfilePictureUnderReview =>
        PendingProfilePictureFileId is { } pending
        && ProfilePictureModerationFileId == pending
        && ProfilePictureModerationState == Customers.ProfilePictureModerationState.UnderReview;

    /// <summary>La moderación dejó la pendiente en revisión humana; true si era esa foto.</summary>
    public bool HoldPendingProfilePictureForReview(Guid fileId)
    {
        if (PendingProfilePictureFileId != fileId)
            return false;
        ProfilePictureModerationFileId = fileId;
        ProfilePictureModerationState = Customers.ProfilePictureModerationState.UnderReview;
        return true;
    }

    /// <summary>La foto pendiente viola la política de contenido: se descarta y queda constancia.</summary>
    public bool RejectPendingProfilePictureForPolicy(Guid fileId)
    {
        if (!DiscardPendingProfilePicture(fileId))
            return false;
        ProfilePictureModerationFileId = fileId;
        ProfilePictureModerationState = Customers.ProfilePictureModerationState.Blocked;
        return true;
    }

    /// <summary>
    /// El escaneo aprobó la foto: pasa a ser la vigente y devuelve la anterior (para borrarla). Si el
    /// fileId no es el pendiente (llegó tarde, se quitó o se archivó el cliente) falla con <c>Stale</c>.
    /// </summary>
    public Result<Guid?> ConfirmProfilePicture(Guid fileId)
    {
        if (Status == CustomerStatus.Archived || PendingProfilePictureFileId != fileId)
            return Result.Failure<Guid?>(CustomerProfilePictureErrors.Stale);

        var previous = ProfilePictureFileId;
        ProfilePictureFileId = fileId;
        ClearPendingProfilePicture();
        ClearProfilePictureModeration();
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success(previous);
    }

    /// <summary>Descarta la pendiente solo si sigue siendo esa. Devuelve si hubo cambio.</summary>
    public bool DiscardPendingProfilePicture(Guid fileId)
    {
        if (PendingProfilePictureFileId != fileId)
            return false;

        ClearPendingProfilePicture();
        return true;
    }

    /// <summary>En revisión de contenido no vence: espera al revisor.</summary>
    public bool IsPendingProfilePictureExpired(DateTime nowUtc) =>
        !IsPendingProfilePictureUnderReview
        && PendingProfilePictureRequestedAtUtc is { } requestedAt
        && nowUtc - requestedAt >= PendingProfilePictureTimeout;

    /// <summary>Quita la foto vigente y cualquier pendiente. Devuelve la vigente que había (null si no había).</summary>
    public Result<Guid?> RemoveProfilePicture(Guid byUserId)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure<Guid?>(CustomerProfilePictureErrors.Archived);

        var removed = ProfilePictureFileId;
        if (removed is null && PendingProfilePictureFileId is null)
            return Result.Success<Guid?>(null);

        ProfilePictureFileId = null;
        ClearPendingProfilePicture();
        ClearProfilePictureModeration();
        Touch(byUserId);
        return Result.Success(removed);
    }

    private void ClearPendingProfilePicture()
    {
        PendingProfilePictureFileId = null;
        PendingProfilePictureRequestedAtUtc = null;
    }

    private void ClearProfilePictureModeration()
    {
        ProfilePictureModerationFileId = null;
        ProfilePictureModerationState = null;
    }

    // ============== Identidad (nombre / negocio / DOB) ==============

    public Result ChangePersonalName(PersonalName newName, Guid byUserId)
    {
        EnsureActive();
        if (Kind != CustomerKind.Individual)
            return Result.Failure(
                new Error(
                    "Customer.NotIndividual",
                    "Personal name can only be changed on Individual customers. Use ChangeBusinessIdentity for Business."
                )
            );
        PersonalName = newName;
        DisplayName = newName.DisplayName;
        Touch(byUserId);
        return Result.Success();
    }

    public Result ChangeBusinessIdentity(BusinessIdentity newIdentity, Guid byUserId)
    {
        EnsureActive();
        if (Kind != CustomerKind.Business)
            return Result.Failure(
                new Error(
                    "Customer.NotBusiness",
                    "Business identity can only be changed on Business customers. Use ChangePersonalName for Individual."
                )
            );
        BusinessIdentity = newIdentity;
        DisplayName = newIdentity.LegalName;
        Touch(byUserId);
        return Result.Success();
    }

    public Result ChangeDateOfBirth(DateOnly? dateOfBirth, Guid byUserId)
    {
        EnsureActive();
        DateOfBirth = dateOfBirth;
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Addresses ==============

    public Result<CustomerAddress> AddAddress(AddressKind kind, AddressValue address, bool isPrimary, Guid byUserId)
    {
        EnsureActive();

        if (isPrimary && _addresses.Any(a => a.Kind == kind && a.IsPrimary))
            return Result.Failure<CustomerAddress>(
                new Error("Customer.PrimaryAddress", $"A primary {kind} address already exists.")
            );

        var created = CustomerAddress.Create(TenantId, Id, kind, address, isPrimary);
        if (created.IsFailure)
            return created;

        _addresses.Add(created.Value);
        Touch(byUserId);
        return created;
    }

    public Result UpdateAddress(Guid addressId, AddressKind kind, AddressValue address, bool isPrimary, Guid byUserId)
    {
        EnsureActive();
        var existing = _addresses.FirstOrDefault(a => a.Id == addressId);
        if (existing is null)
            return Result.Failure(new Error("Customer.AddressNotFound", "Address not found."));

        if (isPrimary && _addresses.Any(a => a.Id != addressId && a.Kind == kind && a.IsPrimary))
            return Result.Failure(new Error("Customer.PrimaryAddress", $"A primary {kind} address already exists."));

        existing.Update(kind, address, isPrimary);
        Touch(byUserId);
        return Result.Success();
    }

    public Result RemoveAddress(Guid addressId, Guid byUserId)
    {
        EnsureActive();
        var existing = _addresses.FirstOrDefault(a => a.Id == addressId);
        if (existing is null)
            return Result.Failure(new Error("Customer.AddressNotFound", "Address not found."));

        _addresses.Remove(existing);
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Contact points ==============

    public Result<CustomerContactPoint> AddContactPoint(
        ContactPointType type,
        string value,
        string normalizedValue,
        string? label,
        bool isPrimary,
        Guid byUserId
    )
    {
        EnsureActive();

        if (isPrimary && _contactPoints.Any(c => c.Type == type && c.IsPrimary))
            return Result.Failure<CustomerContactPoint>(
                new Error("Customer.PrimaryContactPoint", $"A primary {type} contact point already exists.")
            );

        if (_contactPoints.Any(c => c.Type == type && c.NormalizedValue == normalizedValue))
            return Result.Failure<CustomerContactPoint>(
                new Error("Customer.DuplicateContactPoint", "Duplicate contact point.")
            );

        var created = CustomerContactPoint.Create(TenantId, Id, type, value, normalizedValue, label, isPrimary);
        if (created.IsFailure)
            return created;

        _contactPoints.Add(created.Value);
        Touch(byUserId);
        return created;
    }

    public Result UpdateContactPoint(
        Guid contactPointId,
        ContactPointType type,
        string value,
        string normalizedValue,
        string? label,
        bool isPrimary,
        Guid byUserId
    )
    {
        EnsureActive();
        var existing = _contactPoints.FirstOrDefault(c => c.Id == contactPointId);
        if (existing is null)
            return Result.Failure(new Error("Customer.ContactPointNotFound", "Contact point not found."));

        if (isPrimary && _contactPoints.Any(c => c.Id != contactPointId && c.Type == type && c.IsPrimary))
            return Result.Failure(
                new Error("Customer.PrimaryContactPoint", $"A primary {type} contact point already exists.")
            );

        if (_contactPoints.Any(c => c.Id != contactPointId && c.Type == type && c.NormalizedValue == normalizedValue))
            return Result.Failure(new Error("Customer.DuplicateContactPoint", "Duplicate contact point."));

        existing.Update(type, value, normalizedValue, label, isPrimary);
        Touch(byUserId);
        return Result.Success();
    }

    public Result RemoveContactPoint(Guid contactPointId, Guid byUserId)
    {
        EnsureActive();
        var existing = _contactPoints.FirstOrDefault(c => c.Id == contactPointId);
        if (existing is null)
            return Result.Failure(new Error("Customer.ContactPointNotFound", "Contact point not found."));

        _contactPoints.Remove(existing);
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Relations ==============

    public Result<CustomerRelation> AddRelation(
        RelationshipKind kind,
        RelationPurpose purposes,
        PersonalName name,
        EmailAddress? email,
        PhoneNumber? phone,
        DateOnly? dateOfBirth,
        AddressValue? address,
        Guid byUserId
    )
    {
        EnsureActive();
        var created = CustomerRelation.Create(TenantId, Id, kind, purposes, name, email, phone, dateOfBirth, address);
        if (created.IsFailure)
            return created;

        _relations.Add(created.Value);
        Touch(byUserId);
        return created;
    }

    public Result UpdateRelation(
        Guid relationId,
        RelationshipKind kind,
        RelationPurpose purposes,
        PersonalName name,
        EmailAddress? email,
        PhoneNumber? phone,
        DateOnly? dateOfBirth,
        AddressValue? address,
        Guid byUserId
    )
    {
        EnsureActive();
        var existing = _relations.FirstOrDefault(r => r.Id == relationId);
        if (existing is null)
            return Result.Failure(new Error("Customer.RelationNotFound", "Relation not found."));

        existing.Update(kind, purposes, name, email, phone, dateOfBirth, address);
        Touch(byUserId);
        return Result.Success();
    }

    public Result RemoveRelation(Guid relationId, Guid byUserId)
    {
        EnsureActive();
        var existing = _relations.FirstOrDefault(r => r.Id == relationId);
        if (existing is null)
            return Result.Failure(new Error("Customer.RelationNotFound", "Relation not found."));

        _relations.Remove(existing);
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Fiscal profile ==============

    public Result SetFiscalProfile(
        FiscalProfiles.FiscalSubjectKind subjectKind,
        byte[] taxIdentifierCipher,
        string taxIdentifierBlindIndex,
        string taxIdentifierLast4,
        FiscalProfiles.FilingStatus? filingStatus,
        decimal? priorYearAgi,
        bool isReturningCustomer,
        byte[]? refundBankAccountCipher,
        byte[]? refundBankRoutingCipher,
        Guid byUserId,
        short taxIdentifierKeyVersion = 1
    )
    {
        EnsureActive();

        if (FiscalProfile is null)
        {
            var createResult = FiscalProfiles.CustomerFiscalProfile.Create(
                tenantId: TenantId,
                customerId: Id,
                subjectKind: subjectKind,
                taxIdentifierCipher: taxIdentifierCipher,
                taxIdentifierBlindIndex: taxIdentifierBlindIndex,
                taxIdentifierLast4: taxIdentifierLast4,
                updatedByUserId: byUserId,
                filingStatus: filingStatus,
                priorYearAgi: priorYearAgi,
                isReturningCustomer: isReturningCustomer,
                taxIdentifierKeyVersion: taxIdentifierKeyVersion
            );
            if (createResult.IsFailure)
                return createResult;

            if (refundBankAccountCipher is not null)
                createResult.Value.SetRefundBank(refundBankAccountCipher, refundBankRoutingCipher, byUserId);

            FiscalProfile = createResult.Value;
        }
        else
        {
            FiscalProfile.ReplaceTaxIdentifier(
                taxIdentifierCipher,
                taxIdentifierBlindIndex,
                taxIdentifierLast4,
                subjectKind,
                byUserId,
                taxIdentifierKeyVersion
            );
            FiscalProfile.Update(filingStatus, priorYearAgi, isReturningCustomer, byUserId);
            FiscalProfile.SetRefundBank(refundBankAccountCipher, refundBankRoutingCipher, byUserId);
        }

        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>
    /// Actualiza los campos NO sensibles del perfil fiscal (filing/AGI/returning) sin tocar el
    /// identificador cifrado ni su last4 — para editar el perfil sin re-teclear el SSN/EIN. El banco
    /// solo se reemplaza si se pasan cifras nuevas; si vienen null se CONSERVA lo existente (a
    /// diferencia de <see cref="SetFiscalProfile"/>, que reemplaza el perfil entero). Exige que el
    /// perfil ya exista (crear uno requiere identificador).
    /// </summary>
    public Result UpdateFiscalProfile(
        FiscalProfiles.FilingStatus? filingStatus,
        decimal? priorYearAgi,
        bool isReturningCustomer,
        byte[]? refundBankAccountCipher,
        byte[]? refundBankRoutingCipher,
        Guid byUserId
    )
    {
        EnsureActive();

        if (FiscalProfile is null)
            return Result.Failure(new Error("FiscalProfile.NotFound", "There is no fiscal profile to update."));

        FiscalProfile.Update(filingStatus, priorYearAgi, isReturningCustomer, byUserId);

        // Banco solo si se envió (ambas cifras). Vacío = conservar el actual, no borrarlo.
        if (refundBankAccountCipher is not null && refundBankRoutingCipher is not null)
            FiscalProfile.SetRefundBank(refundBankAccountCipher, refundBankRoutingCipher, byUserId);

        Touch(byUserId);
        return Result.Success();
    }

    public Result ChangeOccupation(Guid? occupationId, Guid byUserId)
    {
        EnsureActive();
        OccupationId = occupationId;
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Asignación de staff (acceso por cliente, M:N) + preparador primary ==============

    /// <summary>Fija el preparador PRIMARY (responsable): asegura su fila de asignación, degrada a cualquier
    /// otro primary y denormaliza en <c>AssignedPreparerUserId</c> (para 8879/offboard/chat).</summary>
    public Result AssignPreparer(Guid preparerUserId, Guid byUserId)
    {
        EnsureActive();
        if (preparerUserId == Guid.Empty)
            return Result.Failure(new Error("Customer.InvalidPreparer", "PreparerUserId is required."));

        foreach (var other in _assignments.Where(a => a.IsPrimary && a.UserId != preparerUserId))
            other.SetPrimary(false);

        var existing = _assignments.FirstOrDefault(a => a.UserId == preparerUserId);
        if (existing is null)
            _assignments.Add(CustomerAssignment.Create(TenantId, Id, preparerUserId, isPrimary: true, byUserId));
        else
            existing.SetPrimary(true);

        AssignedPreparerUserId = preparerUserId;
        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>Traspasa el preparador PRIMARY de un empleado que se retira a otro: QUITA la fila del
    /// saliente (no la deja degradada, no debe conservar acceso) y deja al nuevo como primary. Para el
    /// offboard con sucesor.</summary>
    public Result HandOverPreparer(Guid fromUserId, Guid toUserId, Guid byUserId)
    {
        EnsureActive();
        if (toUserId == Guid.Empty)
            return Result.Failure(new Error("Customer.InvalidPreparer", "PreparerUserId is required."));

        _assignments.RemoveAll(a => a.UserId == fromUserId);

        foreach (var other in _assignments.Where(a => a.IsPrimary && a.UserId != toUserId))
            other.SetPrimary(false);

        var existing = _assignments.FirstOrDefault(a => a.UserId == toUserId);
        if (existing is null)
            _assignments.Add(CustomerAssignment.Create(TenantId, Id, toUserId, isPrimary: true, byUserId));
        else
            existing.SetPrimary(true);

        AssignedPreparerUserId = toUserId;
        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>Quita al preparador PRIMARY: elimina su fila y limpia la denormalización. Los accesos
    /// adicionales (no-primary) quedan intactos.</summary>
    public Result UnassignPreparer(Guid byUserId)
    {
        EnsureActive();
        if (AssignedPreparerUserId is null)
            return Result.Failure(new Error("Customer.NoPreparerAssigned", "Customer has no preparer assigned."));

        _assignments.RemoveAll(a => a.UserId == AssignedPreparerUserId);
        AssignedPreparerUserId = null;
        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>Da acceso a un miembro del staff SIN hacerlo primary (asignación adicional). Idempotente.</summary>
    public Result GrantAccess(Guid userId, Guid byUserId)
    {
        EnsureActive();
        if (userId == Guid.Empty)
            return Result.Failure(new Error("Customer.InvalidAssignee", "UserId is required."));

        if (_assignments.All(a => a.UserId != userId))
            _assignments.Add(CustomerAssignment.Create(TenantId, Id, userId, isPrimary: false, byUserId));

        Touch(byUserId);
        return Result.Success();
    }

    /// <summary>Revoca el acceso de un miembro del staff. Si era el primary, limpia la denormalización.</summary>
    public Result RevokeAccess(Guid userId, Guid byUserId)
    {
        EnsureActive();
        var existing = _assignments.FirstOrDefault(a => a.UserId == userId);
        if (existing is null)
            return Result.Failure(new Error("Customer.NotAssigned", "User is not assigned to this customer."));

        var wasPrimary = existing.IsPrimary;
        _assignments.Remove(existing);
        if (wasPrimary)
            AssignedPreparerUserId = null;
        Touch(byUserId);
        return Result.Success();
    }

    // ============== Status transitions ==============

    public Result Archive(Guid byUserId)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure(new Error("Customer.AlreadyArchived", "Customer is already archived."));

        Status = CustomerStatus.Archived;
        ArchivedAtUtc = DateTime.UtcNow;
        ArchivedByUserId = byUserId;
        // Archivar retira la foto: quien archiva pide el borrado de la que había.
        ProfilePictureFileId = null;
        ClearPendingProfilePicture();
        Touch(byUserId);
        return Result.Success();
    }

    public Result Reactivate(Guid byUserId)
    {
        if (Status != CustomerStatus.Archived)
            return Result.Failure(new Error("Customer.NotArchived", "Only archived customers can be reactivated."));

        Status = CustomerStatus.Active;
        ArchivedAtUtc = null;
        ArchivedByUserId = null;
        Touch(byUserId);
        return Result.Success();
    }

    public Result Deactivate(Guid byUserId)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure(
                new Error("Customer.Archived", "Cannot deactivate an archived customer; reactivate first.")
            );
        if (Status == CustomerStatus.Inactive)
            return Result.Failure(new Error("Customer.AlreadyInactive", "Customer is already inactive."));

        Status = CustomerStatus.Inactive;
        Touch(byUserId);
        return Result.Success();
    }

    public Result Activate(Guid byUserId)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure(
                new Error("Customer.Archived", "Cannot activate an archived customer; reactivate first.")
            );
        if (Status == CustomerStatus.Active)
            return Result.Failure(new Error("Customer.AlreadyActive", "Customer is already active."));

        Status = CustomerStatus.Active;
        Touch(byUserId);
        return Result.Success();
    }

    private void EnsureActive()
    {
        if (Status == CustomerStatus.Archived)
            throw new InvalidOperationException("Customer is archived and cannot be modified.");
    }
}
