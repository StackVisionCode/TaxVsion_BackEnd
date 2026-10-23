using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.FromForms;

/// <summary>Datos del alta resueltos desde los destinos del formulario, antes de pasar por el alta existente.</summary>
public sealed class CustomerFormDraft
{
    public CustomerKind? Kind { get; internal set; }
    public string? FirstName { get; internal set; }
    public string? MiddleName { get; internal set; }
    public string? LastName { get; internal set; }
    public string? Prefix { get; internal set; }
    public string? Suffix { get; internal set; }
    public string? LegalName { get; internal set; }
    public BusinessStructure? BusinessStructure { get; internal set; }
    public string? Dba { get; internal set; }
    public DateOnly? FormationDate { get; internal set; }
    public DateOnly? DateOfBirth { get; internal set; }
    public string? PrimaryEmail { get; internal set; }
    public string? PrimaryPhone { get; internal set; }
    public Language? Language { get; internal set; }
    public bool EmailEnabled { get; internal set; } = true;
    public bool SmsEnabled { get; internal set; } = true;
    public ClientFormAddressValue? HomeAddress { get; internal set; }
    public ClientFormAddressValue? MailingAddress { get; internal set; }

    /// <summary>Un alta automática nunca sobreescribe a un cliente existente.</summary>
    public CreateCustomerCommand ToCommand(Guid tenantId, Guid createdByUserId) =>
        new(
            tenantId,
            createdByUserId,
            Kind ?? throw new InvalidOperationException("The draft has no customer kind."),
            FirstName,
            MiddleName,
            LastName,
            Prefix,
            Suffix,
            LegalName,
            BusinessStructure,
            Dba,
            FormationDate,
            PrincipalBusinessActivityId: null,
            DateOfBirth,
            OccupationId: null,
            PrimaryEmail ?? string.Empty,
            PrimaryPhone,
            Language ?? throw new InvalidOperationException("The draft has no language."),
            Overwrite: false,
            EmailEnabled,
            SmsEnabled
        );
}
