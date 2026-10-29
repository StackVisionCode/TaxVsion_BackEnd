using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.DataCollection.Engine.Tests;

internal static class MappingTestCatalog
{
    internal const string Create = "CreateCustomer";
    internal const string Update = "UpdateCustomerProfile";
    internal static readonly string[] Manage = ["customers.manage"];

    internal static readonly PublisherCapabilities Staff = new(
        new HashSet<string>(Manage, StringComparer.Ordinal),
        false
    );
    internal static readonly PublisherCapabilities Admin = new(
        new HashSet<string>(Manage, StringComparer.Ordinal),
        true
    );

    internal static MappingCatalogSnapshot Snapshot(params MappingTarget[] overrides)
    {
        var targets = Targets()
            .Select(target => overrides.FirstOrDefault(item => item.TargetKey == target.TargetKey) ?? target);
        return new MappingCatalogSnapshot(1, targets.ToArray(), Operations());
    }

    internal static MappingTarget Target(string key) => Targets().Single(target => target.TargetKey == key);

    private static MappingTarget[] Targets() =>
        [
            Make(
                "customer.kind",
                MappingTargetType.Enum,
                enums: ["Individual", "Business"],
                requiredFor: [new(Create)]
            ),
            Make("customer.person.first_name", MappingTargetType.String, 80, requiredFor: [new(Create, "Individual")]),
            Make("customer.person.last_name", MappingTargetType.String, 80, requiredFor: [new(Create, "Individual")]),
            Make("customer.person.middle_name", MappingTargetType.String, 80),
            Make("customer.business.legal_name", MappingTargetType.String, 200, requiredFor: [new(Create, "Business")]),
            Make("customer.primary_email", MappingTargetType.Email, 254, requiredFor: [new(Create)]),
            Make("customer.language", MappingTargetType.Enum, enums: ["Es", "En"], requiredFor: [new(Create)]),
            Make("customer.contact.email_enabled", MappingTargetType.Boolean),
            Make(
                "customer.relation.dependents",
                MappingTargetType.Collection,
                operations: [Update],
                fields:
                [
                    new("first_name", MappingTargetType.String, 80, true),
                    new("last_name", MappingTargetType.String, 80, true),
                    new("relationship", MappingTargetType.Enum),
                ]
            ),
        ];

    private static MappingOperation[] Operations() =>
        [
            new(
                Create,
                Manage,
                false,
                "customer.kind",
                new Dictionary<string, string> { ["kind"] = "customer.kind", ["language"] = "customer.language" }
            ),
            new(Update, Manage, false, null, new Dictionary<string, string>()),
        ];

    internal static MappingTarget Make(
        string key,
        MappingTargetType type,
        int? maxLength = null,
        string[]? enums = null,
        MappingRequirement[]? requiredFor = null,
        string[]? operations = null,
        MappingTargetField[]? fields = null,
        MappingTargetStatus status = MappingTargetStatus.Active
    ) =>
        new(
            key,
            "Customer",
            "Customer",
            key,
            type,
            MappingSensitivity.Personal,
            status,
            operations ?? [Create, Update],
            requiredFor ?? [],
            Manage,
            false,
            maxLength,
            enums,
            false,
            fields
        );
}
