import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import { RecordingScope } from './recording-session-state.js';
import { RecordingConsentEntryStatus, isRecordingConsentEntryStatus } from './recording-consent.js';

/**
 * Fila append-only del log de consentimiento de grabacion (RecordingConsentEvent
 * en Prisma). Nunca se actualiza ni se borra una vez creada — cada cambio de
 * respuesta de un usuario es una fila NUEVA (permite reconstruir el historial
 * completo: "pidio a las 10:00, rechazo a las 10:01, acepto a las 10:02").
 */
export interface RecordingConsentEntrySnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly scope: RecordingScope;
  readonly scopeId: string;
  readonly userId: string;
  readonly response: RecordingConsentEntryStatus;
  readonly respondedAtUtc: Date;
  readonly recordedAtUtc: Date;
}

export class RecordingConsentEntry {
  private constructor(private readonly state: RecordingConsentEntrySnapshot) {}

  static rehydrate(snapshot: RecordingConsentEntrySnapshot): RecordingConsentEntry {
    return new RecordingConsentEntry(snapshot);
  }

  static record(input: {
    tenantId: string;
    scope: RecordingScope;
    scopeId: string;
    userId: string;
    response: RecordingConsentEntryStatus;
    respondedAtUtc: Date;
    now?: Date;
  }): Result<RecordingConsentEntry> {
    if (!isRecordingConsentEntryStatus(input.response)) {
      return Result.fail(
        makeError('RecordingConsent.InvalidResponse', `Invalid consent response: ${input.response}.`),
      );
    }
    const snapshot: RecordingConsentEntrySnapshot = {
      id: randomUUID(),
      tenantId: input.tenantId,
      scope: input.scope,
      scopeId: input.scopeId,
      userId: input.userId,
      response: input.response,
      respondedAtUtc: input.respondedAtUtc,
      recordedAtUtc: input.now ?? new Date(),
    };
    return Result.ok(new RecordingConsentEntry(snapshot));
  }

  toSnapshot(): RecordingConsentEntrySnapshot {
    return this.state;
  }

  get userId(): string {
    return this.state.userId;
  }
  get response(): RecordingConsentEntryStatus {
    return this.state.response;
  }
}
