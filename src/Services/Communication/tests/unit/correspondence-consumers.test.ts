import { describe, expect, it, vi } from 'vitest';
import { bindCorrespondenceConsumers } from '../../src/application/event-handlers/correspondence-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { MailSocketEvents } from '../../src/contracts/socket/mail-socket-events.js';

/**
 * Contrato con Correspondence (.NET): CorrespondenceCustomerEmailReceivedIntegrationEvent serializa
 * PascalCase. El consumer solo debe hacer relay realtime al tenant — nada de persistencia.
 */
function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const emitter: RealtimeEmitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
  } as unknown as RealtimeEmitter;

  bindCorrespondenceConsumers(register, { emitter });
  return { handlers, emitter };
}

function envelope(payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType: 'correspondence.customer_email_received.v1',
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('bindCorrespondenceConsumers — relay realtime de correo entrante', () => {
  it('emite mail.incoming al tenant con los ids del correo entrante', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('correspondence.customer_email_received.v1')!(
      envelope({
        CustomerId: 'customer-1',
        IncomingEmailId: 'email-1',
        EmailThreadId: 'thread-1',
        Subject: 'Adjuntos de Pruebas',
      }),
    );

    expect(emitter.emitToTenant).toHaveBeenCalledTimes(1);
    const call = vi.mocked(emitter.emitToTenant).mock.calls[0]![0]!;
    expect(call.tenantId).toBe('tenant-1');
    expect(call.event).toBe(MailSocketEvents.IncomingEmail);
    expect(call.envelope.payload).toEqual({
      customerId: 'customer-1',
      emailThreadId: 'thread-1',
      incomingEmailId: 'email-1',
    });
  });

  it('no filtra el asunto ni el cuerpo en el payload del socket', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('correspondence.customer_email_received.v1')!(
      envelope({ CustomerId: 'c', IncomingEmailId: 'e', EmailThreadId: 't', Subject: 'secreto' }),
    );

    const payload = vi.mocked(emitter.emitToTenant).mock.calls[0]![0]!.envelope.payload as Record<string, unknown>;
    expect(payload).not.toHaveProperty('subject');
    expect(payload).not.toHaveProperty('Subject');
  });

  it('no emite nada si falta CustomerId o EmailThreadId', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('correspondence.customer_email_received.v1')!(
      envelope({ IncomingEmailId: 'email-1', Subject: 'x' }),
    );

    expect(emitter.emitToTenant).not.toHaveBeenCalled();
  });
});
