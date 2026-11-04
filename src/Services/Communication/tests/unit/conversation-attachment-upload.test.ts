import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { Result } from '../../src/domain/shared/result.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type {
  CloudStorageUploadClient,
  CloudStorageInitiatedUpload,
} from '../../src/application/ports/cloudstorage-upload-client.js';
import { createAttachmentUpload } from '../../src/application/use-cases/create-attachment-upload.js';
import { completeAttachmentUpload } from '../../src/application/use-cases/complete-attachment-upload.js';

function u(): string {
  return randomUUID();
}

function conversationRepo(members: string[] | null): ConversationRepository {
  const conversation =
    members === null
      ? null
      : ({
          isParticipant: (uid: string) => members.includes(uid),
          ensureCanWrite: () => Result.okVoid(),
        } as unknown as Conversation);
  return { async findById() {
      return conversation;
    } } as unknown as ConversationRepository;
}

const initiated: CloudStorageInitiatedUpload = {
  fileId: u(),
  uploadUrl: 'https://minio.local/upload',
  formData: { key: 'obj', policy: 'p' },
  expiresAtUtc: '2026-01-01T00:00:00.000Z',
};

const uploadOk: CloudStorageUploadClient = {
  async initiate() {
    return initiated;
  },
  async complete() {
    return { status: 'PendingScan' };
  },
};

const tenantId = u();
const conversationId = u();
const memberId = u();

const cmd = {
  tenantId,
  userId: memberId,
  conversationId,
  originalName: 'w-2.pdf',
  contentType: 'application/pdf',
  sizeBytes: 1024,
};

function reservations() {
  return { reserve: vi.fn().mockResolvedValue(undefined), consume: vi.fn().mockResolvedValue(true), purgeOlderThan: vi.fn().mockResolvedValue(0) };
}

describe('conversation attachment upload mediation', () => {
  it('reserva la subida para ese usuario y esa conversación (F11, E-259)', async () => {
    const attachmentReservations = reservations();
    const result = await createAttachmentUpload(cmd, { conversations: conversationRepo([memberId]), cloudStorageUpload: uploadOk, attachmentReservations });
    expect(result.isSuccess).toBe(true);
    expect(attachmentReservations.reserve).toHaveBeenCalledWith({ fileId: initiated.fileId, tenantId, conversationId, uploadedByUserId: memberId });
  });

  it('initiates an upload for a participant', async () => {
    const result = await createAttachmentUpload(cmd, {
      conversations: conversationRepo([memberId]),
      cloudStorageUpload: uploadOk,
      attachmentReservations: reservations(),
    });
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.fileId).toBe(initiated.fileId);
      expect(result.value.uploadUrl).toBe('https://minio.local/upload');
      expect(result.value.formData).toEqual({ key: 'obj', policy: 'p' });
    }
  });

  it('rechaza la subida en un Direct cuyo otro participante ya no está en la oficina', async () => {
    const direct = Conversation.startDirect({
      tenantId,
      initiator: { userId: memberId, displayName: 'Admin', actorType: 'TenantAdmin' },
      recipient: { userId: u(), displayName: 'Ana Leaver', actorType: 'TenantEmployee' },
    });
    if (!direct.isSuccess) throw new Error('setup');
    const leaver = direct.value.getParticipantSnapshots().find((p) => p.userId !== memberId)!;
    direct.value.retireParticipantBySystem(leaver.userId);
    const initiate = vi.fn();
    const result = await createAttachmentUpload(cmd, {
      conversations: { findById: async () => direct.value } as unknown as ConversationRepository,
      cloudStorageUpload: { ...uploadOk, initiate },
      attachmentReservations: reservations(),
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Chat.Conversation.ParticipantLeft');
      expect(result.error.message).toBe('Ana Leaver is no longer with the office.');
    }
    expect(initiate).not.toHaveBeenCalled();
  });

  it('rejects an upload from a non-participant', async () => {
    const result = await createAttachmentUpload(
      { ...cmd, userId: u() },
      { conversations: conversationRepo([memberId]), cloudStorageUpload: uploadOk, attachmentReservations: reservations() },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('rejects an upload for a missing conversation', async () => {
    const result = await createAttachmentUpload(cmd, {
      conversations: conversationRepo(null),
      cloudStorageUpload: uploadOk,
      attachmentReservations: reservations(),
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Conversation.NotFound');
  });

  it('completes an upload for a participant', async () => {
    const fileId = u();
    const result = await completeAttachmentUpload(
      { tenantId, userId: memberId, conversationId, fileId },
      { conversations: conversationRepo([memberId]), cloudStorageUpload: uploadOk },
    );
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.fileId).toBe(fileId);
      expect(result.value.status).toBe('PendingScan');
    }
  });

  it('rejects a complete from a non-participant', async () => {
    const result = await completeAttachmentUpload(
      { tenantId, userId: u(), conversationId, fileId: u() },
      { conversations: conversationRepo([memberId]), cloudStorageUpload: uploadOk },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });
});
