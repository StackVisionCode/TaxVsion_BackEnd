import type { Result } from '../../domain/shared/result.js';
import { requestCallRecording, type RequestCallRecordingDeps, type RequestCallRecordingResult } from '../use-cases/request-call-recording.js';
import { requestMeetingRecording, type RequestMeetingRecordingDeps, type RequestMeetingRecordingResult } from '../use-cases/request-meeting-recording.js';
import { stopCallRecording } from '../use-cases/stop-call-recording.js';
import { stopMeetingRecording } from '../use-cases/stop-meeting-recording.js';
import { respondCallRecordingConsent } from '../use-cases/respond-call-recording-consent.js';
import { respondMeetingRecordingConsent } from '../use-cases/respond-meeting-recording-consent.js';
import { attachCallRecording } from '../use-cases/attach-call-recording.js';
import { attachMeetingRecording } from '../use-cases/attach-meeting-recording.js';
import type { MediaScope } from './connection-quality.service.js';

/**
 * Servicio de grabacion por Scope (ADD-11 T-76): los handlers de socket entran siempre por aqui y el
 * scope decide el agregado (`Call` o `Meeting`), que conserva sus reglas (consentimiento, politica,
 * topes del resolver). Las implementaciones por agregado siguen en `use-cases/*-recording.ts`.
 */
type Common = { tenantId: string; correlationId: string; clientKey: string; actorUserId: string };

type RequestDeps = RequestCallRecordingDeps & RequestMeetingRecordingDeps;
type StopDeps = Parameters<typeof stopCallRecording>[1] & Parameters<typeof stopMeetingRecording>[1];
type ConsentDeps = Parameters<typeof respondCallRecordingConsent>[1] & Parameters<typeof respondMeetingRecordingConsent>[1];
type AttachDeps = Parameters<typeof attachCallRecording>[1] & Parameters<typeof attachMeetingRecording>[1];

export function requestRecording(scope: 'Call', cmd: Common & { scopeId: string }, deps: RequestDeps): Promise<Result<RequestCallRecordingResult>>;
export function requestRecording(scope: 'Meeting', cmd: Common & { scopeId: string }, deps: RequestDeps): Promise<Result<RequestMeetingRecordingResult>>;
export function requestRecording(
  scope: MediaScope,
  cmd: Common & { scopeId: string },
  deps: RequestDeps,
): Promise<Result<RequestCallRecordingResult | RequestMeetingRecordingResult>> {
  const { scopeId, ...rest } = cmd;
  return scope === 'Call'
    ? requestCallRecording({ ...rest, callId: scopeId }, deps)
    : requestMeetingRecording({ ...rest, meetingId: scopeId }, deps);
}

export function stopRecording(scope: 'Call', cmd: Common & { scopeId: string }, deps: StopDeps): ReturnType<typeof stopCallRecording>;
export function stopRecording(scope: 'Meeting', cmd: Common & { scopeId: string }, deps: StopDeps): ReturnType<typeof stopMeetingRecording>;
export function stopRecording(scope: MediaScope, cmd: Common & { scopeId: string }, deps: StopDeps) {
  const { scopeId, ...rest } = cmd;
  return scope === 'Call' ? stopCallRecording({ ...rest, callId: scopeId }, deps) : stopMeetingRecording({ ...rest, meetingId: scopeId }, deps);
}

export function respondRecordingConsent(
  scope: MediaScope,
  cmd: Omit<Common, 'clientKey'> & { scopeId: string; response: Parameters<typeof respondCallRecordingConsent>[0]['response'] },
  deps: ConsentDeps,
) {
  const { scopeId, ...rest } = cmd;
  return scope === 'Call'
    ? respondCallRecordingConsent({ ...rest, callId: scopeId }, deps)
    : respondMeetingRecordingConsent({ ...rest, meetingId: scopeId }, deps);
}

export function attachRecording(scope: 'Call', cmd: Common & { scopeId: string; fileId: string }, deps: AttachDeps): ReturnType<typeof attachCallRecording>;
export function attachRecording(scope: 'Meeting', cmd: Common & { scopeId: string; fileId: string }, deps: AttachDeps): ReturnType<typeof attachMeetingRecording>;
export function attachRecording(scope: MediaScope, cmd: Common & { scopeId: string; fileId: string }, deps: AttachDeps) {
  const { scopeId, ...rest } = cmd;
  return scope === 'Call' ? attachCallRecording({ ...rest, callId: scopeId }, deps) : attachMeetingRecording({ ...rest, meetingId: scopeId }, deps);
}
