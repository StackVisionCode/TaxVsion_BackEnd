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

    const [readers, writers] = await Promise.all([
      deps.userPermissions.findActiveByTenantAndPermission(
        env.tenantId,
        ConnectorsPermissions.AccountsOfficeRead,
      ),
      deps.userPermissions.findActiveByTenantAndPermission(env.tenantId, ConnectorsPermissions.AccountsWrite),
    ]);
    const recipients = new Set([...readers, ...writers].map((user) => user.userId));
    for (const userId of recipients) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId,
        event: MailSocketEvents.IncomingEmail,
        envelope,
      });
    }
  });
}
