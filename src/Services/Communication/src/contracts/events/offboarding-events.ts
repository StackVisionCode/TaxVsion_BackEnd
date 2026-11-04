import type { IntegrationEvent } from './integration-event.js';

/**
 * Acuse de un paso del retiro (offboard). Mismo contrato que el .NET
 * `UserOffboardingStepCompletedIntegrationEvent` (`[MessageIdentity]` = este alias), que Auth registra en el
 * avance del retiro (idempotente por service+step). `service` = carpeta de src/Services; `step` = el
 * consumer (acá, el archivo sin extensión), igual que el registro de pasos esperados de Auth.
 */
export const OFFBOARDING_STEP_COMPLETED_TYPE = 'auth.user.offboarding_step_completed.v1';

/** Aviso de Auth (retiro vencido sin todos los acuses): lo consume Notification, no Communication. */
export const OFFBOARDING_INCOMPLETE_CLR_TYPE =
  'BuildingBlocks.Messaging.AuthIntegrationEvents.UserOffboardingIncompleteIntegrationEvent';

export const COMMUNICATION_OFFBOARDING_SERVICE = 'Communication';
export const COMMUNICATION_OFFBOARDING_STEP = 'offboarding-consumers';

export interface UserOffboardingStepCompletedEvent extends IntegrationEvent {
  readonly eventType: typeof OFFBOARDING_STEP_COMPLETED_TYPE;
  readonly userId: string;
  readonly service: string;
  readonly step: string;
  readonly completedAtUtc: string;
}
