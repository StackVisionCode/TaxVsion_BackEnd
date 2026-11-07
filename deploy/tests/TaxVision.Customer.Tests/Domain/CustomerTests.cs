using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

public sealed class CustomerTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid U1 = Guid.NewGuid();
    private static readonly Guid U2 = Guid.NewGuid();
    private static readonly Guid U3 = Guid.NewGuid();

    private static readonly AddressValue Address = AddressValue.Create("1 Main St", "Miami", "33101", "US").Value;
    private static readonly PersonalName Name = PersonalName.Create("Ana", "Perez").Value;
    private static readonly byte[] Cipher = [1, 2, 3];

    /// <summary>Todas las mutaciones que exigen un cliente no archivado.</summary>
    public static TheoryData<string, Func<DomainCustomer, Result>> ArchivedGuards =>
        new()
        {
            { "ChangePrimaryEmail", c => c.ChangePrimaryEmail(EmailAddress.Create("new@example.com").Value, Actor) },
            { "ChangePrimaryPhone", c => c.ChangePrimaryPhone(null, Actor) },
            { "ChangeLanguage", c => c.ChangeLanguage(Language.Es, Actor) },
            { "ChangeContactPolicy", c => c.ChangeContactPolicy(true, false, Actor) },
            { "ChangePersonalName", c => c.ChangePersonalName(Name, Actor) },
            { "ChangeBusinessIdentity", c => c.ChangeBusinessIdentity(null!, Actor) },
            { "ChangeDateOfBirth", c => c.ChangeDateOfBirth(null, Actor) },
            { "AddAddress", c => c.AddAddress(AddressKind.Home, Address, true, Actor) },
            { "UpdateAddress", c => c.UpdateAddress(Guid.NewGuid(), AddressKind.Home, Address, true, Actor) },
            { "RemoveAddress", c => c.RemoveAddress(Guid.NewGuid(), Actor) },
            { "AddContactPoint", c => c.AddContactPoint(ContactPointType.Phone, "1", "1", null, false, Actor) },
            {
                "UpdateContactPoint",
                c => c.UpdateContactPoint(Guid.NewGuid(), ContactPointType.Phone, "1", "1", null, false, Actor)
            },
            { "RemoveContactPoint", c => c.RemoveContactPoint(Guid.NewGuid(), Actor) },
            {
                "AddRelation",
                c =>
                    c.AddRelation(
                        RelationshipKind.Child,
                        RelationPurpose.Dependent,
                        Name,
                        null,
                        null,
                        null,
                        null,
                        Actor
                    )
            },
            {
                "UpdateRelation",
                c =>
                    c.UpdateRelation(
                        Guid.NewGuid(),
                        RelationshipKind.Child,
                        RelationPurpose.Dependent,
                        Name,
                        null,
                        null,
                        null,
                        null,
                        Actor
                    )
            },
            { "RemoveRelation", c => c.RemoveRelation(Guid.NewGuid(), Actor) },
            {
                "SetFiscalProfile",
                c =>
                    c.SetFiscalProfile(
                        FiscalSubjectKind.Individual,
                        Cipher,
                        "index",
                        "1234",
                        null,
                        null,
                        false,
                        null,
                        null,
                        Actor
                    )
            },
            { "UpdateFiscalProfile", c => c.UpdateFiscalProfile(null, null, false, null, null, Actor) },
            { "ChangeOccupation", c => c.ChangeOccupation(null, Actor) },
            { "AssignPreparer", c => c.AssignPreparer(U1, Actor) },
            { "HandOverPreparer", c => c.HandOverPreparer(U1, U2, Actor) },
            { "UnassignPreparer", c => c.UnassignPreparer(Actor) },
            { "GrantAccess", c => c.GrantAccess(U1, Actor) },
            { "RevokeAccess", c => c.RevokeAccess(U1, Actor) },
        };

    [Fact]
    public void AssignPreparer_OnArchivedCustomer_ReturnsFailure()
    {
        var customer = NewCustomer();
        customer.Archive(Actor);

        var result = customer.AssignPreparer(U1, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.Archived", result.Error.Code);
        Assert.Null(customer.AssignedPreparerUserId);
    }

    [Theory]
    [MemberData(nameof(ArchivedGuards))]
    public void Mutation_OnArchivedCustomer_ReturnsArchivedFailure(string method, Func<DomainCustomer, Result> mutate)
    {
        var customer = NewCustomer();
        customer.Archive(Actor);
        customer.ClearDomainEvents();

        var result = mutate(customer);

        Assert.True(result.IsFailure, method);
        Assert.Equal("Customer.Archived", result.Error.Code);
        Assert.Empty(customer.DomainEvents);
    }

    [Fact]
    public void HandOverPreparer_FromUserNotAssigned_IsNoOp()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(U3, Actor);
        customer.ClearDomainEvents();

        var result = customer.HandOverPreparer(U1, U2, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(U3, customer.AssignedPreparerUserId);
        Assert.DoesNotContain(customer.Assignments, a => a.UserId == U2);
        Assert.Contains(customer.Assignments, a => a.UserId == U3 && a.IsPrimary);
        Assert.Empty(customer.DomainEvents);
    }

    [Fact]
    public void HandOverPreparer_FromUserNotPrimary_RemovesOnlyTheirRow()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(U3, Actor);
        customer.GrantAccess(U1, Actor);

        var result = customer.HandOverPreparer(U1, U2, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(U3, customer.AssignedPreparerUserId);
        Assert.Equal(U3, Assert.Single(customer.Assignments).UserId);
    }

    [Fact]
    public void HandOverPreparer_FromPrimary_MovesThePrimaryToTheSuccessor()
    {
        var customer = NewCustomer();
        customer.AssignPreparer(U1, Actor);

        var result = customer.HandOverPreparer(U1, U2, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(U2, customer.AssignedPreparerUserId);
        Assert.True(Assert.Single(customer.Assignments).IsPrimary);
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                Guid.NewGuid(),
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace").Value,
                null,
                EmailAddress.Create("ada@example.com").Value,
                null,
                Language.En,
                Actor
            )
            .Value;
}
