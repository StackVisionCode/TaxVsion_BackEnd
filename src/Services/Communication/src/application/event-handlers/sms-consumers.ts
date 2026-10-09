import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { SmsSocketEvents, type SmsMessageUpdatedPayload } from '../../contracts/socket/sms-socket-events.js';

/**
 * Relay realtime del resultado de un mensaje SMS (servicio SMS, .NET). Los cuatro eventos por mensaje
 * —Accepted/Delivered/Failed/Suppressed (ver `SmsIntegrationEvents.cs`)— se traducen a UN solo evento de
 * socket `sms.message.updated` hacia la sala STAFF del tenant (`t:{tenantId}:staff`): el listado del módulo
 * SMS (y la pestaña SMS del perfil) actualiza "enviado / entregado / no entregado" sin refrescar a mano,
 * igual que `campaign.run.updated`. Payload mínimo (ids + estado); el front re-consulta por HTTP. No persiste.
 */
export function bindSmsConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { emitter: RealtimeEmitter },
): void {
  const relay = (env: IncomingEnvelope, status: SmsMessageUpdatedPayload['status']): void => {
    const messageId = getString(env.payload, 'messageId') ?? getString(env.payload, 'MessageId');
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!messageId || !customerId) return;

    // Solo personal: el módulo SMS es una herramienta de la oficina (no de clientes del portal).
    deps.emitter.emitToTenantStaff({
      tenantId: env.tenantId,
      event: SmsSocketEvents.MessageUpdated,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { messageId, customerId, status } satisfies SmsMessageUpdatedPayload,
      },
    });
  };

  register('sms.message.accepted.v1', async (env) => relay(env, 'Accepted'));
  register('sms.message.delivered.v1', async (env) => relay(env, 'Delivered'));
  register('sms.message.failed.v1', async (env) => relay(env, 'Failed'));
  register('sms.message.suppressed.v1', async (env) => relay(env, 'Suppressed'));
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
