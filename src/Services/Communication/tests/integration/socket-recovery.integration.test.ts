import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { createServer, type Server as HttpServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { PrismaClient } from '@prisma/client';
import { buildSocketServer, type CommunicationIoServer } from '../../src/infrastructure/socket/build-io.js';
import { signJoinTicket } from '../../src/infrastructure/jwks/join-ticket.js';
import { connectRedis, disconnectRedis } from '../../src/infrastructure/redis/redis-client.js';
import { PrismaNotificationRepository } from '../../src/infrastructure/persistence/prisma-notification-repository.js';
import { listNotificationsSince } from '../../src/application/use-cases/notification-queries.js';

/**
 * Reconexión del socket con el adaptador de Redis y SQL Server reales (`.env`). No hay recuperación
 * de sesión: lo emitido durante el corte se pierde y el cliente lo recupera con `/since`.
 * Cliente mínimo del protocolo (engine.io v4 sobre WebSocket nativo) que se comporta como
 * socket.io-client: si el servidor le da `pid`, lo manda al reconectar con el último `offset`.
 */
const PATH = '/communication/socket.io';

class RawClient {
  readonly events: unknown[][] = [];
  pid: string | undefined;
  lastOffset: string | undefined;
  private ws: WebSocket | undefined;

  constructor(private readonly port: number) {}

  connect(auth: Record<string, unknown>): Promise<{ pid?: string }> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${this.port}${PATH}/?EIO=4&transport=websocket`);
      this.ws = ws;
      ws.onerror = () => reject(new Error('ws error'));
      ws.onmessage = (msg) => {
        const data = String(msg.data);
        if (data.startsWith('0')) {
          const withSession = this.pid ? { ...auth, pid: this.pid, offset: this.lastOffset } : auth;
          ws.send(`40${JSON.stringify(withSession)}`);
        } else if (data === '2') {
          ws.send('3');
        } else if (data.startsWith('40')) {
          const body = JSON.parse(data.slice(2)) as { pid?: string };
          if (body.pid) this.pid = body.pid;
          resolve(body);
        } else if (data.startsWith('44')) {
          reject(new Error(data));
        } else if (data.startsWith('42')) {
          const args = JSON.parse(data.slice(2)) as unknown[];
          const last = args[args.length - 1];
          if (this.pid && typeof last === 'string') this.lastOffset = last;
          this.events.push(args);
        }
      };
    });
  }

  /** Corte del transporte (no un "disconnect" de namespace): es el caso recuperable. */
  drop(): void {
    this.ws?.close();
  }
}

async function listen(server: HttpServer): Promise<number> {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  return (server.address() as AddressInfo).port;
}

function waitFor(predicate: () => boolean, timeoutMs = 5_000): Promise<void> {
  const started = Date.now();
  return new Promise((resolve, reject) => {
    const tick = () => {
      if (predicate()) return resolve();
      if (Date.now() - started > timeoutMs) return reject(new Error('timeout'));
      setTimeout(tick, 20);
    };
    tick();
  });
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

const prisma = new PrismaClient();
const notifications = new PrismaNotificationRepository(prisma);
const tenants: string[] = [];

function inboxRow(tenantId: string, userId: string, createdAtUtc: Date, id = randomUUID()) {
  return {
    Id: id,
    TenantId: tenantId,
    UserId: userId,
    Kind: 'billing.invoice_issued',
    Priority: 'Normal',
    Title: 'Invoice issued',
    Body: 'A new invoice is ready.',
    MetadataJson: '{}',
    Category: 'Billing',
    ParamsJson: '{}',
    SourceEventId: randomUUID(),
    SourceEventType: 'InvoiceIssued',
    CreatedAtUtc: createdAtUtc,
  };
}

afterAll(async () => {
  if (tenants.length > 0) await prisma.notificationEntry.deleteMany({ where: { TenantId: { in: tenants } } });
  await prisma.$disconnect();
});

describe('socket recovery', () => {
  let http: HttpServer;
  let io: CommunicationIoServer;
  let port: number;
  const recovered: boolean[] = [];
  let disconnects = 0;

  beforeAll(async () => {
    await connectRedis();
    http = createServer();
    io = buildSocketServer(http);
    io.on('connection', (s) => {
      recovered.push(s.recovered);
      s.on('disconnect', () => disconnects++);
    });
    port = await listen(http);
  });

  afterAll(async () => {
    io.close();
    await disconnectRedis();
  });

  it('a short drop is not recovered and /since returns what was missed', async () => {
    const tenantId = randomUUID();
    tenants.push(tenantId);
    const guestUserId = randomUUID();
    const ticket = await signJoinTicket({
      tenantId,
      guestUserId,
      guestLinkVersion: 1,
      meetingId: randomUUID(),
      displayName: 'Recovery test',
    });
    const room = `t:${tenantId}:u:${guestUserId}`;
    const client = new RawClient(port);

    const first = await client.connect({ ticket });
    expect(first.pid).toBeUndefined();
    io.to(room).emit('first', { n: 1 });
    await waitFor(() => client.events.length === 1);
    const lastSeenUtc = new Date();
    client.drop();
    await waitFor(() => disconnects === 1);

    // Durante el corte: llega una notificación y su evento en vivo se pierde.
    const missed = inboxRow(tenantId, guestUserId, new Date(lastSeenUtc.getTime() + 1));
    await prisma.notificationEntry.create({ data: missed });
    io.to(room).emit('notification.created', { id: missed.Id });
    await client.connect({ ticket });
    await sleep(500);

    expect(recovered).toEqual([false, false]);
    expect(client.events.map((e) => e[0])).toEqual(['first']);
    const since = await listNotificationsSince(
      { tenantId, userId: guestUserId, sinceUtc: lastSeenUtc, limit: 50 },
      { notifications },
    );
    expect(since.isSuccess && since.value.items.map((n) => n.id.toLowerCase())).toEqual([
      missed.Id.toLowerCase(),
    ]);
  });

  it('two rows in the same millisecond are both returned across pages', async () => {
    const tenantId = randomUUID();
    tenants.push(tenantId);
    const userId = randomUUID();
    const sameMs = new Date('2026-11-22T10:00:00.123Z');
    const ids = [randomUUID(), randomUUID()];
    await prisma.notificationEntry.createMany({
      data: ids.map((id) => inboxRow(tenantId, userId, sameMs, id)),
    });

    const seen: string[] = [];
    let cursor: { sinceUtc: Date; sinceId?: string } = { sinceUtc: new Date('2026-11-22T09:00:00.000Z') };
    for (let page = 0; page < 3; page++) {
      const result = await listNotificationsSince(
        { tenantId, userId, ...cursor, limit: 1 },
        { notifications },
      );
      if (!result.isSuccess || !result.value.nextCursorUtc || !result.value.nextCursorId) break;
      seen.push(...result.value.items.map((n) => n.id.toLowerCase()));
      cursor = { sinceUtc: new Date(result.value.nextCursorUtc), sinceId: result.value.nextCursorId };
    }

    expect(seen.sort()).toEqual(ids.map((id) => id.toLowerCase()).sort());
  });
});
