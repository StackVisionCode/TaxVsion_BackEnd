import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { CallSignalDto } from '../../contracts/socket/call-socket-events.js';

/**
 * Relay de signaling WebRTC (offer/answer/ICE) al peer objetivo. El backend NO
 * parsea SDP/ICE — solo autoriza y despacha.
 *
 * Reglas:
 *   1. Ambos (from y target) deben ser participantes de la call.
 *   2. targetPeerUserId debe ser el "otro" participante del actor.
 *   3. La call no puede estar terminada.
 *
 * Cierra el bug legacy de signaling cross-tenant / cross-call (el legacy no
 * validaba que target fuera peer real del caller).
 */
export interface RelaySignalCommand {
  readonly tenantId: string;
  readonly callId: string;
  readonly fromUserId: string;
  readonly targetPeerUserId: string;
  readonly kind: 'offer' | 'answer' | 'ice';
  readonly data: Record<string, unknown>;
}

export interface RelaySignalResult {
  readonly targetUserId: string;
  readonly signal: CallSignalDto;
}

export async function relayCallSignal(
  command: RelaySignalCommand,
  deps: { calls: CallRepository },
): Promise<Result<RelaySignalResult>> {
  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));

  if (!call.isParticipant(command.fromUserId)) {
    return Result.fail(makeError('Call.Signal.NotParticipant', 'Only call participants can signal.'));
  }
  const expectedPeer = call.getPeerUserId(command.fromUserId);
  if (expectedPeer !== command.targetPeerUserId) {
    return Result.fail(
      makeError('Call.Signal.WrongTarget', 'Target peer is not the counterpart of this participant.'),
    );
  }
  if (call.status !== 'Accepted' && call.status !== 'Active') {
    return Result.fail(
      makeError('Call.Signal.InvalidState', `Signaling not allowed in status ${call.status}.`),
    );
  }

  return Result.ok({
    targetUserId: expectedPeer,
    signal: {
      callId: command.callId,
      fromPeerUserId: command.fromUserId,
      kind: command.kind,
      data: command.data,
    },
  });
}
