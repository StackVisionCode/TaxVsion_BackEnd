import type { PrismaClient } from '@prisma/client';
import type { Redis } from 'ioredis';
import { randomUUID } from 'node:crypto';
import type {
  IdempotencyReservation,
  IdempotencyStore,
} from '../../application/ports/idempotency-store.js';

/**
 * Idempotency store con dos capas:
 *   L1 = Redis (SET NX PX) — coordina reservas frescas entre pods sin tocar DB.
 *   L2 = Prisma IdempotencyRecord — persiste el resultado para replays fuera de la ventana Redis.
 *
 * El `token` en la reserva se usa para asegurarse de que solo quien reservó
 * puede liberar (evita release ajeno en race conditions).
 *
 * Cierre CRIT-10: idempotencia explicita y compartida entre pods.
 */
export class PrismaIdempotencyStore implements IdempotencyStore {
  constructor(
    private readonly prisma: PrismaClient,
    private readonly redis: Redis,
  ) {}

  async tryReserve<T>(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    ttlSeconds: number;
  }): Promise<IdempotencyReservation<T>> {
    const persisted = await this.prisma.idempotencyRecord.findUnique({
      where: {
        TenantId_UserId_Scope_ClientKey: {
          TenantId: input.tenantId,
          UserId: input.userId,
          Scope: input.scope,
          ClientKey: input.clientKey,
        },
      },
    });
    if (persisted) {
      return { status: 'replay', payload: JSON.parse(persisted.ResultPayload) as T };
    }

    const token = randomUUID();
    const key = this.buildKey(input);
    const ok = await this.redis.set(key, token, 'PX', input.ttlSeconds * 1000, 'NX');
    if (ok !== 'OK') {
      // Otro pod la tiene reservada — reintentamos DB lookup rápido; si sigue
      // sin haber commit, devolvemos "fresh" con un token nuevo pero perderemos
      // el race (la commit del ganador se replicará luego).
      const persistedAgain = await this.prisma.idempotencyRecord.findUnique({
        where: {
          TenantId_UserId_Scope_ClientKey: {
            TenantId: input.tenantId,
            UserId: input.userId,
            Scope: input.scope,
            ClientKey: input.clientKey,
          },
        },
      });
      if (persistedAgain) {
        return { status: 'replay', payload: JSON.parse(persistedAgain.ResultPayload) as T };
      }
    }
    return { status: 'fresh', token };
  }

  async commit<T>(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    payload: T;
    token: string;
  }): Promise<void> {
    await this.prisma.idempotencyRecord.upsert({
      where: {
        TenantId_UserId_Scope_ClientKey: {
          TenantId: input.tenantId,
          UserId: input.userId,
          Scope: input.scope,
          ClientKey: input.clientKey,
        },
      },
      create: {
        TenantId: input.tenantId,
        UserId: input.userId,
        Scope: input.scope,
        ClientKey: input.clientKey,
        ResultPayload: JSON.stringify(input.payload),
      },
      update: {
        ResultPayload: JSON.stringify(input.payload),
      },
    });
    await this.releaseWithToken(input, input.token);
  }

  async release(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    token: string;
  }): Promise<void> {
    await this.releaseWithToken(input, input.token);
  }

  private async releaseWithToken(
    input: { tenantId: string; userId: string; scope: string; clientKey: string },
    token: string,
  ): Promise<void> {
    const key = this.buildKey(input);
    // Solo borra si el valor coincide con el token — evita liberar reservas ajenas.
    const script = `if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end`;
    await this.redis.eval(script, 1, key, token);
  }

  private buildKey(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
  }): string {
    return `comm:idem:${input.tenantId}:${input.userId}:${input.scope}:${input.clientKey}`;
  }
}
