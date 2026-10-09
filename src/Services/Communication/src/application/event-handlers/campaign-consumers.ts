import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { CampaignSocketEvents } from '../../contracts/socket/campaign-socket-events.js';

/**
 * Relay realtime del avance de un envío de campaña (servicio Campaigns, .NET). Los tres eventos del run
 * —arranque (`campaign.run.started.v1`), resultado de cada unidad (`campaign.dispatch.result.v1`) y cierre
 * (`campaign.run.completed.v1`)— se traducen a UN solo evento de socket `campaign.run.updated` hacia la sala
 * STAFF del tenant (`t:{tenantId}:staff`): el front recarga los runs y actualiza "enviado / no enviado" sin
 * refrescar a mano. Payload mínimo (solo ids); el front pide los datos por HTTP. No persiste nada.
 */
export function bindCampaignConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { emitter: RealtimeEmitter },
): void {
  const relay = (env: IncomingEnvelope): void => {
    const campaignId = getString(env.payload, 'campaignId') ?? getString(env.payload, 'CampaignId');
    const runId = getString(env.payload, 'runId') ?? getString(env.payload, 'RunId');
    if (!campaignId || !runId) return;

    // Solo personal: las campañas son una herramienta de la oficina (no de clientes del portal).
    deps.emitter.emitToTenantStaff({
      tenantId: env.tenantId,
      event: CampaignSocketEvents.RunUpdated,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { campaignId, runId },
      },
    });
  };

  register('campaign.run.started.v1', async (env) => relay(env));
  register('campaign.dispatch.result.v1', async (env) => relay(env));
  register('campaign.run.completed.v1', async (env) => relay(env));
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
