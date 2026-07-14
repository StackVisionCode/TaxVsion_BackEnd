namespace BuildingBlocks.Domain;

/// <summary>
/// Hecho de dominio que ocurre dentro de un agregado y se procesa in-process — nunca
/// cruza la frontera del microservicio (para eso está IntegrationEvent, en
/// BuildingBlocks.Messaging). El agregado solo lo junta vía AggregateRoot.AddDomainEvent;
/// quien lo drena y publica es el DbContext, siempre antes de confirmar la transacción.
/// </summary>
public interface IDomainEvent;
