import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaFileLibraryReadRepository } from '../../src/infrastructure/persistence/prisma-file-library-read-repository.js';

/** Biblioteca de archivos contra SQL Server real: ámbito, visibilidad y backfill de grabaciones y transcripts. */
const prisma = new PrismaClient();
const repo = new PrismaFileLibraryReadRepository(prisma);
const tenantId = randomUUID();
const me = randomUUID();
const other = randomUUID();
const customerId = randomUUID();
const ids = {
  myCall: randomUUID(),
  internalCall: randomUUID(),
  clientCall: randomUUID(),
  myMeeting: randomUUID(),
  foreignMeeting: randomUUID(),
};
const files = {
  myCallRec: randomUUID(),
  myCallTxt: randomUUID(),
  myCallJson: randomUUID(),
  internalRec: randomUUID(),
  clientRec: randomUUID(),
  meetingRec1: randomUUID(),
  meetingRec2: randomUUID(),
  legacyTxt: randomUUID(),
  processingRec: randomUUID(),
  purgedRec: randomUUID(),
  purgedTxt: randomUUID(),
  foreignRec: randomUUID(),
};
const sessionIds: Record<string, string> = {};

async function call(id: string, caller: string, callee: string, clientCustomer?: string) {
  await prisma.call.create({
    data: {
      Id: id,
      TenantId: tenantId,
      Kind: 'Audio',
      Status: 'Ended',
      CallerUserId: caller,
      CalleeUserId: callee,
      RingingAtUtc: new Date(),
      Participants: {
        create: [
          { TenantId: tenantId, UserId: caller, DisplayName: 'Caller', Role: 'Caller', JoinOrder: 1 },
          {
            TenantId: tenantId,
            UserId: callee,
            DisplayName: 'Callee',
            Role: 'Callee',
            JoinOrder: 2,
            CustomerId: clientCustomer ?? null,
          },
        ],
      },
    },
  });
}

async function meeting(id: string, host: string, participant?: string) {
  await prisma.meeting.create({
    data: {
      Id: id,
      TenantId: tenantId,
      Title: 'Review',
      Status: 'Ended',
      ShortCode: id.slice(0, 8),
      HostUserId: host,
      CreatedByUserId: host,
      ...(participant
        ? {
            Participants: {
              create: [
                {
                  TenantId: tenantId,
                  UserId: participant,
                  DisplayName: 'Guest',
                  Role: 'Attendee',
                  Status: 'Left',
                  JoinOrder: 2,
                },
              ],
            },
          }
        : {}),
    },
  });
}

async function session(
  key: string,
  scope: 'Call' | 'Meeting',
  scopeId: string,
  fileId: string,
  opts: { state?: string; purged?: boolean; sequence?: number } = {},
) {
  const id = randomUUID();
  sessionIds[key] = id;
  await prisma.recordingSession.create({
    data: {
      Id: id,
      TenantId: tenantId,
      Scope: scope,
      ScopeId: scopeId,
      State: opts.state ?? 'Ready',
      RequestedByUserId: other,
      RequestedAtUtc: new Date(),
      ReadyAtUtc: new Date(),
      RecordingFileId: fileId,
      PurgedAtUtc: opts.purged ? new Date() : null,
      Sequence: opts.sequence ?? 1,
    },
  });
  return id;
}

beforeAll(async () => {
  await call(ids.myCall, me, other);
  await call(ids.internalCall, other, randomUUID());
  await call(ids.clientCall, other, randomUUID(), customerId);
  await meeting(ids.myMeeting, other, me);
  await meeting(ids.foreignMeeting, other);

  const myCallSession = await session('myCall', 'Call', ids.myCall, files.myCallRec);
  await prisma.transcriptArtifact.create({
    data: {
      TenantId: tenantId,
      Scope: 'Call',
      ScopeId: ids.myCall,
      RecordingSessionId: myCallSession,
      State: 'Ready',
      TxtFileId: files.myCallTxt,
      JsonFileId: files.myCallJson,
    },
  });
  await session('internal', 'Call', ids.internalCall, files.internalRec);
  await session('client', 'Call', ids.clientCall, files.clientRec);
  await session('meeting1', 'Meeting', ids.myMeeting, files.meetingRec1, { sequence: 1 });
  await session('meeting2', 'Meeting', ids.myMeeting, files.meetingRec2, { sequence: 2 });
  // Transcript anterior a D-71 (sin sesión): cuenta como el de la primera grabación.
  await prisma.transcriptArtifact.create({
    data: {
      TenantId: tenantId,
      Scope: 'Meeting',
      ScopeId: ids.myMeeting,
      State: 'Ready',
      TxtFileId: files.legacyTxt,
    },
  });
  await session('processing', 'Meeting', ids.foreignMeeting, files.processingRec, {
    state: 'Processing',
    sequence: 1,
  });
  const purged = await session('purged', 'Meeting', ids.foreignMeeting, files.purgedRec, {
    purged: true,
    sequence: 2,
  });
  await prisma.transcriptArtifact.create({
    data: {
      TenantId: tenantId,
      Scope: 'Meeting',
      ScopeId: ids.foreignMeeting,
      RecordingSessionId: purged,
      State: 'Ready',
      TxtFileId: files.purgedTxt,
    },
  });
});

