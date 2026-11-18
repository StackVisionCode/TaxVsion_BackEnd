import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaCustomerDirectoryRepository } from '../../src/infrastructure/persistence/prisma-customer-directory-repository.js';

/**
 * F-E contra SQL Server real: el cliente sin email entra al directorio, un aviso de perfil viejo no pisa uno
 * nuevo, un email nulo no borra el conocido (el email no se quita) y el estado del aviso de cambio respeta el
 * orden de los avisos de estado.
 */
const prisma = new PrismaClient();
const customers = new PrismaCustomerDirectoryRepository(prisma);
const tenantId = randomUUID();
const t1 = new Date('2026-11-18T10:00:00.000Z');
const t2 = new Date('2026-11-18T10:05:00.000Z');

afterAll(async () => {
  await prisma.customerDirectoryEntry.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

const row = (customerId: string) =>
  prisma.customerDirectoryEntry.findUnique({ where: { CustomerId: customerId } });

describe('directorio de clientes: email opcional y orden del perfil (SQL real)', () => {
  it('un cliente sin email entra con Email null', async () => {
    const customerId = randomUUID();

    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: null,
      isActive: true,
      occurredAtUtc: t1,
    });

    const saved = await row(customerId);
    expect(saved?.Email).toBeNull();
    expect(saved?.IsActive).toBe(true);
  });

  it('el email que llega después se guarda, y un aviso viejo no lo pisa', async () => {
    const customerId = randomUUID();
    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: null,
      isActive: true,
      occurredAtUtc: t1,
    });

    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada L',
      email: 'ada@x.test',
      occurredAtUtc: t2,
    });
    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada viejo',
      email: null,
      occurredAtUtc: t1,
    });

    const saved = await row(customerId);
    expect(saved?.Email).toBe('ada@x.test');
    expect(saved?.DisplayName).toBe('Ada L');
  });

  it('un email nulo no borra el conocido', async () => {
    const customerId = randomUUID();
    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: 'ada@x.test',
      isActive: true,
      occurredAtUtc: t1,
    });

    await customers.upsert({ customerId, tenantId, displayName: 'Ada', email: null, occurredAtUtc: t2 });

    expect((await row(customerId))?.Email).toBe('ada@x.test');
  });

  it('el estado de un aviso de cambio viejo no reactiva a un cliente desactivado después', async () => {
    const customerId = randomUUID();
    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: null,
      isActive: true,
      occurredAtUtc: t1,
    });
    await customers.markInactive(customerId, t2);

    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: 'ada@x.test',
      isActive: true,
      occurredAtUtc: t1,
    });

    expect((await row(customerId))?.IsActive).toBe(false);
  });

  it('el aviso de cambio que crea la fila tarde la deja con su estado', async () => {
    const customerId = randomUUID();

    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: 'ada@x.test',
      isActive: false,
      occurredAtUtc: t2,
    });
    await customers.markActive(customerId, t1);

    expect((await row(customerId))?.IsActive).toBe(false);
  });

  it('la reconciliación (sin fecha) aplica siempre', async () => {
    const customerId = randomUUID();
    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada',
      email: null,
      isActive: true,
      occurredAtUtc: t2,
    });

    await customers.upsert({
      customerId,
      tenantId,
      displayName: 'Ada R',
      email: 'r@x.test',
      isActive: false,
    });

    const saved = await row(customerId);
    expect(saved?.DisplayName).toBe('Ada R');
    expect(saved?.Email).toBe('r@x.test');
    expect(saved?.IsActive).toBe(false);
  });
});
