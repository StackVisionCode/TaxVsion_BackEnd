namespace TaxVision.DataCollection.Engine.Definitions;

public enum FormPurpose
{
    General,
    ClientIntake,
    Questionnaire,
    Checklist,
    InformationRequest,
    DocumentRequest,
    Assessment,
}

public enum FormPresentation
{
    SinglePage,
    Wizard,
}

public enum FormCollectionBasis
{
    ServiceRequest,
    Consent,
}

public enum DataClass
{
    General,
    Financial,
    Personal,
    Restricted,
}

public enum FormActionType
{
    NotifyStaff,
    RouteToReview,
    GenerateSummary,
    CreateCustomer,
    UpdateCustomerProfile,
    RequestSignature,
    ApplyRetentionClass,
}

public enum FormActionTrigger
{
    OnSubmit,
    OnComplete,
    Manual,
}

public enum MappingTransform
{
    None,
    Trim,
    Lowercase,
    Uppercase,
    DigitsOnly,
}
