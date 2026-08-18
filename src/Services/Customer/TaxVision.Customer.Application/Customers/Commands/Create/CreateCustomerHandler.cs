using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Commands.Create;

public static class CreateCustomerHandler
{
    public static async Task<Result<CustomerResponse>> Handle(
        CreateCustomerCommand cmd,
        ICustomerRepository repository,
        ICustomerDuplicateDetector duplicates,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var voResult = BuildValueObjects(cmd);
        if (voResult.IsFailure)
            return Result.Failure<CustomerResponse>(voResult.Error);

        var vo = voResult.Value;

        // El mismo detector que usa el import: dos puertas al mismo dato con reglas distintas terminan
        // en que la laxa se usa para meter lo que la otra rechaza.
        var match = await duplicates.FindDuplicateAsync(
            cmd.TenantId,
            new CustomerDuplicateCandidate(
                vo.Email.Value,
                vo.Phone?.E164Value,
                DisplayNameOf(cmd, vo),
                cmd.DateOfBirth
            ),
            excludeCustomerId: null,
            ct
        );

        if (match is not null)
        {
            if (!cmd.Overwrite)
                return Result.Failure<CustomerResponse>(
                    CustomerDuplicateErrors.DuplicateFound(
                        match.ExistingCustomerId,
                        match.ExistingDisplayName,
                        match.MatchedBy
                    )
                );

            return await OverwriteAsync(
                match.ExistingCustomerId,
                cmd,
                vo,
                repository,
                unitOfWork,
                bus,
                correlation,
                ct
            );
        }

        var customerResult = RegisterCustomer(cmd, vo);
        if (customerResult.IsFailure)
            return Result.Failure<CustomerResponse>(customerResult.Error);

        var customer = customerResult.Value;
        await PersistCustomerAsync(customer, repository, unitOfWork, ct);
        await PublishCreatedEventAsync(customer, correlation, bus);

        return Result.Success(MapToResponse(customer));
    }

