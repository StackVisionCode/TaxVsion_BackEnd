namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Motivo de una acción de la oficina sobre una credencial (03 §4.3). Se persiste como byte.</summary>
public enum CredentialAdministrationReasonCode : byte
{
    OfficePolicy = 1,
    SharedPassword = 2,
    SuspectedCompromise = 3,
    UserRequest = 4,
    Other = 5,
}
