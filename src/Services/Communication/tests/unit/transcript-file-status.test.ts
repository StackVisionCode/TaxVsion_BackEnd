import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { TranscriptArtifact } from '../../src/domain/recording/transcript-artifact.js';
import type { TranscriptArtifactRepository } from '../../src/application/ports/transcript-artifact-repository.js';
import type { CloudStorageMetadataClient } from '../../src/application/ports/cloudstorage-metadata-client.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import { markTranscriptFileAvailable, releaseRejectedTranscriptFile } from '../../src/application/use-cases/transcript-file-status.js';
import { runRecordingSweep, type RecordingSweepDeps } from '../../src/application/use-cases/recording-sweep.js';
import { bindCloudStorageConsumers } from '../../src/application/event-handlers/cloudstorage-consumers.js';
import { InMemoryMeetingRepository, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

const u = (): string => randomUUID();

class InMemoryTranscripts implements TranscriptArtifactRepository {
  readonly items = new Map<string, TranscriptArtifact>();
  async save(artifact: TranscriptArtifact): Promise<void> {
    this.items.set(artifact.toSnapshot().id, artifact);
  }
  async findByScope(): Promise<TranscriptArtifact | null> {
    return null;
  }
  async findByRecordingSession(): Promise<TranscriptArtifact | null> {
    return null;
  }
  async findByFileId(tenantId: string, fileId: string): Promise<TranscriptArtifact | null> {
    return (
      [...this.items.values()].find((a) => {
        const s = a.toSnapshot();
        return s.tenantId === tenantId && (s.txtFileId === fileId || s.jsonFileId === fileId);
      }) ?? null
    );
  }
  async listReadyUnconfirmed(completedBefore: Date, take: number): Promise<TranscriptArtifact[]> {
    return [...this.items.values()]
      .filter((a) => {
        const s = a.toSnapshot();
        return s.state === 'Ready' && !s.filesConfirmedAtUtc && s.txtFileId && s.completedAtUtc && s.completedAtUtc < completedBefore;
      })
      .slice(0, take);
  }
}

const noCalls = { findById: async () => null, save: async () => undefined } as unknown as CallRepository;

/** Reunion terminada con su grabacion transcrita: la reunion apunta al .txt. */
async function meetingWithTranscript(completedAgoMs = 3 * 3_600_000) {
  const host = { userId: u(), displayName: 'Ana Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Revision', host });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  const txtFileId = u();
  const jsonFileId = u();
  meeting.attachTranscript(txtFileId);
  const artifact = TranscriptArtifact.start({ tenantId: meeting.tenantId, scope: 'Meeting', scopeId: meeting.id, recordingSessionId: u() });
  artifact.complete(
    { txtFileId, jsonFileId, language: 'es', model: 'm', vad: true, durationSeconds: 10, wordCount: 0, segmentCount: 0, speakerCount: 0, realtimeFactor: null },
    new Date(Date.now() - completedAgoMs),
  );
  const meetings = new InMemoryMeetingRepository();
  const transcripts = new InMemoryTranscripts();
  await meetings.save(meeting);
  await transcripts.save(artifact);
  const deps = { transcripts, meetings: meetings as never, calls: noCalls };
  return { meeting, artifact, txtFileId, jsonFileId, meetings, transcripts, deps };
}

describe('MNT-01 (E-292): archivo del transcript rechazado o confirmado por CloudStorage', () => {
  it('sin el .txt el transcript queda Failed y la reunión deja de apuntarlo', async () => {
    const { meeting, artifact, txtFileId, deps } = await meetingWithTranscript();

    const released = await releaseRejectedTranscriptFile({ tenantId: meeting.tenantId, fileId: txtFileId, reason: 'EmptyFile' }, deps);

    expect(released).toBe(true);
    expect(artifact.toSnapshot()).toMatchObject({ state: 'Failed', txtFileId: null, failureReason: 'StorageRejected:EmptyFile' });
    expect(meeting.toSnapshot().transcriptFileId).toBeNull();
  });

  it('sin el .json solo se suelta ese id: el transcript sigue Ready y la reunión lo conserva', async () => {
    const { meeting, artifact, txtFileId, jsonFileId, deps } = await meetingWithTranscript();

    await releaseRejectedTranscriptFile({ tenantId: meeting.tenantId, fileId: jsonFileId, reason: 'UploadPolicy' }, deps);

    expect(artifact.toSnapshot()).toMatchObject({ state: 'Ready', txtFileId, jsonFileId: null });
    expect(meeting.toSnapshot().transcriptFileId).toBe(txtFileId);
  });

  it('la confirmación del .txt lo saca del barrido', async () => {
    const { meeting, artifact, txtFileId, deps } = await meetingWithTranscript();
    await markTranscriptFileAvailable({ tenantId: meeting.tenantId, fileId: txtFileId }, deps);
    expect(artifact.toSnapshot().filesConfirmedAtUtc).not.toBeNull();
  });
});