    /// <summary>
    /// Le aplica los datos nuevos al cliente que ya estaba, en vez de crear otro.
    ///
    /// <para>
    /// Publica <c>CustomerUpdated</c> y <b>no</b> <c>CustomerCreated</c>: los siete servicios que
    /// proyectan el directorio de clientes aplican por fecha, y anunciar como alta lo que fue una
    /// edición les diría que existe un cliente que no nació hoy.
    /// </para>
    /// </summary>
    private static async Task<Result<CustomerResponse>> OverwriteAsync(
        Guid existingCustomerId,
        CreateCustomerCommand cmd,
        CustomerValueObjects vo,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByIdAsync(existingCustomerId, ct);
        if (existing is null || existing.TenantId != cmd.TenantId)
            return Result.Failure<CustomerResponse>(new Error("Customer.NotFound", "Customer not found."));

        var applied = ApplyOverwrite(existing, cmd, vo);
        if (applied.IsFailure)
            return Result.Failure<CustomerResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = existing.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = existing.Id,
                DisplayName = existing.DisplayName,
                PrimaryEmail = existing.PrimaryEmail.Value,
                PrimaryPhone = existing.PrimaryPhone?.E164Value,
                Language = existing.Language.ToString(),
                PreferredChannel = existing.PreferredChannel.ToString(),
                OccupationId = existing.OccupationId,
                ModifiedByUserId = cmd.CreatedByUserId,
            }
        );

        return Result.Success(MapToResponse(existing));
    }

    private static Result ApplyOverwrite(CustomerEntity existing, CreateCustomerCommand cmd, CustomerValueObjects vo)
    {
        var results = new List<Result>
        {
            existing.ChangePrimaryEmail(vo.Email, cmd.CreatedByUserId),
            existing.ChangePrimaryPhone(vo.Phone, cmd.CreatedByUserId),
            existing.ChangePreferences(cmd.Language, cmd.PreferredChannel, cmd.CreatedByUserId),
            existing.ChangeOccupation(cmd.OccupationId, cmd.CreatedByUserId),
        };

        if (vo.PersonalName is not null && existing.Kind == CustomerKind.Individual)
            results.Add(existing.ChangePersonalName(vo.PersonalName, cmd.CreatedByUserId));

        if (vo.BusinessIdentity is not null && existing.Kind == CustomerKind.Business)
            results.Add(existing.ChangeBusinessIdentity(vo.BusinessIdentity, cmd.CreatedByUserId));

        if (cmd.DateOfBirth.HasValue)
            results.Add(existing.ChangeDateOfBirth(cmd.DateOfBirth, cmd.CreatedByUserId));

        foreach (var r in results)
            if (r.IsFailure)
                return r;

        return Result.Success();
    }

    /// <summary>Lo que se compara contra el nombre guardado: el aggregate lo arma igual al registrar.</summary>
    private static string? DisplayNameOf(CreateCustomerCommand cmd, CustomerValueObjects vo) =>
        cmd.Kind == CustomerKind.Business ? vo.BusinessIdentity?.LegalName : vo.PersonalName?.DisplayName;

    // ============== Fase 1: construir value objects desde el comando ==============

    private sealed record CustomerValueObjects(
        EmailAddress Email,
        PhoneNumber? Phone,
        PersonalName? PersonalName,
        BusinessIdentity? BusinessIdentity
    );

    private static Result<CustomerValueObjects> BuildValueObjects(CreateCustomerCommand cmd)
    {
        var emailResult = EmailAddress.Create(cmd.PrimaryEmail);
        if (emailResult.IsFailure)
            return Result.Failure<CustomerValueObjects>(emailResult.Error);

        var phoneResult = BuildPhone(cmd.PrimaryPhone);
        if (phoneResult.IsFailure)
            return Result.Failure<CustomerValueObjects>(phoneResult.Error);

        var nameResult = BuildPersonalName(cmd);
        if (nameResult.IsFailure)
            return Result.Failure<CustomerValueObjects>(nameResult.Error);

        var businessResult = BuildBusinessIdentity(cmd);
        if (businessResult.IsFailure)
            return Result.Failure<CustomerValueObjects>(businessResult.Error);

        return Result.Success(
            new CustomerValueObjects(emailResult.Value, phoneResult.Value, nameResult.Value, businessResult.Value)
        );
    }

    private static Result<PhoneNumber?> BuildPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Success<PhoneNumber?>(null);

        var phoneResult = PhoneNumber.Create(raw);
        return phoneResult.IsFailure
            ? Result.Failure<PhoneNumber?>(phoneResult.Error)
            : Result.Success<PhoneNumber?>(phoneResult.Value);
    }

    private static Result<PersonalName?> BuildPersonalName(CreateCustomerCommand cmd)
    {
        if (cmd.Kind != CustomerKind.Individual)
            return Result.Success<PersonalName?>(null);

        var nameResult = PersonalName.Create(
            cmd.FirstName ?? string.Empty,
            cmd.LastName ?? string.Empty,
            cmd.MiddleName,
            cmd.Prefix,
            cmd.Suffix
        );
        return nameResult.IsFailure
            ? Result.Failure<PersonalName?>(nameResult.Error)
            : Result.Success<PersonalName?>(nameResult.Value);
    }

    private static Result<BusinessIdentity?> BuildBusinessIdentity(CreateCustomerCommand cmd)
    {
        if (cmd.Kind != CustomerKind.Business)
            return Result.Success<BusinessIdentity?>(null);

        if (cmd.BusinessStructure is null)
            return Result.Failure<BusinessIdentity?>(
                new Error("Customer.BusinessStructure", "Business structure is required.")
            );

        var bizResult = BusinessIdentity.Create(
            cmd.LegalName ?? string.Empty,
            cmd.BusinessStructure.Value,
            cmd.Dba,
            cmd.FormationDate,
            cmd.PrincipalBusinessActivityId
        );
        return bizResult.IsFailure
            ? Result.Failure<BusinessIdentity?>(bizResult.Error)
            : Result.Success<BusinessIdentity?>(bizResult.Value);
    }

    // ============== Fase 2: invocar factory del aggregate ==============

    private static Result<CustomerEntity> RegisterCustomer(CreateCustomerCommand cmd, CustomerValueObjects vo) =>
        CustomerEntity.Register(
            cmd.TenantId,
            cmd.Kind,
            vo.PersonalName,
            vo.BusinessIdentity,
            vo.Email,
            vo.Phone,
            cmd.Language,
            cmd.PreferredChannel,
            createdByUserId: cmd.CreatedByUserId,
            dateOfBirth: cmd.DateOfBirth,
            occupationId: cmd.OccupationId
        );

    // ============== Fase 3: persistir en BD ==============

    private static async Task PersistCustomerAsync(
        CustomerEntity customer,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await repository.AddAsync(customer, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // ============== Fase 4: publicar evento de integracion ==============

    private static Task PublishCreatedEventAsync(
        CustomerEntity customer,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new CustomerCreatedIntegrationEvent
                {
                    TenantId = customer.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = customer.Id,
                    Kind = customer.Kind.ToString(),
                    DisplayName = customer.DisplayName,
                    PrimaryEmail = customer.PrimaryEmail.Value,
                    PrimaryPhone = customer.PrimaryPhone?.E164Value,
                    Language = customer.Language.ToString(),
                    PreferredChannel = customer.PreferredChannel.ToString(),
                    OccupationId = customer.OccupationId,
                    CreatedByUserId = customer.CreatedByUserId,
                }
            )
            .AsTask();

    // ============== Mapeo aggregate -> response ==============

    private static CustomerResponse MapToResponse(CustomerEntity c) =>
        new(
            c.Id,
            c.TenantId,
            c.Kind,
            c.Status,
            c.DisplayName,
            c.PrimaryEmail.Value,
            c.PrimaryPhone?.E164Value,
            c.Language,
            c.PreferredChannel,
            c.OccupationId,
            OccupationName: null,
            c.BusinessIdentity?.PrincipalBusinessActivityId,
            PrincipalBusinessActivityName: null,
            c.CreatedAtUtc
        );
}
