/**
 * Idempotency store — cierra CRIT-10 del legacy. La API es:
 *   1) `tryReserve` intenta ocupar (tenant, user, scope, clientKey) con TTL.
 *   2) Si ya existia una reserva completa (con payload), devuelve el payload
 *      anterior — el cliente recibe el mismo ack sin re-ejecutar.
 *   3) `commit` guarda el resultado tras exito.
 *
 * La implementacion combina Redis (SET NX PX) + tabla IdempotencyRecord para
 * cross-pod safety. Redis es la fast lane; la tabla es la persistencia
 * autoritativa para replays fuera de la ventana de Redis.
 */
export type IdempotencyReservation<T> =
  | { readonly status: 'fresh'; readonly token: string }
  | { readonly status: 'replay'; readonly payload: T };

export interface IdempotencyStore {
  tryReserve<T>(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    ttlSeconds: number;
  }): Promise<IdempotencyReservation<T>>;

  commit<T>(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    payload: T;
    token: string;
  }): Promise<void>;

  release(input: {
    tenantId: string;
    userId: string;
    scope: string;
    clientKey: string;
    token: string;
  }): Promise<void>;
}
