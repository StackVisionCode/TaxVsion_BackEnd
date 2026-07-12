/**
 * Puerto que representa el contrato de un evento entrante desde el bus.
 *
 * Vive en application/ports/ para que los event-handlers de application NO
 * dependan de la implementacion concreta del runtime (RabbitMQ) — la regla
 * "Ningun use case importa desde infrastructure/" del libro.
 *
 * El runtime concreto (`ConsumerRuntime`) en infrastructure/rabbit/ importa
 * este tipo y lo cumple; los handlers en application/event-handlers/ solo
 * conocen esta interfaz.
 */
export interface IncomingEnvelope {
  readonly eventId: string;
  readonly eventType: string;
  readonly tenantId: string;
  readonly correlationId?: string;
  readonly occurredOnUtc: string;
  readonly payload: Readonly<Record<string, unknown>>;
}

export type ConsumerHandler = (envelope: IncomingEnvelope) => Promise<void>;
