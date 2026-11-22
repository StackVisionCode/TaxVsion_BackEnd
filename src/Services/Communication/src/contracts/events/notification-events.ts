import type { IntegrationEvent } from './integration-event.js';

/**
 * Communication descartó un `notification.inapp.requested.v1` por payload inválido. Lo consume Notification
 * (.NET `InAppNotificationRejectedIntegrationEvent`) para marcar fallida su fila de auditoría.
 */
export const INAPP_NOTIFICATION_REJECTED_TYPE = 'communication.notification.inapp_rejected.v1';

export type InAppNotificationRejectionReason = 'InvalidPayload';

export interface InAppNotificationRejectedEvent extends IntegrationEvent {
  readonly eventType: typeof INAPP_NOTIFICATION_REJECTED_TYPE;
  readonly sourceEventId: string;
  readonly recipientUserId: string;
  readonly kind: string;
  readonly reason: InAppNotificationRejectionReason;
  /** Rutas de los campos inválidos separadas por coma; nunca los valores. */
  readonly detail: string;
}
