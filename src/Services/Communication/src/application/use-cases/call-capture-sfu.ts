import type { types as MediasoupTypes } from 'mediasoup';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingCaptureMode, RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { SfuMediaSource, SfuService, TransportInfo } from '../ports/sfu-service.js';
import type { IceServer, TurnCredentialFactory } from '../ports/turn-credential-factory.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';

/**
 * F7 (D-56): una llamada 1:1 grabada server-side sigue en mesh entre las partes y, ademas, cada parte
 * publica send-only al router propio de la llamada (id = callId) para que el recorder la capture.
 * Solo los participantes, y solo mientras la sesion este Recording con captura Server.
 */
export interface CallCaptureSfuDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly sfu: SfuService;
}

async function ensureCapturing(deps: CallCaptureSfuDeps, tenantId: string, callId: string, userId: string): Promise<Result<void>> {
  const call = await deps.calls.findById(tenantId, callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  if (!call.isParticipant(userId)) return Result.fail(makeError('Call.NotParticipant', 'You are not part of this call.'));
  const session = await deps.recordingSessions.findByScope(tenantId, RecordingScope.Call, callId);
  const s = session?.toSnapshot();
  if (!s || s.state !== 'Recording' || s.captureMode !== RecordingCaptureMode.Server) {
    return Result.fail(makeError('Call.Recording.NotCapturing', 'This call is not being recorded on the server.'));
  }
  return Result.okVoid();
}

export interface CallCaptureTransportResult extends TransportInfo {
  readonly iceServers: readonly IceServer[];
}

export async function createCallCaptureTransport(
  cmd: { tenantId: string; callId: string; userId: string },
  deps: CallCaptureSfuDeps & { turn: TurnCredentialFactory; effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined },
): Promise<Result<CallCaptureTransportResult>> {
  const guard = await ensureCapturing(deps, cmd.tenantId, cmd.callId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    // Mismo tope de subida que las reuniones SFU (politica de media en BD).
    const media = deps.effectiveSettings ? (await deps.effectiveSettings.resolve(cmd.tenantId)).media.sfu : null;
    const created = await deps.sfu.createTransport({
      tenantId: cmd.tenantId,
      meetingId: cmd.callId,
      userId: cmd.userId,
      direction: 'send',
      maxIncomingBitrateKbps: media?.maxIncomingBitrateKbps,
    });
    const { iceServers } = deps.turn.issue({ tenantId: cmd.tenantId, userId: cmd.userId, ttlSeconds: 3600 });
    return Result.ok({
      id: created.id,
      iceParameters: created.iceParameters,
      iceCandidates: created.iceCandidates,
      dtlsParameters: created.dtlsParameters,
      iceServers,
    });
  } catch (err) {
    return Result.fail(makeError('Call.Sfu.TransportFailed', (err as Error).message));
  }
}

export async function connectCallCaptureTransport(
  cmd: { tenantId: string; callId: string; userId: string; transportId: string; dtlsParameters: MediasoupTypes.DtlsParameters },
  deps: CallCaptureSfuDeps,
): Promise<Result<void>> {
  const guard = await ensureCapturing(deps, cmd.tenantId, cmd.callId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const ok = await deps.sfu.connectTransport({
      meetingId: cmd.callId,
      userId: cmd.userId,
      transportId: cmd.transportId,
      dtlsParameters: cmd.dtlsParameters,
    });
    return ok ? Result.okVoid() : Result.fail(makeError('Call.Sfu.TransportNotFound', 'Transport not found.'));
  } catch (err) {
    return Result.fail(makeError('Call.Sfu.ConnectFailed', (err as Error).message));
  }
}

export async function produceCallCapture(
  cmd: {
    tenantId: string;
    callId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
    source: SfuMediaSource;
  },
  deps: CallCaptureSfuDeps,
): Promise<Result<{ producerId: string }>> {
  const guard = await ensureCapturing(deps, cmd.tenantId, cmd.callId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const produced = await deps.sfu.produce({
      meetingId: cmd.callId,
      userId: cmd.userId,
      transportId: cmd.transportId,
      kind: cmd.kind,
      rtpParameters: cmd.rtpParameters,
      source: cmd.source,
    });
    return produced ? Result.ok(produced) : Result.fail(makeError('Call.Sfu.TransportNotFound', 'Send transport not found.'));
  } catch (err) {
    return Result.fail(makeError('Call.Sfu.ProduceFailed', (err as Error).message));
  }
}
