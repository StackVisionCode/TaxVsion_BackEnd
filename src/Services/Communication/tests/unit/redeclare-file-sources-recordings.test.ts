import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { redeclareFileSources } from '../../src/application/use-cases/redeclare-file-sources.js';
import type { FileSourceDeclaredEvent } from '../../src/contracts/events/cloudstorage-events.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import { FakeFileLibraryReadRepository } from '../helpers/chat-file-library.js';

const u = () => randomUUID();

/** Backfill `kind=recordings`: grabaciones no purgadas con archivo y transcripts `Ready` (txt y json). */
function seeded() {
  const tenantId = u();
  const repo = new FakeFileLibraryReadRepository();
  const [call, meeting, customerId] = [u(), u(), u()];
  const files = { callRec: u(), meetingRec: u(), txt: u(), json: u(), meetingTxt: u() };
  repo.recordingRedeclare.push(
    { tenantId, id: u(), scope: 'Call', scopeId: call, fileId: files.callRec },
    { tenantId, id: u(), scope: 'Meeting', scopeId: meeting, fileId: files.meetingRec },
  );
  repo.transcriptRedeclare.push(
    { tenantId, id: u(), scope: 'Call', scopeId: call, txtFileId: files.txt, jsonFileId: files.json },
    { tenantId, id: u(), scope: 'Meeting', scopeId: meeting, txtFileId: files.meetingTxt, jsonFileId: null },
  );
  repo.recordingClients.push({
    tenantId,
    scope: 'Call',
    scopeId: call,
    actorType: 'CustomerPortal',
    customerId,
  });
  const published: IntegrationEvent[] = [];
  const deps = {
    fileLibrary: repo,
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
  };
  return { tenantId, call, meeting, customerId, files, published, deps };
}

describe('redeclareFileSources — kind=recordings', () => {
  it('dryRun cuenta los archivos sin encolar', async () => {
    const s = seeded();

    const result = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'recordings', dryRun: true, correlationId: 'c' },
      s.deps,
    );

    expect(result).toEqual({ dryRun: true, scanned: 5, declared: 0 });
    expect(s.published).toEqual([]);
  });

  it('real declara grabación, transcript y transcript_data con el recurso y el cliente de la llamada', async () => {
    const s = seeded();

    const result = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'recordings', dryRun: false, correlationId: 'c' },
      s.deps,
    );

    expect(result).toEqual({ dryRun: false, scanned: 5, declared: 5 });
    const rows = (s.published as FileSourceDeclaredEvent[]).map((e) => [
      e.fileId,
      e.sourceResourceType,
      e.sourceResourceId,
      e.sourceRole,
      e.relatedCustomerId,
    ]);
    expect(rows).toEqual(
      expect.arrayContaining([
        [s.files.callRec, 'call', s.call, 'recording', s.customerId],
        [s.files.meetingRec, 'meeting', s.meeting, 'recording', null],
        [s.files.txt, 'call', s.call, 'transcript', s.customerId],
        [s.files.json, 'call', s.call, 'transcript_data', s.customerId],
        [s.files.meetingTxt, 'meeting', s.meeting, 'transcript', null],
      ]),
    );
    expect(rows).toHaveLength(5);
  });
});
