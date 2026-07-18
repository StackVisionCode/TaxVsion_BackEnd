namespace TaxVision.Scribe.Domain;

/// <summary>Estado de una EmailTemplateVersion/EmailLayoutVersion individual. Solo una Published a la vez por template/layout.</summary>
public enum EmailVersionStatus
{
    Draft,
    Published,
    Archived,
}