afterAll(async () => {
  await prisma.transcriptArtifact.deleteMany({ where: { TenantId: tenantId } });
  await prisma.recordingSession.deleteMany({ where: { TenantId: tenantId } });
  await prisma.customerAssignmentProjection.deleteMany({ where: { TenantId: tenantId } });
  await prisma.call.deleteMany({ where: { TenantId: tenantId } });
  await prisma.meeting.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('PrismaFileLibraryReadRepository — grabaciones (SQL real)', () => {
  it('llamadas: las propias; con call.record, además las que tienen cliente (nunca internas ajenas)', async () => {
    const own = await repo.listCallRecordingScope({ tenantId, userId: me, clientCalls: 'none', take: 10 });
    const all = await repo.listCallRecordingScope({ tenantId, userId: me, clientCalls: 'all', take: 10 });
    const before = await repo.listCallRecordingScope({
      tenantId,
      userId: me,
      clientCalls: 'assigned',
      take: 10,
    });
    await prisma.customerAssignmentProjection.create({
      data: { TenantId: tenantId, CustomerId: customerId, UserId: me, Version: new Date() },
    });
    const after = await repo.listCallRecordingScope({
      tenantId,
      userId: me,
      clientCalls: 'assigned',
      take: 10,
    });

    expect(own.map((r) => r.scopeId)).toEqual([ids.myCall]);
    expect(new Set(all.map((r) => r.scopeId))).toEqual(new Set([ids.myCall, ids.clientCall]));
    expect(before.map((r) => r.scopeId)).toEqual([ids.myCall]);
    expect(new Set(after.map((r) => r.scopeId))).toEqual(new Set([ids.myCall, ids.clientCall]));
  });

  it('reuniones: solo donde fue anfitrión o participante y hay una grabación lista no purgada', async () => {
    const mine = await repo.listMeetingRecordingScope({ tenantId, userId: me, take: 10 });
    const host = await repo.listMeetingRecordingScope({ tenantId, userId: other, take: 10 });

    expect(mine.map((r) => r.scopeId)).toEqual([ids.myMeeting]);
    // La reunión ajena solo tiene una grabación en proceso y otra purgada.
    expect(host.map((r) => r.scopeId)).toEqual([ids.myMeeting]);
  });

  it('por archivo: grabaciones listas y .txt; nunca .json, en proceso, purgados ni otro tenant', async () => {
    const rows = await repo.findRecordingFiles({ tenantId, fileIds: Object.values(files) });
    const byFile = new Map(rows.map((r) => [r.fileId, r]));

    expect(new Set(byFile.keys())).toEqual(
      new Set([
        files.myCallRec,
        files.myCallTxt,
        files.internalRec,
        files.clientRec,
        files.meetingRec1,
        files.meetingRec2,
        files.legacyTxt,
      ]),
    );
    expect(byFile.get(files.myCallTxt)).toMatchObject({
      scope: 'Call',
      scopeId: ids.myCall,
      role: 'transcript',
    });
    expect(byFile.get(files.meetingRec2)).toMatchObject({ role: 'recording', sequence: 2, readyCount: 2 });
    expect(byFile.get(files.legacyTxt)).toMatchObject({ role: 'transcript', sequence: 1 });
    expect(await repo.findRecordingFiles({ tenantId: randomUUID(), fileIds: [files.myCallRec] })).toEqual([]);
  });

  it('el backfill salta lo purgado: grabaciones con archivo y transcripts listos de sesiones vivas', async () => {
    const recordings = await repo.listRecordingsForRedeclare({ tenantId, afterId: null, take: 100 });
    const transcripts = await repo.listTranscriptsForRedeclare({ tenantId, afterId: null, take: 100 });
    const firstTwo = await repo.listRecordingsForRedeclare({ tenantId, afterId: null, take: 2 });
    const rest = await repo.listRecordingsForRedeclare({
      tenantId,
      afterId: firstTwo.at(-1)?.id ?? null,
      take: 100,
    });

    expect(new Set(recordings.map((r) => r.fileId))).toEqual(
      new Set([
        files.myCallRec,
        files.internalRec,
        files.clientRec,
        files.meetingRec1,
        files.meetingRec2,
        files.processingRec,
      ]),
    );
    expect([...firstTwo, ...rest].map((r) => r.id)).toEqual(recordings.map((r) => r.id));
    expect(new Set(transcripts.map((t) => t.txtFileId))).toEqual(new Set([files.myCallTxt, files.legacyTxt]));
    expect(transcripts.find((t) => t.txtFileId === files.myCallTxt)?.jsonFileId).toBe(files.myCallJson);
  });

  it('el reclamo por ids usa el criterio del backfill', async () => {
    const recordings = await repo.findRecordingsForRedeclare({ tenantId, fileIds: Object.values(files) });
    const byJson = await repo.findTranscriptsForRedeclare({ tenantId, fileIds: [files.myCallJson] });
    const purged = await repo.findTranscriptsForRedeclare({ tenantId, fileIds: [files.purgedTxt] });

    expect(new Set(recordings.map((r) => r.fileId))).toEqual(
      new Set([
        files.myCallRec,
        files.internalRec,
        files.clientRec,
        files.meetingRec1,
        files.meetingRec2,
        files.processingRec,
      ]),
    );
    expect(byJson.map((t) => [t.txtFileId, t.jsonFileId])).toEqual([[files.myCallTxt, files.myCallJson]]);
    expect(purged).toEqual([]);
    expect(await repo.findRecordingsForRedeclare({ tenantId: randomUUID(), fileIds: [files.myCallRec] })).toEqual([]);
  });

  it('los clientes de la llamada salen con su cliente', async () => {
    const rows = await repo.listRecordingClients({
      tenantId,
      scope: 'Call',
      scopeIds: [ids.clientCall, ids.myCall],
    });

    expect(rows).toEqual([{ scopeId: ids.clientCall, actorType: 'CustomerPortal', customerId }]);
  });
});