describe('MNT-01 (E-292): barrido de transcripts sin confirmar', () => {
  function sweepDeps(ctx: Awaited<ReturnType<typeof meetingWithTranscript>>, status: string | null): RecordingSweepDeps {
    const metadata = { getMetadata: async () => (status ? { status } : null) } as unknown as CloudStorageMetadataClient;
    return {
      meetings: ctx.meetings as never,
      calls: noCalls,
      recordingSessions: new InMemoryRecordingSessionRepository(),
      publisher: new RecordingPublisher(),
      effectiveSettings: new FakeEffectiveSettingsResolver(),
      transcripts: ctx.transcripts,
      cloudStorageMetadata: metadata,
    } as unknown as RecordingSweepDeps;
  }

  it('sana el puntero a un .txt que CloudStorage nunca registró (dato anterior al arreglo)', async () => {
    const ctx = await meetingWithTranscript();

    const report = await runRecordingSweep(new Date(), sweepDeps(ctx, null));

    expect(report.transcriptsReleased).toBe(1);
    expect(ctx.artifact.toSnapshot().failureReason).toBe('StorageRejected:NotRegistered');
    expect(ctx.meeting.toSnapshot().transcriptFileId).toBeNull();
  });

  it('un .txt disponible se confirma una vez y no se vuelve a consultar', async () => {
    const ctx = await meetingWithTranscript();
    const deps = sweepDeps(ctx, 'Available');

    const first = await runRecordingSweep(new Date(), deps);
    const second = await runRecordingSweep(new Date(), deps);

    expect(first.transcriptsConfirmed).toBe(1);
    expect(second.transcriptsConfirmed).toBe(0);
    expect(ctx.meeting.toSnapshot().transcriptFileId).toBe(ctx.txtFileId);
  });

  it('recién terminado y todavía sin registro: espera el plazo', async () => {
    const ctx = await meetingWithTranscript(2 * 60_000);

    const report = await runRecordingSweep(new Date(), sweepDeps(ctx, null));

    expect(report.transcriptsReleased).toBe(0);
    expect(ctx.artifact.toSnapshot().state).toBe('Ready');
  });
});

describe('MNT-01 (E-292): cloudstorage.file.save_rejected.v1', () => {
  function bind() {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const calls: string[] = [];
    bindCloudStorageConsumers((type, handler) => handlers.set(type, handler), {
      attachmentTracking: { findByFileId: async () => null } as never,
      notifications: {} as never,
      emitter: {} as never,
      inAppSourcePolicy: 'Notification',
      recordingFiles: {
        onAvailable: async () => undefined,
        onRejected: async (_t, fileId, reason) => void calls.push(`recording:${fileId}:${reason}`),
      },
      transcriptFiles: {
        onAvailable: async (_t, fileId) => void calls.push(`transcript-ok:${fileId}`),
        onRejected: async (_t, fileId, reason) => void calls.push(`transcript:${fileId}:${reason}`),
      },
    });
    const send = (type: string, payload: Record<string, unknown>) =>
      handlers.get(type)!({ eventType: type, tenantId: 't', payload } as unknown as IncomingEnvelope);
    return { send, calls };
  }

  it('transcript o grabación sueltan el archivo; las pistas se ignoran', async () => {
    const { send, calls } = bind();
    await send('cloudstorage.file.save_rejected.v1', { FileId: 'f1', FolderType: 'Transcripts', Reason: 'EmptyFile' });
    await send('cloudstorage.file.save_rejected.v1', { FileId: 'f2', FolderType: 'Recordings', Reason: 'StorageQuota.Exceeded' });
    await send('cloudstorage.file.save_rejected.v1', { FileId: 'f3', FolderType: 'RecordingTracks', Reason: 'UploadPolicy' });
    await send('cloudstorage.file.available.v1', { FileId: 'f4', FolderType: 'Transcripts' });

    expect(calls).toEqual(['transcript:f1:EmptyFile', 'recording:f2:StorageRejected', 'transcript-ok:f4']);
  });
});
