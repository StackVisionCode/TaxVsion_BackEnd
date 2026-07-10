/**
 * Envelope estandar de eventos Socket.IO en Communication. Se aplica a TODO
 * evento server → client para que el cliente pueda dedupear (`eventId`),
 * ordenar (`sequence` per-room cuando aplique) y trazar (`correlationId`).
 *
 * Cierre CRIT-10 del legacy: la idempotencia se hace por `eventId`, no por
 * heuristica de payload.
 */
export interface SocketEnvelope<TPayload> {
  readonly eventId: string;
  readonly correlationId: string;
  readonly emittedAtUtc: string;
  readonly sequence?: number;
  readonly payload: TPayload;
}

/**
 * Ack estandar para eventos client → server con Idempotency-Key.
 * Cliente puede reintentar mismo Idempotency-Key y recibir el mismo ack.
 */
export type SocketAck<TValue = void> =
  | { ok: true; value: TValue }
  | { ok: false; code: string; message: string };
