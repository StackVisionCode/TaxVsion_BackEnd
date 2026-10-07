import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { WalletSocketEvents } from '../../contracts/socket/wallet-socket-events.js';

/**
 * Relay realtime del cambio de saldo del monedero (servicio Wallet, .NET). `wallet.balance.changed.v1`
 * (recarga acreditada / reserva / liquidación) se traduce a un evento de socket `wallet.updated` hacia la
 * sala STAFF del tenant (`t:{tenantId}:staff`): el pill de saldo y el apartado Wallet del front se refrescan
 * sin que el usuario refresque. Payload vacío (el front re-consulta `GET /wallet`). No persiste nada.
 */
export function bindWalletConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { emitter: RealtimeEmitter },
): void {
  register('wallet.balance.changed.v1', async (env) => {
    deps.emitter.emitToTenantStaff({
      tenantId: env.tenantId,
      event: WalletSocketEvents.Updated,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: {},
      },
    });
  });
}
