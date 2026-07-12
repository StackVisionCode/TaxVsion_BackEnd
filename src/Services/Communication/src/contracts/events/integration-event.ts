/**
 * Contrato base de un integration event publicado al exchange taxvision-events.
 * Mismo shape que BuildingBlocks.Messaging.IIntegrationEvent (.NET) — permite
 * interoperabilidad Node ↔ .NET sin traducciones.
 */
export interface IntegrationEvent {
  readonly eventId: string;
  readonly eventType: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly occurredOnUtc: string; // ISO-8601
}
