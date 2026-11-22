import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaNotificationRepository } from '../../src/infrastructure/persistence/prisma-notification-repository.js';
import { getNotification } from '../../src/application/use-cases/notification-queries.js';
import { Notification } from '../../src/domain/notifications/notification.js';

/**
 * Detalle por id con SQL real: solo devuelve la fila propia y no descartada, y no la marca leída.
 * Oficinas y usuarios aleatorios; se borran solo estas filas al final.
 */
const prisma = new PrismaClient();
const notifications = new PrismaNotificationRepository(prisma);
const tenantId = randomUUID();
const otherTenantId = randomUUID();
const userId = randomUUID();
const otherUserId = randomUUID();

async function seed(tenant: string, user: string): Promise<string> {
  const created = Notification.create({
    tenantId: tenant,
    userId: user,
    kind: 'signature.completed',
    priority: 'High',
    title: 'Document signed',
    body: 'The client signed the engagement letter.',
    resourceType: 'signature-request',
    resourceId: randomUUID(),
    params: { documentName: 'Engagement letter' },
    sourceEventId: randomUUID(),
    sourceEventType: 'SignatureCompleted',
  });
  if (!created.isSuccess) throw new Error(created.error.message);
  await notifications.createIfMissing(created.value);
  return created.value.toSnapshot().id;
}

afterAll(async () => {
  await prisma.notificationEntry.deleteMany({ where: { TenantId: { in: [tenantId, otherTenantId] } } });
  await prisma.$disconnect();
});

describe('notification detail (SQL Server)', () => {
  it("reads only the caller's own row", async () => {
    const otherUserRow = await seed(tenantId, otherUserId);
    const otherTenantRow = await seed(otherTenantId, userId);
    const ownRow = await seed(tenantId, userId);
    const dismissedRow = await seed(tenantId, userId);
    await notifications.dismiss(tenantId, userId, dismissedRow, new Date());

    const read = (notificationId: string) =>
      getNotification({ tenantId, userId, notificationId }, { notifications });

    const own = await read(ownRow);
    expect(own.isSuccess).toBe(true);
    if (!own.isSuccess) return;
    expect(own.value).toMatchObject({
      id: ownRow,
      title: 'Document signed',
      priority: 'High',
      readAtUtc: null,
    });

    for (const id of [otherUserRow, otherTenantRow, dismissedRow, randomUUID()]) {
      const result = await read(id);
      expect(result.isSuccess, id).toBe(false);
      if (!result.isSuccess) expect(result.error.code).toBe('Notification.NotFound');
    }

    const stored = await prisma.notificationEntry.findFirst({ where: { Id: ownRow } });
    expect(stored?.ReadAtUtc).toBeNull();
  });
});
