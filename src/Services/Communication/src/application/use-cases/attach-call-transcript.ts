import type { CallRepository } from '../ports/call-repository.js';
import { Result, makeError } from '../../domain/shared/result.js';

/**
 * Adjunta un transcript ya generado por el worker de transcripts (Fase 6,
 * proceso separado) a una llamada. A diferencia de attach-call-recording.ts
 * (disparado por un socket de usuario, con idempotencia por clientKey), esto
 * lo dispara `transcript-consumers.ts` al procesar
 * `communication.call.transcript_ready.v1` — la deduplicacion la da el
 * `ProcessedEventStore` (inbox) que `ConsumerRuntime` ya aplica a todo
 * handler registrado, no hace falta un segundo mecanismo.
 */
export interface AttachCallTranscriptCommand {
  readonly tenantId: string;
  readonly callId: string;
  readonly transcriptFileId: string;
}

export interface AttachCallTranscriptResult {
  readonly callId: string;
  readonly transcriptFileId: string;
}

export async function attachCallTranscript(
  cmd: AttachCallTranscriptCommand,
  deps: { calls: CallRepository },
): Promise<Result<AttachCallTranscriptResult>> {
  const call = await deps.calls.findById(cmd.tenantId, cmd.callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));

  const result = call.attachTranscript(cmd.transcriptFileId);
  if (!result.isSuccess) return Result.fail(result.error);

  await deps.calls.save(call);
  return Result.ok({ callId: call.id, transcriptFileId: cmd.transcriptFileId });
}
