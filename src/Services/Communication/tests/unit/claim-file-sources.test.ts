import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { bindCloudStorageConsumers } from '../../src/application/event-handlers/cloudstorage-consumers.js';
import { redeclareFileSources } from '../../src/application/use-cases/redeclare-file-sources.js';
import type { AttachmentTrackingRepository } from '../../src/application/ports/attachment-tracking-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { FileSourceDeclaredEvent } from '../../src/contracts/events/cloudstorage-events.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import { FakeFileLibraryReadRepository } from '../helpers/chat-file-library.js';

const u = () => randomUUID();
const CLAIM = 'cloudstorage.file_sources_claim_requested.v1';

/** Oficina con chat (adjunto y nota de voz), grabaciones y transcripts; más una oficina ajena. */
function seeded() {
  const tenantId = u();
  const otherTenant = u();
  const repo = new FakeFileLibraryReadRepository();
  const [conversationId, call, meeting, customerId] = [u(), u(), u(), u()];
  const files = {
    attachment: u(),
    voiceNote: u(),
    callRec: u(),
    meetingRec: u(),
    txt: u(),
    json: u(),
    foreignTenant: u(),
  };
  repo.redeclare.push(
    { tenantId, fileId: files.attachment, conversationId, isVoiceNote: false },
    { tenantId, fileId: files.voiceNote, conversationId, isVoiceNote: true },
    { tenantId: otherTenant, fileId: files.foreignTenant, conversationId: u(), isVoiceNote: false },
  );
  repo.clients.push({ tenantId, conversationId, actorType: 'CustomerPortal', customerId, isRemoved: false });
  repo.recordingRedeclare.push(
    { tenantId, id: u(), scope: 'Call', scopeId: call, fileId: files.callRec },
    { tenantId, id: u(), scope: 'Meeting', scopeId: meeting, fileId: files.meetingRec },
  );
  repo.transcriptRedeclare.push({
    tenantId,
    id: u(),
    scope: 'Call',
    scopeId: call,
    txtFileId: files.txt,
    jsonFileId: files.json,
  });
  repo.recordingClients.push({
    tenantId,
    scope: 'Call',
    scopeId: call,
    actorType: 'CustomerPortal',
    customerId,
  });

  const published: IntegrationEvent[] = [];
  const publisher = { enqueue: async (e: IntegrationEvent) => void published.push(e) };
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  bindCloudStorageConsumers((type, handler) => handlers.set(type, handler), {
    attachmentTracking: {} as AttachmentTrackingRepository,
    notifications: {} as NotificationRepository,
    emitter: {} as RealtimeEmitter,
    inAppSourcePolicy: 'Notification',
    fileSourceClaims: { fileLibrary: repo, publisher },
  });
  const claim = async (payload: Record<string, unknown>, tenant = tenantId) => {
    const handler = handlers.get(CLAIM);
    if (!handler) throw new Error(`${CLAIM} no está registrado`);
    await handler({
      eventId: u(),
      eventType: CLAIM,
      tenantId: tenant,
      correlationId: 'corr-1',
      occurredOnUtc: new Date().toISOString(),
      payload: { tenantId: tenant, ...payload },
    });
  };
  return { tenantId, otherTenant, repo, files, published, publisher, claim };
}

/** Lo que importa de una declaración (sin eventId ni fechas). */
function shape(e: IntegrationEvent) {
  const d = e as FileSourceDeclaredEvent;
  return {
    eventType: d.eventType,
    tenantId: d.tenantId,
    correlationId: d.correlationId,
    fileId: d.fileId,
    sourceService: d.sourceService,
    sourceResourceType: d.sourceResourceType,
    sourceResourceId: d.sourceResourceId,
    sourceRole: d.sourceRole,
    relatedCustomerId: d.relatedCustomerId,
  };
}

const byFile = (a: { fileId: string }, b: { fileId: string }) => a.fileId.localeCompare(b.fileId);

describe('claimFileSources — cloudstorage.file_sources_claim_requested.v1', () => {
  it('declara solo los suyos, con los mismos valores que la reconciliación manual', async () => {
    const s = seeded();
    const own = [
      s.files.attachment,
      s.files.voiceNote,
      s.files.callRec,
      s.files.meetingRec,
      s.files.txt,
      s.files.json,
    ];

    await s.claim({ fileIds: [...own, u(), u()] });

    const manual: IntegrationEvent[] = [];
    const deps = {
      fileLibrary: s.repo,
      publisher: { enqueue: async (e: IntegrationEvent) => void manual.push(e) },
    };
    for (const kind of ['chat', 'recordings'] as const) {
      await redeclareFileSources(
        { tenantId: s.tenantId, kind, dryRun: false, correlationId: 'corr-1' },
        deps,
      );
    }
    expect(s.published.map(shape).sort(byFile)).toEqual(manual.map(shape).sort(byFile));
    expect(new Set(s.published.map((e) => (e as FileSourceDeclaredEvent).fileId))).toEqual(new Set(own));
  });

  it('acepta FileIds en PascalCase y solo declara los pedidos', async () => {
    const s = seeded();

    await s.claim({ FileIds: [s.files.voiceNote, s.files.json] });

    expect(s.published.map(shape)).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          fileId: s.files.voiceNote,
          sourceResourceType: 'conversation',
          sourceRole: 'voice_note',
        }),
        expect.objectContaining({
          fileId: s.files.json,
          sourceResourceType: 'call',
          sourceRole: 'transcript_data',
        }),
      ]),
    );
    expect(s.published).toHaveLength(2);
  });

  it('respeta el tenant: el archivo de otra oficina no se declara', async () => {
    const s = seeded();

    await s.claim({ fileIds: [s.files.foreignTenant, s.files.attachment] });
    expect(s.published.map((e) => (e as FileSourceDeclaredEvent).fileId)).toEqual([s.files.attachment]);

    s.published.length = 0;
    await s.claim({ fileIds: [s.files.attachment] }, s.otherTenant);
    expect(s.published).toEqual([]);
  });

  it('lista vacía o sin ids válidos: no consulta ni publica', async () => {
    const s = seeded();
    const spy = vi.spyOn(s.repo, 'findChatAttachmentsForRedeclare');

    await s.claim({ fileIds: [] });
    await s.claim({ fileIds: ['not-a-guid'] });
    await s.claim({});

    expect(s.published).toEqual([]);
    expect(spy).not.toHaveBeenCalled();
  });
});
