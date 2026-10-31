import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';

/** F7 (T-34): de que es el transcript. Las notas de voz guardan el texto en el mensaje; aqui solo su rastro. */
export type TranscriptScope = 'Meeting' | 'Call' | 'VoiceNote';
export type TranscriptState = 'Processing' | 'Ready' | 'Failed';

export interface TranscriptArtifactSnapshot {
  readonly id: string;
  readonly tenantId: string;
  readonly scope: TranscriptScope;
  readonly scopeId: string;
  readonly recordingSessionId: string | null;
  readonly state: TranscriptState;
  readonly txtFileId: string | null;
  readonly jsonFileId: string | null;
  readonly language: string | null;
  readonly model: string | null;
  readonly vad: boolean;
  readonly durationSeconds: number | null;
  readonly wordCount: number | null;
  readonly segmentCount: number | null;
  readonly speakerCount: number | null;
  readonly realtimeFactor: number | null;
  readonly failureReason: string | null;
  readonly createdAtUtc: Date;
  readonly completedAtUtc: Date | null;
}

export interface TranscriptResult {
  readonly txtFileId: string | null;
  readonly jsonFileId: string | null;
  readonly language: string | null;
  readonly model: string | null;
  readonly vad: boolean;
  readonly durationSeconds: number;
  readonly wordCount: number;
  readonly segmentCount: number;
  readonly speakerCount: number;
  readonly realtimeFactor: number | null;
}

/**
 * Estado del transcript separado del de la grabacion (H-5): una grabacion Ready puede tener el
 * transcript Processing o Failed sin que eso la afecte. Uno por (Scope, ScopeId); un reproceso lo
 * vuelve a Processing.
 */
export class TranscriptArtifact {
  private constructor(private state: TranscriptArtifactSnapshot) {}

  static rehydrate(snapshot: TranscriptArtifactSnapshot): TranscriptArtifact {
    return new TranscriptArtifact(snapshot);
  }

  static start(input: {
    tenantId: string;
    scope: TranscriptScope;
    scopeId: string;
    recordingSessionId?: string | null;
    now?: Date;
  }): TranscriptArtifact {
    return new TranscriptArtifact({
      id: randomUUID(),
      tenantId: input.tenantId,
      scope: input.scope,
      scopeId: input.scopeId,
      recordingSessionId: input.recordingSessionId ?? null,
      state: 'Processing',
      txtFileId: null,
      jsonFileId: null,
      language: null,
      model: null,
      vad: false,
      durationSeconds: null,
      wordCount: null,
      segmentCount: null,
      speakerCount: null,
      realtimeFactor: null,
      failureReason: null,
      createdAtUtc: input.now ?? new Date(),
      completedAtUtc: null,
    });
  }

  /** Reproceso (recomponer): vuelve a Processing conservando el id. */
  restart(now: Date = new Date()): void {
    this.state = {
      ...this.state,
      state: 'Processing',
      failureReason: null,
      completedAtUtc: null,
      createdAtUtc: now,
    };
  }

  complete(result: TranscriptResult, now: Date = new Date()): Result<void> {
    if (this.state.state === 'Ready' && this.state.txtFileId === result.txtFileId) return Result.okVoid();
    if (result.durationSeconds < 0 || result.wordCount < 0) {
      return Result.fail(makeError('Transcript.InvalidResult', 'Transcript metrics cannot be negative.'));
    }
    this.state = {
      ...this.state,
      state: 'Ready',
      txtFileId: result.txtFileId,
      jsonFileId: result.jsonFileId,
      language: result.language,
      model: result.model,
      vad: result.vad,
      durationSeconds: Math.round(result.durationSeconds),
      wordCount: result.wordCount,
      segmentCount: result.segmentCount,
      speakerCount: result.speakerCount,
      realtimeFactor: result.realtimeFactor,
      failureReason: null,
      completedAtUtc: now,
    };
    return Result.okVoid();
  }

  fail(reason: string, now: Date = new Date()): void {
    if (this.state.state === 'Ready') return;
    this.state = { ...this.state, state: 'Failed', failureReason: reason.slice(0, 64), completedAtUtc: now };
  }

  toSnapshot(): TranscriptArtifactSnapshot {
    return this.state;
  }
}
