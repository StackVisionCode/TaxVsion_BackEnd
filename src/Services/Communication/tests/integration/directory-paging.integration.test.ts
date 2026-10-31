import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaCustomerDirectoryRepository } from '../../src/infrastructure/persistence/prisma-customer-directory-repository.js';
import { PrismaUserDirectoryRepository } from '../../src/infrastructure/persistence/prisma-user-directory-repository.js';
import type { DirectoryCursor } from '../../src/application/ports/directory-pager.js';

/** F8c (T-14) contra SQL Server real: cursor estable, `q` opcional, ABAC dentro de la consulta y LIKE literal. */
const prisma = new PrismaClient();
const customers = new PrismaCustomerDirectoryRepository(prisma);
const staff = new PrismaUserDirectoryRepository(prisma);
const tenantId = randomUUID();
const preparer = randomUUID();
const customersSeed = ['Ana 50%', 'Beto', 'Carla', 'Diego', 'Eva', 'Fede', 'Gina'].map((name, i) => ({ id: randomUUID(), name, active: i !== 6 }));
const [zoe, yago, xime] = [randomUUID(), randomUUID(), randomUUID()];

beforeAll(async () => {
  await prisma.customerDirectoryEntry.createMany({
    data: customersSeed.map((c, i) => ({ CustomerId: c.id, TenantId: tenantId, DisplayName: c.name, Email: `c${i}@x.test`, IsActive: c.active })),
  });
  // Asignados al preparador: Beto, Diego y Fede (índices 1, 3, 5).
  await prisma.customerAssignmentProjection.createMany({
    data: customersSeed.filter((_, i) => i % 2 === 1).map((c) => ({ TenantId: tenantId, CustomerId: c.id, UserId: preparer, Version: new Date() })),
  });
  await prisma.userDirectoryEntry.createMany({
    data: [
      { UserId: zoe, TenantId: tenantId, DisplayName: 'Zoe', Email: 'z@x.test', ActorType: 'TenantEmployee' },
      { UserId: yago, TenantId: tenantId, DisplayName: 'Yago', Email: 'y@x.test', ActorType: 'TenantAdmin' },
      { UserId: xime, TenantId: tenantId, DisplayName: 'Xime', Email: 'x@x.test', ActorType: 'CustomerPortal' },
    ],
  });
});

afterAll(async () => {
  await prisma.customerAssignmentProjection.deleteMany({ where: { TenantId: tenantId } });
  await prisma.customerDirectoryEntry.deleteMany({ where: { TenantId: tenantId } });
  await prisma.userDirectoryEntry.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('directorio paginado (SQL real)', () => {
  it('recorre todos los clientes activos por páginas de 2 sin repetir ni saltar', async () => {
    const seen: string[] = [];
    let after: DirectoryCursor | null = null;
    for (let i = 0; i < 10; i++) {
      const page = await customers.pageCustomers({ tenantId, query: null, take: 2, after, assignedToUserId: null });
      seen.push(...page.map((r) => r.displayName));
      const last = page.at(-1);
      if (page.length < 2 || !last) break;
      after = { displayName: last.displayName, id: last.customerId };
    }
    expect(seen).toEqual(['Ana 50%', 'Beto', 'Carla', 'Diego', 'Eva', 'Fede']);
  });

  it('el ABAC va en la consulta: la primera página del preparador ya trae sus 2 asignados', async () => {
    const page = await customers.pageCustomers({ tenantId, query: null, take: 2, after: null, assignedToUserId: preparer });
    expect(page.map((r) => r.displayName)).toEqual(['Beto', 'Diego']);
  });

  it('`%` del término se busca literal', async () => {
    expect((await customers.pageCustomers({ tenantId, query: '50%', take: 5, after: null, assignedToUserId: null })).map((r) => r.displayName)).toEqual(['Ana 50%']);
    expect(await customers.pageCustomers({ tenantId, query: '5_%', take: 5, after: null, assignedToUserId: null })).toEqual([]);
  });

  it('staff: sin clientes de portal ni el propio usuario', async () => {
    const page = await staff.pageStaff({ tenantId, query: null, excludeUserId: zoe, take: 10, after: null });
    expect(page.map((r) => r.displayName)).toEqual(['Yago']);
  });
});
