/**
 * Estado de una entrada individual del log append-only de consentimiento
 * (RecordingConsentEvent en Prisma). Incluye 'Requested' a diferencia del
 * RecordingConsentResponse de contracts/events/integration-event.ts (que
 * solo modela 'Accepted'|'Rejected' — la respuesta final que viaja en el
 * consentSnapshot de los eventos de integracion). Aqui 'Requested' es la
 * fila que se inserta al pedir consentimiento, antes de que el participante
 * responda — permite auditar cuanto tardo alguien en responder, o si nunca
 * respondio (fila 'Requested' sin fila 'Accepted'/'Rejected' posterior).
 */
export const RecordingConsentEntryStatus = {
  Requested: 'Requested',
  Accepted: 'Accepted',
  Rejected: 'Rejected',
} as const;
export type RecordingConsentEntryStatus =
  (typeof RecordingConsentEntryStatus)[keyof typeof RecordingConsentEntryStatus];

export function isRecordingConsentEntryStatus(value: string): value is RecordingConsentEntryStatus {
  return value === 'Requested' || value === 'Accepted' || value === 'Rejected';
}

/**
 * Politica que determina si una RecordingSession puede transicionar a
 * Recording dado el conjunto de respuestas recibidas hasta el momento.
 * Configurable por tenant via TenantCommunicationSettings.RecordingConsentPolicy
 * (default 'NoRejections').
 *
 *  - AllAcceptedRequired: todos los participantes (menos quien pidio grabar)
 *    deben responder 'Accepted' explicitamente. Usado por Calls (Fase Backend 4,
 *    solo 2 partes — ninguna ambiguedad de "no respondio").
 *  - NoRejections: quien no responde se trata como aceptado por default; un
 *    'Rejected' explicito bloquea. Default para Meetings.
 *  - HostOverride: el host puede forzar el inicio sin importar las respuestas
 *    (para escenarios donde el tenant confia en el juicio del preparer).
 */
export const RecordingConsentPolicy = {
  AllAcceptedRequired: 'AllAcceptedRequired',
  NoRejections: 'NoRejections',
  HostOverride: 'HostOverride',
} as const;
export type RecordingConsentPolicy = (typeof RecordingConsentPolicy)[keyof typeof RecordingConsentPolicy];

export function isRecordingConsentPolicy(value: string): value is RecordingConsentPolicy {
  return value === 'AllAcceptedRequired' || value === 'NoRejections' || value === 'HostOverride';
}

export interface RecordingConsentTerminalResponse {
  readonly userId: string;
  readonly response: 'Accepted' | 'Rejected';
}

/**
 * Evalua si, dado el estado actual de respuestas, la politica permite
 * arrancar la grabacion. Pura — no toca RecordingSession ni persistencia;
 * el use case (Fase Backend 3/4) es quien junta `consentEntries` (leyendo
 * RecordingConsentRepository) y `participantUserIds` (leyendo Meeting/Call)
 * antes de llamar esto, y pasa el boolean resultante a
 * Meeting.startRecording/Call.startRecording como `policyDecision`.
 */
export function evaluateRecordingConsentPolicy(input: {
  readonly policy: RecordingConsentPolicy;
  readonly participantUserIds: readonly string[];
  readonly requestedByUserId: string;
  /** Solo respuestas terminales (Accepted/Rejected) — no incluir filas 'Requested'. */
  readonly consentEntries: readonly RecordingConsentTerminalResponse[];
}): boolean {
  if (input.policy === RecordingConsentPolicy.HostOverride) {
    return true;
  }
  const responseByUser = new Map(input.consentEntries.map((entry) => [entry.userId, entry.response]));
  const otherParticipants = input.participantUserIds.filter((userId) => userId !== input.requestedByUserId);

  if (input.policy === RecordingConsentPolicy.NoRejections) {
    return !otherParticipants.some((userId) => responseByUser.get(userId) === 'Rejected');
  }

  // AllAcceptedRequired
  return otherParticipants.every((userId) => responseByUser.get(userId) === 'Accepted');
}
