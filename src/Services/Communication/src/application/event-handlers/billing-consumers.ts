import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import {
  BillingSocketEvents,
  type InvoiceUpdatedPayload,
} from '../../contracts/socket/billing-socket-events.js';

/**
 * Relay realtime del cambio de estado de una factura (servicio Billing, .NET). `billing.invoice.status_changed.v1`
 * (emisión, cobro, anulación, edición o corrección manual) se traduce a un evento de socket `invoice.updated` hacia la
 * sala STAFF del tenant (`t:{tenantId}:staff`): la lista de facturas se actualiza sin que nadie recargue. El payload es
 * mínimo (id, número, estados, disparador); el front re-consulta por HTTP. No persiste nada.
 */
export function bindBillingConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { emitter: RealtimeEmitter },
): void {
  const relay = async (env: IncomingEnvelope): Promise<void> => {
    const body = env.payload ?? {};
    const invoiceId = pick(body, 'invoiceId');
    const status = pick(body, 'toStatus');
    // Sin factura o sin estado no hay nada que refrescar: se descarta en silencio, no es un error del mensaje.
    if (!invoiceId || !status) {
      return;
    }

    const payload: InvoiceUpdatedPayload = {
      invoiceId,
      invoiceNumber: pick(body, 'invoiceNumber'),
      fromStatus: pick(body, 'fromStatus'),
      status,
      trigger: pick(body, 'trigger') ?? 'Unknown',
    };

    deps.emitter.emitToTenantStaff({
      tenantId: env.tenantId,
      event: BillingSocketEvents.InvoiceUpdated,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload,
      },
    });
  };

  // El nombre llega como `eventType` del cuerpo o, si no, como el alias del header: se registran los dos.
  register('billing.invoice.status_changed', relay);
  register('billing.invoice.status_changed.v1', relay);
}

/** Lee un campo en camelCase o PascalCase (según cómo lo serialice el publicador .NET). */
function pick(source: Readonly<Record<string, unknown>>, key: string): string | null {
  const pascal = key.charAt(0).toUpperCase() + key.slice(1);
  const value = source[key] ?? source[pascal];
  return typeof value === 'string' && value.length > 0 ? value : null;
}
