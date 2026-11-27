namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Quién originó el link de reset: el propio usuario (forgot), la oficina o un incidente.</summary>
public enum ResetOrigin : byte
{
    SelfService = 0,
    Administrative = 1,
    Incident = 2,
}
