import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { MailSocketEvents } from '../../contracts/socket/mail-socket-events.js';
import { readDotNetField } from '../../contracts/events/dotnet-payload.js';
import { ConnectorsPermissions } from '../../domain/shared/permissions.js';

const NormalizedPayloadSchema = z.object({
  customerId: z.string().uuid(),
  emailThreadId: z.string().uuid(),
  incomingEmailId: z.string().uuid().optional(),
  accountId: z.string().uuid().optional(),
  mailboxOwnerUserId: z.string().uuid().nullable().optional(),
});

const ThreadStatePayloadSchema = z.object({
  customerId: z.string().uuid(),
  emailThreadId: z.string().uuid(),
  status: z.string().min(1),
  unreadCount: z.number().int().nonnegative(),
  messageCount: z.number().int().nonnegative(),
  actorUserId: z.string().uuid().nullable().optional(),
});

export function parseCorrespondenceThreadStateChanged(payload: Record<string, unknown>) {
  return ThreadStatePayloadSchema.safeParse({
    customerId: readDotNetField(payload, 'CustomerId', 'customerId'),
    emailThreadId: readDotNetField(payload, 'EmailThreadId', 'emailThreadId'),
    status: readDotNetField(payload, 'Status', 'status'),
    unreadCount: readDotNetField(payload, 'UnreadCount', 'unreadCount'),
    messageCount: readDotNetField(payload, 'MessageCount', 'messageCount'),
    actorUserId: readDotNetField(payload, 'ActorUserId', 'actorUserId'),
  });
}

export function parseCorrespondenceCustomerEmailReceived(payload: Record<string, unknown>) {
  return NormalizedPayloadSchema.safeParse({
    customerId: readDotNetField(payload, 'CustomerId', 'customerId'),
    emailThreadId: readDotNetField(payload, 'EmailThreadId', 'emailThreadId'),
    incomingEmailId: readDotNetField(payload, 'IncomingEmailId', 'incomingEmailId'),
    accountId: readDotNetField(payload, 'AccountId', 'accountId'),
    mailboxOwnerUserId: readDotNetField(payload, 'MailboxOwnerUserId', 'mailboxOwnerUserId'),
  });
}

export function bindCorrespondenceConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    emitter: RealtimeEmitter;
    userPermissions: UserPermissionsProjectionRepository;
  },
): void {
  register('correspondence.customer_email_received.v1', async (env) => {
    const parsed = parseCorrespondenceCustomerEmailReceived(env.payload);
    if (!parsed.success || !parsed.data.accountId) return;

    const envelope = {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: new Date().toISOString(),
      payload: {
        customerId: parsed.data.customerId,
        emailThreadId: parsed.data.emailThreadId,
        incomingEmailId: parsed.data.incomingEmailId ?? '',
      },
    };

    if (parsed.data.mailboxOwnerUserId) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId: parsed.data.mailboxOwnerUserId,
        event: MailSocketEvents.IncomingEmail,
        envelope,
      });
      return;
    }

    for (const userId of await officeMailReaders(env.tenantId)) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId,
        event: MailSocketEvents.IncomingEmail,
        envelope,
      });
    }
  });

  // Correspondence no sabe de quién es cada buzón: el aviso va a quienes ven el buzón de oficina (que ven
  // todos) y a quien hizo el cambio (sus otras pestañas). El dueño de un buzón personal que no actuó lo
  // ve al recargar o con el próximo correo entrante.
  register('correspondence.thread_state_changed.v1', async (env) => {
    const parsed = parseCorrespondenceThreadStateChanged(env.payload);
    if (!parsed.success) return;

    const envelope = {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: new Date().toISOString(),
      payload: {
        customerId: parsed.data.customerId,
        emailThreadId: parsed.data.emailThreadId,
        status: parsed.data.status,
        unreadCount: parsed.data.unreadCount,
        messageCount: parsed.data.messageCount,
        actorUserId: parsed.data.actorUserId ?? null,
      },
    };

    const recipients = await officeMailReaders(env.tenantId);
    if (parsed.data.actorUserId) recipients.add(parsed.data.actorUserId);
    for (const userId of recipients) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId,
        event: MailSocketEvents.ThreadState,
        envelope,
      });
    }
  });

  /** Usuarios que ven el buzón de oficina (office.read o write): ven el correo de toda la oficina. */
  async function officeMailReaders(tenantId: string): Promise<Set<string>> {
    const [readers, writers] = await Promise.all([
      deps.userPermissions.findActiveByTenantAndPermission(tenantId, ConnectorsPermissions.AccountsOfficeRead),
      deps.userPermissions.findActiveByTenantAndPermission(tenantId, ConnectorsPermissions.AccountsWrite),
    ]);
    return new Set([...readers, ...writers].map((user) => user.userId));
  }
}
