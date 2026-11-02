namespace TaxVision.Signature.Domain.Delivery;

/// <summary>Para qué se entrega: el paquete final de la solicitud o la copia en curso tras firmar.</summary>
public enum DeliveryPurpose
{
    Completed = 0,
    PartialCopy = 1,
}

public enum DeliveryItemKind
{
    SignedDocument = 0,
    Certificate = 1,
    PartialCopy = 2,
}

public enum DeliveryStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
}
