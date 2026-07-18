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

/**
 * Respuesta de un participante a una solicitud de consentimiento de grabación.
 * Compartido entre Meeting y Call — ver Fase Backend 2 (dominio) y Fase Backend
 * 3/4 (uso real). No respondida a tiempo se resuelve por policy (ver
 * RecordingConsentPolicy en TenantCommunicationSettings), nunca queda "en blanco"
 * en el snapshot final adjunto a RecordingStarted/RecordingReady.
 */
export type RecordingConsentResponse = 'Accepted' | 'Rejected';

/**
 * Entrada individual del snapshot de consentimiento adjunto a los eventos
 * RecordingStarted/RecordingReady — permite a Compliance/Audit reconstruir
 * quien acepto/rechazo sin tener que consultar RecordingConsentEvent aparte.
 */
export interface RecordingConsentSnapshotEntry {
  readonly userId: string;
  readonly response: RecordingConsentResponse;
  readonly respondedAtUtc: string;
}
