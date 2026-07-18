import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallUpgradedToVideoEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallUpgradedToVideoDto } from '../../contracts/socket/call-socket-events.js';

export interface UpgradeCallToVideoCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
}

export interface UpgradeCallToVideoResult {
  readonly callId: string;
  readonly upgradedAtUtc: string;
}

export interface UpgradeCallToVideoDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

/**
 * Solo cambia `Call.kind` de Audio a Video en el aggregate; el signaling
 * WebRTC (renegociacion offer/answer con la nueva track) lo resuelve el
 * frontend. Idempotente por `clientKey` — dos toques rapidos del boton
 * "activar video" no publican dos eventos ni emiten dos veces al peer.
 */
export async function upgradeCallToVideo(
  cmd: UpgradeCallToVideoCommand,
  deps: UpgradeCallToVideoDeps,
): Promise<Result<UpgradeCallToVideoResult>> {
  const reservation = await deps.idempotency.tryReserve<UpgradeCallToVideoResult>({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.upgrade_to_video',
    clientKey: cmd.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: cmd.tenantId,
      userId: cmd.actorUserId,
      scope: 'call.upgrade_to_video',
      clientKey: cmd.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(cmd.tenantId, cmd.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const upgradeResult = call.upgradeToVideo({ actorUserId: cmd.actorUserId });
  if (!upgradeResult.isSuccess) {
    await release();
    return Result.fail(upgradeResult.error);
  }
  await deps.calls.save(call);

  const upgradedAtUtc = new Date().toISOString();
  const event: CallUpgradedToVideoEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.UpgradedToVideo,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: upgradedAtUtc,
    callId: cmd.callId,
    upgradedByUserId: cmd.actorUserId,
    upgradedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const dto: CallUpgradedToVideoDto = {
    callId: cmd.callId,
    upgradedByUserId: cmd.actorUserId,
    upgradedAtUtc,
  };
  deps.emitter.emitToCall({
    tenantId: cmd.tenantId,
    callId: cmd.callId,
    event: CallSocketEvents.UpgradedToVideo,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: upgradedAtUtc,
      payload: dto,
    },
  });

  const result: UpgradeCallToVideoResult = { callId: cmd.callId, upgradedAtUtc };
  await deps.idempotency.commit({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.upgrade_to_video',
    clientKey: cmd.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
