using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Customer.Domain.Addresses;
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

    private Customer() { }

    public CustomerKind Kind { get; private set; }
    public CustomerStatus Status { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public PersonalName? PersonalName { get; private set; }
    public BusinessIdentity? BusinessIdentity { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public PreferredChannel PreferredChannel { get; private set; }
    public Language Language { get; private set; }
    public EmailAddress PrimaryEmail { get; private set; } = default!;
    public PhoneNumber? PrimaryPhone { get; private set; }
    public Guid? ProfilePictureFileId { get; private set; }
    public Guid? OccupationId { get; private set; }
    public CustomerFiscalProfile? FiscalProfile { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? LastModifiedByUserId { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }

    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses.AsReadOnly();
    public IReadOnlyCollection<CustomerContactPoint> ContactPoints => _contactPoints.AsReadOnly();
    public IReadOnlyCollection<CustomerRelation> Relations => _relations.AsReadOnly();

    public static Result<Customer> Register(
        Guid tenantId,
        CustomerKind kind,
        PersonalName? personalName,
        BusinessIdentity? businessIdentity,
        EmailAddress primaryEmail,
        PhoneNumber? primaryPhone,
        Language language,
        PreferredChannel preferredChannel,
        Guid createdByUserId,
        DateOnly? dateOfBirth = null,
        Guid? occupationId = null
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
            PreferredChannel = preferredChannel,
            PrimaryEmail = primaryEmail,
            PrimaryPhone = primaryPhone,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
        };
        customer.SetTenant(tenantId);
        return Result.Success(customer);
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

    public Result ChangePreferences(Language language, PreferredChannel channel, Guid byUserId)
    {
        EnsureActive();
        Language = language;
        PreferredChannel = channel;
        Touch(byUserId);
        return Result.Success();
    }

    public Result SetProfilePicture(Guid? fileId, Guid byUserId)
    {
        EnsureActive();
        ProfilePictureFileId = fileId;
        Touch(byUserId);
        return Result.Success();
    }

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
        Guid byUserId
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
                isReturningCustomer: isReturningCustomer
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
                byUserId
            );
            FiscalProfile.Update(filingStatus, priorYearAgi, isReturningCustomer, byUserId);
            FiscalProfile.SetRefundBank(refundBankAccountCipher, refundBankRoutingCipher, byUserId);
        }

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

    public Result Archive(Guid byUserId)
    {
        if (Status == CustomerStatus.Archived)
            return Result.Failure(new Error("Customer.AlreadyArchived", "Customer is already archived."));

        Status = CustomerStatus.Archived;
        ArchivedAtUtc = DateTime.UtcNow;
        ArchivedByUserId = byUserId;
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

    private void EnsureActive()
    {
        if (Status == CustomerStatus.Archived)
            throw new InvalidOperationException("Customer is archived and cannot be modified.");
    }
}
