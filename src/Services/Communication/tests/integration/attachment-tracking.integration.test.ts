import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import {
  AttachmentAlreadyBoundError,
  PrismaAttachmentTrackingRepository,
} from '../../src/infrastructure/persistence/prisma-attachment-tracking-repository.js';
import { PrismaAttachmentUploadReservationRepository } from '../../src/infrastructure/persistence/prisma-attachment-upload-reservation-repository.js';

/** F11 (E-259) con SQL real: un adjunto no se reasigna a otro chat y la reserva es de un solo uso. */
const prisma = new PrismaClient();
const tracking = new PrismaAttachmentTrackingRepository(prisma);
const reservations = new PrismaAttachmentUploadReservationRepository(prisma);
const tenantId = randomUUID();
const fileIds: string[] = [];

afterAll(async () => {
  await prisma.attachmentTracking.deleteMany({ where: { FileId: { in: fileIds } } });
  await prisma.attachmentUploadReservation.deleteMany({ where: { FileId: { in: fileIds } } });
  await prisma.$disconnect();
});

describe('Adjuntos (SQL Server)', () => {
  it('registrar el mismo archivo en otro mensaje/chat falla y no mueve el original; repetir el mismo es idempotente', async () => {
    const fileId = randomUUID();
    fileIds.push(fileId);
    const original = { fileId, messageId: randomUUID(), conversationId: randomUUID(), tenantId };
    await tracking.register(original);
    await tracking.register(original);
    await expect(tracking.register({ ...original, messageId: randomUUID(), conversationId: randomUUID() })).rejects.toBeInstanceOf(
      AttachmentAlreadyBoundError,
    );
    const row = await tracking.findByFileId(fileId);
    expect([row?.conversationId, row?.messageId]).toEqual([original.conversationId, original.messageId]);
  });

  it('la reserva solo se consume con todo coincidente y una sola vez, aun con dos envíos a la vez', async () => {
    const fileId = randomUUID();
    fileIds.push(fileId);
    const r = { fileId, tenantId, conversationId: randomUUID(), uploadedByUserId: randomUUID() };
    await reservations.reserve(r);
    expect(await reservations.consume({ ...r, uploadedByUserId: randomUUID() })).toBe(false);
    const results = await Promise.all([reservations.consume(r), reservations.consume(r)]);
    expect(results.filter(Boolean)).toHaveLength(1);
  });
});
