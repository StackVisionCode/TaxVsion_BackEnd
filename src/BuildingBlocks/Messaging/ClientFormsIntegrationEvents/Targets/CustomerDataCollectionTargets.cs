namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

public static class CustomerDataCollectionTargets
{
    public const int CatalogVersion = 1;
    private const string CustomerDomain = "Customer";
    private const string Individual = "Individual";
    private const string Business = "Business";
    private const string E164Pattern = @"^\+[1-9]\d{6,14}$";

    private static readonly DataCollectionOperation[] CreateAndUpdate =
    [
        DataCollectionOperation.CreateCustomer,
        DataCollectionOperation.UpdateCustomerProfile,
    ];

    private static readonly DataCollectionOperation[] UpdateOnly = [DataCollectionOperation.UpdateCustomerProfile];
    private static readonly string[] CustomerManage = ["customers.manage"];
    private const string KindTarget = "customer.kind";
    private const string LanguageTarget = "customer.language";

    public static IReadOnlyList<DataCollectionOperationDescriptor> Operations { get; } =
    [
        new(
            DataCollectionOperation.CreateCustomer,
            CustomerManage,
            RequiresTenantAdmin: false,
            DiscriminatorTargetKey: KindTarget,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = KindTarget,
                ["language"] = LanguageTarget,
            }
        ),
        new(
            DataCollectionOperation.UpdateCustomerProfile,
            CustomerManage,
            RequiresTenantAdmin: false,
            DiscriminatorTargetKey: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
        ),
    ];

    private static readonly DataCollectionTargetField[] DependentFields =
    [
        new("first_name", DataCollectionTargetType.String, 80, Required: true),
        new("middle_name", DataCollectionTargetType.String, 80),
        new("last_name", DataCollectionTargetType.String, 80, Required: true),
        new("suffix", DataCollectionTargetType.String, 20),
        new("date_of_birth", DataCollectionTargetType.Date),
    ];

    public static IReadOnlyList<DataCollectionTarget> All { get; } =
    [
        Target(
            KindTarget,
            "Customer",
            "Customer kind",
            DataCollectionTargetType.Enum,
            enums: [Individual, Business],
            requiredFor: [Required(DataCollectionOperation.CreateCustomer)],
            sensitivity: DataCollectionSensitivity.General
        ),
        Target(
            "customer.person.first_name",
            "Customer",
            "First name",
            DataCollectionTargetType.String,
            maxLength: 80,
            requiredFor: [Required(DataCollectionOperation.CreateCustomer, Individual)]
        ),
        Target(
            "customer.person.last_name",
            "Customer",
            "Last name",
            DataCollectionTargetType.String,
            maxLength: 80,
            requiredFor: [Required(DataCollectionOperation.CreateCustomer, Individual)]
        ),
        Target(
            "customer.person.middle_name",
            "Customer",
            "Middle name",
            DataCollectionTargetType.String,
            maxLength: 80
        ),
        Target("customer.person.prefix", "Customer", "Name prefix", DataCollectionTargetType.String, maxLength: 20),
        Target("customer.person.suffix", "Customer", "Name suffix", DataCollectionTargetType.String, maxLength: 20),
        Target(
            "customer.business.legal_name",
            "Customer",
            "Legal business name",
            DataCollectionTargetType.String,
            maxLength: 200,
            requiredFor: [Required(DataCollectionOperation.CreateCustomer, Business)]
        ),
        Target(
            "customer.business.structure",
            "Customer",
            "Business structure",
            DataCollectionTargetType.Enum,
            enums: ["SoleProprietorship", "Partnership", "Llc", "SCorp", "CCorp", "NonProfit", "Other"],
            requiredFor: [Required(DataCollectionOperation.CreateCustomer, Business)],
            sensitivity: DataCollectionSensitivity.General
        ),
        Target(
            "customer.business.dba",
            "Customer",
            "Doing business as",
            DataCollectionTargetType.String,
            maxLength: 200
        ),
        Target(
            "customer.business.formation_date",
            "Customer",
            "Business formation date",
            DataCollectionTargetType.Date,
            pastOnly: true
        ),
        Target("customer.date_of_birth", "Customer", "Date of birth", DataCollectionTargetType.Date),
        Target(
            "customer.primary_email",
            "Customer",
            "Primary email",
            DataCollectionTargetType.Email,
            maxLength: 254,
            requiredFor: [Required(DataCollectionOperation.CreateCustomer)]
        ),
        Target(
            "customer.primary_phone",
            "Customer",
            "Primary phone",
            DataCollectionTargetType.Phone,
            pattern: E164Pattern
        ),
        Target(
            LanguageTarget,
            "Customer",
            "Preferred language",
            DataCollectionTargetType.Enum,
            enums: ["Es", "En", "Pt", "Fr"],
            requiredFor: [Required(DataCollectionOperation.CreateCustomer)],
            sensitivity: DataCollectionSensitivity.General
        ),
        Target(
            "customer.contact.email_enabled",
            "Customer",
            "Email enabled",
            DataCollectionTargetType.Boolean,
            sensitivity: DataCollectionSensitivity.General
        ),
        Target(
            "customer.contact.sms_enabled",
            "Customer",
            "SMS enabled",
            DataCollectionTargetType.Boolean,
            sensitivity: DataCollectionSensitivity.General
        ),
        Target("customer.address.home", "Address", "Home address", DataCollectionTargetType.Address),
        Target("customer.address.mailing", "Address", "Mailing address", DataCollectionTargetType.Address),
        Target(
            "customer.relation.dependents",
            "Relation",
            "Dependents",
            DataCollectionTargetType.Collection,
            operations: UpdateOnly,
            fields: DependentFields
        ),
        Target(
            "customer.fiscal.filing_status",
            "FiscalProfile",
            "Filing status",
            DataCollectionTargetType.Enum,
            enums: ["Single", "MarriedJoint", "MarriedSeparate", "HeadOfHousehold", "QualifyingSurvivingSpouse"],
            operations: UpdateOnly,
            requiresTenantAdmin: true
        ),
        Target(
            "customer.fiscal.tax_identifier",
            "FiscalProfile",
            "Tax identifier",
            DataCollectionTargetType.TaxId,
            operations: UpdateOnly,
            sensitivity: DataCollectionSensitivity.Restricted,
            requiresTenantAdmin: true
        ),
    ];

    private static DataCollectionTargetRequirement Required(DataCollectionOperation operation, string? kind = null) =>
        new(operation, kind);

    private static DataCollectionTarget Target(
        string key,
        string entity,
        string displayName,
        DataCollectionTargetType dataType,
        int? maxLength = null,
        IReadOnlyList<string>? enums = null,
        bool pastOnly = false,
        string? pattern = null,
        IReadOnlyList<DataCollectionOperation>? operations = null,
        IReadOnlyList<DataCollectionTargetRequirement>? requiredFor = null,
        DataCollectionSensitivity sensitivity = DataCollectionSensitivity.Personal,
        bool requiresTenantAdmin = false,
        IReadOnlyList<DataCollectionTargetField>? fields = null
    ) =>
        new(
            key,
            CustomerDomain,
            entity,
            displayName,
            dataType,
            new DataCollectionTargetConstraints(maxLength, enums, pastOnly, pattern),
            sensitivity,
            operations ?? CreateAndUpdate,
            requiredFor ?? [],
            CustomerManage,
            requiresTenantAdmin,
            DataCollectionTargetStatus.Active,
            TargetSchemaVersion: 1,
            IntroducedIn: CatalogVersion,
            Fields: fields
        );
}
