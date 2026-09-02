import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { MailSocketEvents } from '../../contracts/socket/mail-socket-events.js';

/**
 * Correspondence persistió un correo ENTRANTE que matcheó a un cliente del tenant
 * (`correspondence.customer_email_received.v1`). El inbox es compartido por tenant, así que se
 * avisa a TODO el tenant (`t:{tenantId}`) para que el módulo Mail del front recargue los hilos sin
 * recargar la página. Payload mínimo (solo ids) — sin asunto ni cuerpo: no se filtra contenido, y el
 * front pide los datos por HTTP como siempre. No persiste nada: es puro relay realtime.
 */
export function bindCorrespondenceConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { emitter: RealtimeEmitter },
): void {
  register('correspondence.customer_email_received.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const emailThreadId =
      getString(env.payload, 'emailThreadId') ?? getString(env.payload, 'EmailThreadId');
    const incomingEmailId =
      getString(env.payload, 'incomingEmailId') ?? getString(env.payload, 'IncomingEmailId');
    if (!customerId || !emailThreadId) return;

    deps.emitter.emitToTenant({
      tenantId: env.tenantId,
      event: MailSocketEvents.IncomingEmail,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { customerId, emailThreadId, incomingEmailId: incomingEmailId ?? '' },
      },
    });
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
