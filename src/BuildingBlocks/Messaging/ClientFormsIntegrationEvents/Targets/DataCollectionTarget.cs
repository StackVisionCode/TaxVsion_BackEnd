namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

public enum DataCollectionTargetType
{
    String,
    Date,
    Email,
    Phone,
    Enum,
    Boolean,
    Address,
    TaxId,
    Collection,
}

public enum DataCollectionSensitivity
{
    General,
    Personal,
    Restricted,
}

public enum DataCollectionOperation
{
    CreateCustomer,
    UpdateCustomerProfile,
}

public enum DataCollectionTargetStatus
{
    Active,
    Deprecated,
    Retired,
}

public sealed record DataCollectionTargetConstraints(
    int? MaxLength = null,
    IReadOnlyList<string>? EnumValues = null,
    bool PastOnly = false,
    string? Pattern = null
);

public sealed record DataCollectionTargetRequirement(DataCollectionOperation Operation, string? Kind = null);

public sealed record DataCollectionTarget(
    string TargetKey,
    string Domain,
    string Entity,
    string DisplayName,
    DataCollectionTargetType DataType,
    DataCollectionTargetConstraints Constraints,
    DataCollectionSensitivity Sensitivity,
    IReadOnlyList<DataCollectionOperation> Operations,
    IReadOnlyList<DataCollectionTargetRequirement> RequiredFor,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresTenantAdmin,
    DataCollectionTargetStatus Status,
    int TargetSchemaVersion,
    int IntroducedIn,
    int? DeprecatedIn = null
);
