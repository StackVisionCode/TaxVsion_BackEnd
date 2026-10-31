import type { PrismaClient, TranscriptArtifact as PrismaTranscriptArtifact } from '@prisma/client';
import {
  TranscriptArtifact,
  type TranscriptScope,
  type TranscriptState,
} from '../../domain/recording/transcript-artifact.js';
import type { TranscriptArtifactRepository } from '../../application/ports/transcript-artifact-repository.js';

const SCOPES: readonly TranscriptScope[] = ['Meeting', 'Call', 'VoiceNote'];
const STATES: readonly TranscriptState[] = ['Processing', 'Ready', 'Failed'];

export class PrismaTranscriptArtifactRepository implements TranscriptArtifactRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(artifact: TranscriptArtifact): Promise<void> {
    const s = artifact.toSnapshot();
    const mutable = {
      RecordingSessionId: s.recordingSessionId,
      State: s.state,
      TxtFileId: s.txtFileId,
      JsonFileId: s.jsonFileId,
      Language: s.language,
      Model: s.model,
      Vad: s.vad,
      DurationSeconds: s.durationSeconds,
      WordCount: s.wordCount,
      SegmentCount: s.segmentCount,
      SpeakerCount: s.speakerCount,
      RealtimeFactor: s.realtimeFactor,
      FailureReason: s.failureReason,
      CreatedAtUtc: s.createdAtUtc,
      CompletedAtUtc: s.completedAtUtc,
    };
    await this.prisma.transcriptArtifact.upsert({
      where: { Scope_ScopeId: { Scope: s.scope, ScopeId: s.scopeId } },
      create: { Id: s.id, TenantId: s.tenantId, Scope: s.scope, ScopeId: s.scopeId, ...mutable },
      update: mutable,
    });
  }

  async findByScope(tenantId: string, scope: TranscriptScope, scopeId: string): Promise<TranscriptArtifact | null> {
    const row = await this.prisma.transcriptArtifact.findFirst({ where: { TenantId: tenantId, Scope: scope, ScopeId: scopeId } });
    return row ? toDomain(row) : null;
  }
}

function toDomain(row: PrismaTranscriptArtifact): TranscriptArtifact {
  if (!SCOPES.includes(row.Scope as TranscriptScope)) throw new Error(`Corrupted transcript scope '${row.Scope}'`);
  if (!STATES.includes(row.State as TranscriptState)) throw new Error(`Corrupted transcript state '${row.State}'`);
  return TranscriptArtifact.rehydrate({
    id: row.Id,
    tenantId: row.TenantId,
    scope: row.Scope as TranscriptScope,
    scopeId: row.ScopeId,
    recordingSessionId: row.RecordingSessionId,
    state: row.State as TranscriptState,
    txtFileId: row.TxtFileId,
    jsonFileId: row.JsonFileId,
    language: row.Language,
    model: row.Model,
    vad: row.Vad,
    durationSeconds: row.DurationSeconds,
    wordCount: row.WordCount,
    segmentCount: row.SegmentCount,
    speakerCount: row.SpeakerCount,
    realtimeFactor: row.RealtimeFactor,
    failureReason: row.FailureReason,
    createdAtUtc: row.CreatedAtUtc,
    completedAtUtc: row.CompletedAtUtc,
  });
}
