import { PrismaClient } from '@prisma/client';
import { logger } from '../logger/logger.js';

/**
 * Prisma singleton. La conexion vive todo el ciclo del proceso; en tests se
 * mockea o se usa Testcontainers (fuera de scope Fase 0).
 */
export const prisma: PrismaClient = new PrismaClient({
  log: [
    { emit: 'event', level: 'warn' },
    { emit: 'event', level: 'error' },
  ],
});

// Reenviamos logs de Prisma al logger estructurado (no a stdout crudo).
prisma.$on('warn' as never, (e: unknown) => logger.warn({ prisma: e }, 'Prisma warn'));
prisma.$on('error' as never, (e: unknown) => logger.error({ prisma: e }, 'Prisma error'));

export async function connectPrisma(): Promise<void> {
  await prisma.$connect();
  logger.info('Prisma connected');
}

export async function disconnectPrisma(): Promise<void> {
  await prisma.$disconnect();
  logger.info('Prisma disconnected');
}
