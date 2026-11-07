import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaUserPermissionsProjectionRepository } from '../../src/infrastructure/persistence/prisma-user-permissions-projection.js';
import { PrismaUserDirectoryRepository } from '../../src/infrastructure/persistence/prisma-user-directory-repository.js';
import { PrismaCustomerPortalAccountRepository } from '../../src/infrastructure/persistence/prisma-customer-portal-account-repository.js';
import { PrismaCustomerDirectoryRepository } from '../../src/infrastructure/persistence/prisma-customer-directory-repository.js';

/**
 * Orden de los avisos de estado contra SQL Server real: un desactivar viejo que llega después de un
 * reactivar nuevo no gana (prefetch 20 + reintentos no respetan el orden de Auth/Customer).
 */
const prisma = new PrismaClient();
const permissions = new PrismaUserPermissionsProjectionRepository(prisma);
const users = new PrismaUserDirectoryRepository(prisma);
const portal = new PrismaCustomerPortalAccountRepository(prisma);
const customers = new PrismaCustomerDirectoryRepository(prisma);
const tenantId = randomUUID();
const userId = randomUUID();
const customerId = randomUUID();
const t1 = new Date('2026-11-01T10:00:00.000Z');
const t2 = new Date('2026-11-01T10:05:00.000Z');
const now = new Date();

beforeAll(async () => {
  await prisma.userPermissionsProjection.create({
    data: {
      UserId: userId,
      TenantId: tenantId,
      Permissions: '[]',
      ActorType: 'CustomerPortal',
      IsActive: false,
    },
  });
  await prisma.userDirectoryEntry.create({
    data: { UserId: userId, TenantId: tenantId, DisplayName: 'Ana', Email: 'ana@x.test', IsActive: false },
  });
  await prisma.customerPortalAccount.create({
    data: { CustomerId: customerId, TenantId: tenantId, UserId: userId, IsActive: false },
  });
  await prisma.customerDirectoryEntry.create({
    data: {
      CustomerId: customerId,
      TenantId: tenantId,
      DisplayName: 'Ana',
      Email: 'ana@x.test',
      IsActive: false,
    },
  });
});

afterAll(async () => {
  await prisma.userPermissionsProjection.deleteMany({ where: { TenantId: tenantId } });
  await prisma.userDirectoryEntry.deleteMany({ where: { TenantId: tenantId } });
  await prisma.customerPortalAccount.deleteMany({ where: { TenantId: tenantId } });
  await prisma.customerDirectoryEntry.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

async function activeFlags() {
  const [p, u, a, c] = await Promise.all([
    prisma.userPermissionsProjection.findUnique({ where: { UserId: userId } }),
    prisma.userDirectoryEntry.findUnique({ where: { UserId: userId } }),
    prisma.customerPortalAccount.findUnique({ where: { CustomerId: customerId } }),
    prisma.customerDirectoryEntry.findUnique({ where: { CustomerId: customerId } }),
  ]);
  return [p?.IsActive, u?.IsActive, a?.IsActive, c?.IsActive];
}

describe('orden de los avisos de estado (SQL real)', () => {
  it('reactivar (t2) y luego un desactivar viejo (t1): queda activo', async () => {
    await permissions.markActive(userId, now, t2);
    await users.markActive(userId, t2);
    await portal.markActiveByUserId(userId, t2);
    await customers.markActive(customerId, t2);

    await permissions.markInactive(userId, now, t1);
    await users.markInactive(userId, t1);
    await portal.markInactiveByUserId(userId, t1);
    await customers.markInactive(customerId, t1);

    expect(await activeFlags()).toEqual([true, true, true, true]);
  });

  it('un aviso más nuevo sí se aplica', async () => {
    const t3 = new Date('2026-11-01T10:10:00.000Z');
    await permissions.markInactive(userId, now, t3);
    await users.markInactive(userId, t3);
    await portal.markInactiveByUserId(userId, t3);
    await customers.markInactive(customerId, t3);

    expect(await activeFlags()).toEqual([false, false, false, false]);
  });
});
