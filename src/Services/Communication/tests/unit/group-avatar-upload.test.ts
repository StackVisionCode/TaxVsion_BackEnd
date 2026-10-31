import { randomUUID as u } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { CloudStorageUploadRequest } from '../../src/application/ports/cloudstorage-upload-client.js';
import { completeGroupAvatarUpload, createGroupAvatarUpload } from '../../src/application/use-cases/group-avatar-upload.js';

/** F8b (T-21): la foto del grupo la sube Communication a `Avatars`, solo para quien administra el grupo. */
describe('group avatar upload', () => {
  const tenantId = u();
  const owner = u();
  const member = u();

  function setup() {
    const r = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Team',
      creator: { userId: owner, displayName: 'Ana', actorType: 'TenantEmployee' },
      members: [{ userId: member, displayName: 'Beto', actorType: 'TenantEmployee' }],
    });
    if (!r.isSuccess) throw new Error(r.error.code);
    const requests: CloudStorageUploadRequest[] = [];
    const deps = {
      conversations: { findById: async () => r.value },
      cloudStorageUpload: {
        initiate: async (_t: string, req: CloudStorageUploadRequest) => {
          requests.push(req);
          return { fileId: 'f1', uploadUrl: 'https://minio.test', formData: {}, expiresAtUtc: '' };
        },
        complete: async () => ({ status: 'Pending' }),
      },
    };
    return { conversationId: r.value.id, deps, requests };
  }

  const photo = { originalName: 'team.png', contentType: 'image/png', sizeBytes: 1000 };

  it('el Owner pide la subida con destino Avatars; un Member no', async () => {
    const { conversationId, deps, requests } = setup();
    const denied = await createGroupAvatarUpload({ tenantId, userId: member, conversationId, canAdministerAnyGroup: false, ...photo }, deps);
    expect(denied.isSuccess ? 'ok' : denied.error.code).toBe('Chat.Group.AdminOnly');

    const ok = await createGroupAvatarUpload({ tenantId, userId: owner, conversationId, canAdministerAnyGroup: false, ...photo }, deps);
    expect(ok.isSuccess).toBe(true);
    expect(requests).toEqual([expect.objectContaining({ purpose: 'GroupAvatar', conversationId })]);
  });

  it('solo imágenes JPG/PNG/WebP; el superpoder confirma sin ser miembro', async () => {
    const { conversationId, deps } = setup();
    const pdf = await createGroupAvatarUpload({ tenantId, userId: owner, conversationId, canAdministerAnyGroup: false, ...photo, contentType: 'application/pdf' }, deps);
    expect(pdf.isSuccess ? 'ok' : pdf.error.code).toBe('Chat.Group.AvatarType');

    const done = await completeGroupAvatarUpload({ tenantId, userId: u(), conversationId, canAdministerAnyGroup: true, fileId: 'f1' }, deps);
    expect(done.isSuccess && done.value).toEqual({ fileId: 'f1', status: 'Pending' });
  });
});
