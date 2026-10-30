import type { IntegrationEvent, RecordingConsentResponse, RecordingConsentSnapshotEntry } from './integration-event.js';

/**
 * Integration events del ciclo de vida de un meeting REALMENTE publicados hoy
 * por algun use case (verificado por grep en application/). Los eventos que
 * todavia no se publican (Cancelled, Rescheduled, ParticipantDenied) viven en
 * ./pending/meeting-pending-events.ts junto a la fase del plan Backend que
 * los activa — no los reintroduzcas aqui hasta que un use case los publique.
 *
 * El ciclo de recording/consent (ConsentRequested/ConsentRecorded/Started/
 * Stopped/ProcessingStarted) se graduo desde pending/ en Fase Backend 3 —
 * ahora los publican request/respond/start/stop-meeting-recording.ts y
 * attach-meeting-recording.ts. InvitationCreated se graduo en Fase Backend 5
 * — lo publica create-meeting-invitations.ts.
 *
 * Consumidores: Notification (invitaciones, cancelaciones), Planner (sync de slots),
 * Analytics (participant-minutes para facturación), Compliance (audit de grabación).
 */
export const MeetingEventTypes = {
  Scheduled:               'communication.meeting.scheduled.v1',
  Started:                 'communication.meeting.started.v1',
  Ended:                   'communication.meeting.ended.v1',
  Locked:                  'communication.meeting.locked.v1',
  Unlocked:                'communication.meeting.unlocked.v1',
  HostTransferred:         'communication.meeting.host_transferred.v1',
  ParticipantJoined:       'communication.meeting.participant_joined.v1',
  ParticipantLeft:         'communication.meeting.participant_left.v1',
  ParticipantAdmitted:     'communication.meeting.participant_admitted.v1',
  ParticipantRemovedByHost: 'communication.meeting.participant_removed_by_host.v1',
  // F4: simetricos a los de calls (INV-04 §13.4) y resumen de calidad al terminar (INV-03 §13.6, ADD-08).
  ScreenShareStarted: 'communication.meeting.screen_share_started.v1',
  ScreenShareStopped: 'communication.meeting.screen_share_stopped.v1',
  QualitySummary: 'communication.meeting.quality_summary.v1',
  RecordingConsentRequested: 'communication.meeting.recording_consent_requested.v1',
  RecordingConsentRecorded: 'communication.meeting.recording_consent_recorded.v1',
  RecordingStarted:        'communication.meeting.recording_started.v1',
  RecordingStopped:        'communication.meeting.recording_stopped.v1',
  RecordingProcessingStarted: 'communication.meeting.recording_processing_started.v1',
  RecordingReady:          'communication.meeting.recording_ready.v1',
  TranscriptReady:         'communication.meeting.transcript_ready.v1',
  InvitationCreated:       'communication.meeting.invitation_created.v1',
  Cancelled:               'communication.meeting.cancelled.v1',
  Rescheduled:             'communication.meeting.rescheduled.v1',
  ParticipantDenied:       'communication.meeting.participant_denied.v1',
  // Calendar agendo una cita virtual y aca nacio su sala. Va aparte del `scheduled` generico porque
  // lleva el id de la cita: es lo unico que le permite a Calendar guardar el codigo corto.
  LinkedToAppointment:     'communication.meeting.linked_to_appointment.v1',
  ParticipantRoleChanged:  'communication.meeting.participant_role_changed.v1',
  RecordingValidationFailed: 'communication.meeting.recording_validation_failed.v1',
  RecordingFailed:         'communication.meeting.recording_failed.v1',
  // F1 — un participante Joined volvio a entrar con media nueva (recarga, otra pestaña).
  ParticipantRejoined:     'communication.meeting.participant_rejoined.v1',
  // F1 — un transport del SFU llego a ICE/DTLS failed: base del MeetingEventLog (F3) y analytics (F10).
  TransportFailed:         'communication.meeting.transport_failed.v1',
  // F3 — ciclo de vida: aviso 10/5/1 antes del corte, agendada vencida, salida forzada por el sistema.
  DurationWarning:         'communication.meeting.duration_warning.v1',
  Expired:                 'communication.meeting.expired.v1',
  ParticipantForcedLeave:  'communication.meeting.participant_forced_leave.v1',
  // F6 — invitaciones (T-64) y recordatorio.
  InvitationUsed:          'communication.meeting.invitation_used.v1',
  InvitationRevoked:       'communication.meeting.invitation_revoked.v1',
  InvitationExpired:       'communication.meeting.invitation_expired.v1',
  Reminder:                'communication.meeting.reminder.v1',
} as const;

/** Motivo del fin de una reunión (espejo de MeetingEndReason del dominio). */
export type MeetingEndReasonValue = 'Host' | 'MaxDurationReached' | 'NoParticipants' | 'Offboarding' | 'Backfill';

/** Por qué un participante salió sin pedirlo: venció su gracia, le revocaron la sesión, venció su token, lo retiraron o terminó la reunión. */
export type ForcedLeaveReason = 'Grace' | 'Revoked' | 'Expired' | 'Offboarding' | 'MeetingEnded';

/** @since F3 — meeting-lifecycle-scheduler: una vez por umbral (600/300/60 s). */
export interface MeetingDurationWarningEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.duration_warning.v1';
  readonly meetingId: string;
  readonly remainingSeconds: number;
  readonly endsAtUtc: string;
}

/** @since F3 — agendada que nunca se inició y venció (D-35), por el scheduler o el job de limpieza. */
export interface MeetingExpiredEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.expired.v1';
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly scheduledForUtc: string | null;
  readonly expiredAtUtc: string;
}

/** @since F3 — force-leave-meeting.ts. Se publica además del `participant_left` normal. */
export interface MeetingParticipantForcedLeaveEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_forced_leave.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly reason: ForcedLeaveReason;
  readonly leftAtUtc: string;
}

/** @since F1 — join-meeting.ts cuando `Meeting.requestJoin` renueva la sesion de un participante Joined. */
export interface MeetingParticipantRejoinedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_rejoined.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  /** joinOrder nuevo: los peers rearman su PeerConnection cuando ven uno mayor. */
  readonly joinOrder: number;
  readonly rejoinedAtUtc: string;
}

/** @since F1 — sfu-transport-observer.ts. Solo se publica en `failed` (no en cada transicion). */
export interface MeetingTransportFailedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.transport_failed.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly direction: 'send' | 'recv';
  readonly transportId: string;
  readonly layer: 'ice' | 'dtls';
  readonly failedAtUtc: string;
}

export interface MeetingScheduledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.scheduled.v1';
  readonly meetingId: string;
  readonly title: string;
  readonly hostUserId: string;
  readonly scheduledForUtc: string | null;
  readonly shortCode: string;
}

/**
 * La sala de una cita virtual quedo creada. Calendar lo consume para guardar el codigo corto y poder
 * mostrar el link; sin el id de la cita no sabria a cual pertenece.
 */
export interface MeetingLinkedToAppointmentEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.linked_to_appointment.v1';
  readonly appointmentId: string;
  readonly meetingId: string;
  readonly shortCode: string;
  readonly scheduledForUtc: string | null;
}

export interface MeetingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.started.v1';
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly startedAtUtc: string;
}

export interface MeetingEndedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.ended.v1';
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly endedAtUtc: string;
  readonly durationSeconds: number;
  readonly participantCount: number;
  readonly recordingFileId: string | null;
  /** @since F3 — Host (End explícito) o el motivo del sistema. */
  readonly reason: MeetingEndReasonValue;
}

export interface MeetingLockedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.locked.v1';
  readonly meetingId: string;
  readonly lockedByUserId: string;
  readonly lockedAtUtc: string;
}

export interface MeetingUnlockedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.unlocked.v1';
  readonly meetingId: string;
  readonly unlockedByUserId: string;
  readonly unlockedAtUtc: string;
}

export interface MeetingHostTransferredEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.host_transferred.v1';
  readonly meetingId: string;
  readonly previousHostUserId: string;
  readonly newHostUserId: string;
  readonly transferredAtUtc: string;
  /** @since F3 — Transfer (el host lo cedió), Succession (venció su gracia, D-08) u Offboarding (consumer). */
  readonly reason?: 'Transfer' | 'Succession' | 'Offboarding';
}

export interface MeetingParticipantJoinedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_joined.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly joinedAtUtc: string;
}

export interface MeetingParticipantLeftEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_left.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly joinedAtUtc: string;
  readonly leftAtUtc: string;
  readonly durationSeconds: number;
}

export interface MeetingParticipantAdmittedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_admitted.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly admittedByUserId: string;
  readonly admittedAtUtc: string;
}

export interface MeetingParticipantRemovedByHostEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_removed_by_host.v1';
  readonly meetingId: string;
  readonly removedParticipantUserId: string;
  readonly removedByUserId: string;
  readonly removedAtUtc: string;
}

export interface MeetingScreenShareStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.screen_share_started.v1';
  readonly meetingId: string;
  readonly sharingUserId: string;
  readonly startedAtUtc: string;
}

export interface MeetingScreenShareStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.screen_share_stopped.v1';
  readonly meetingId: string;
  readonly sharingUserId: string;
  readonly stoppedAtUtc: string;
}

/** Agregado por reunion al terminar (F4 → ADD-08): niveles reportados y scores del SFU. */
export interface MeetingQualitySummaryEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.quality_summary.v1';
  readonly meetingId: string;
  readonly participants: number;
  /** Conteo de reportes por nivel (Excellent/Good/Fair/Poor/Disconnected). */
  readonly levels: Record<string, number>;
  readonly relayReports: number;
  readonly sfuScoreSamples: number;
  readonly sfuScoreAvg: number | null;
  readonly transportFailures: number;
  readonly endedAtUtc: string;
}

export interface MeetingRecordingReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_ready.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly durationSeconds: number;
  readonly participantCount: number;
  readonly readyAtUtc: string;
  /** Fase 1B — antes ausente; Notification lo usaba como recipient simbolico `meeting:{id}`. */
  readonly hostUserId: string;
  /**
   * Quien acepto/rechazo grabar. Presente cuando el meeting paso por el
   * flujo de consent (Fase Backend 3, publicado desde el consumer de
   * transcript_ready); ausente en el path legacy de attach-meeting-recording.ts
   * (meetings sin RecordingSession — nunca pidieron consentimiento).
   */
  readonly consentSnapshot?: readonly RecordingConsentSnapshotEntry[];
}

/** @since Fase Backend 3 — request-meeting-recording.ts */
export interface MeetingRecordingConsentRequestedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_consent_requested.v1';
  readonly meetingId: string;
  readonly requestedByUserId: string;
  readonly participants: readonly string[];
  readonly requestedAtUtc: string;
}

/** @since Fase Backend 3 — respond-meeting-recording-consent.ts */
export interface MeetingRecordingConsentRecordedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_consent_recorded.v1';
  readonly meetingId: string;
  readonly userId: string;
  readonly response: RecordingConsentResponse;
  readonly respondedAtUtc: string;
}

/** @since Fase Backend 3 — start-meeting-recording.ts (auto-invocado desde respond o desde el timeout scheduler) */
export interface MeetingRecordingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_started.v1';
  readonly meetingId: string;
  readonly startedByUserId: string;
  readonly startedAtUtc: string;
  readonly consentSnapshot: readonly RecordingConsentSnapshotEntry[];
}

/** @since Fase Backend 3 — stop-meeting-recording.ts */
export interface MeetingRecordingStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_stopped.v1';
  readonly meetingId: string;
  readonly stoppedByUserId: string;
  readonly stoppedAtUtc: string;
  readonly elapsedSeconds: number;
}

/**
 * Publicado cuando attach-meeting-recording.ts recibe el fileId subido y
 * transiciona RecordingSession a Processing — distinto de RecordingReady
 * (que llega despues, cuando el transcript worker confirma).
 * @since Fase Backend 3 — attach-meeting-recording.ts (transicion Processing)
 */
export interface MeetingRecordingProcessingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_processing_started.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly startedAtUtc: string;
}

/** Ver docblock de CallTranscriptReadyEvent — mismo flujo, para meetings. */
export interface MeetingTranscriptReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.transcript_ready.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly transcriptFileId: string;
  readonly detectedLanguage: string | null;
  /** @since Fase Transcript 5 — derivado por el worker de los timestamps de whisper.cpp. */
  readonly durationSeconds: number;
  /** @since Fase Transcript 5 — conteo de palabras del transcript ya limpio (sin timestamps). */
  readonly wordCount: number;
  readonly readyAtUtc: string;
}

/**
 * @since Fase Backend 6 — cancel-meeting.ts. Notification consume esto para
 * mandar el mail "el meeting fue cancelado" a cada participante + a los
 * invitados por link (recuperados via listInvitationsByMeeting).
 */
export interface MeetingCancelledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.cancelled.v1';
  readonly meetingId: string;
  readonly cancelledByUserId: string;
  readonly cancelledAtUtc: string;
  /** IDs de participantes ya registrados (Host + Cohost + Attendee, cualquier status). */
  readonly participantUserIds: readonly string[];
  /** F6: solo externos sin cuenta (los de cuenta reciben in-app por `inviteeUserIds`). */
  readonly invitedEmails: readonly string[];
  /** Motivo opcional escrito por el host, se puede incluir en el mail al participante. */
  readonly reason: string | null;
  /** F6 (T-64): datos para el email y el `.ics` de cancelacion. */
  readonly meetingTitle: string;
  readonly shortCode: string;
  readonly scheduledForUtc: string | null;
  readonly durationMinutes: number | null;
  readonly hostUserId: string;
  readonly hostDisplayName: string;
  readonly timeZone: string | null;
  readonly inviteeUserIds: readonly string[];
}

/** @since Fase Backend 6 — reschedule-meeting.ts */
export interface MeetingRescheduledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.rescheduled.v1';
  readonly meetingId: string;
  readonly rescheduledByUserId: string;
  readonly previousScheduledForUtc: string | null;
  readonly newScheduledForUtc: string | null;
  readonly rescheduledAtUtc: string;
  readonly participantUserIds: readonly string[];
  /** F6: solo externos sin cuenta. */
  readonly invitedEmails: readonly string[];
  /** F6 (T-64): datos para el email y el `.ics` actualizado. */
  readonly meetingTitle: string;
  readonly shortCode: string;
  readonly durationMinutes: number | null;
  readonly hostUserId: string;
  readonly hostDisplayName: string;
  readonly timeZone: string | null;
  readonly inviteeUserIds: readonly string[];
}

/**
 * @since Fase Backend 6 — deny-waiting-room-participant.ts. Distinto de
 * ParticipantRemovedByHost: este evento solo se emite cuando el host rechaza
 * una solicitud pendiente (participant status='Waiting'), no cuando saca a
 * alguien ya adentro.
 */
export interface MeetingParticipantDeniedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_denied.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly deniedByUserId: string;
  readonly deniedAtUtc: string;
}

/**
 * @since Fase Backend 6 — promote-participant-to-cohost.ts / demote-cohost-to-attendee.ts.
 * Un solo tipo para ambas direcciones (previousRole/newRole distinguen), asi
 * un consumer que quiera "cualquier cambio de rol" no tiene que suscribirse a dos.
 */
export interface MeetingParticipantRoleChangedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_role_changed.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly changedByUserId: string;
  readonly previousRole: 'Host' | 'Cohost' | 'Attendee';
  readonly newRole: 'Host' | 'Cohost' | 'Attendee';
  readonly changedAtUtc: string;
}

/**
 * @since Fase Backend 8 — bug #245. Publicado por el transcript worker cuando
 * ffprobe detecta que el file subido no tiene track de audio (o el file
 * esta corrupto y ffprobe falla). Consumer en transcript-consumers.ts
 * transiciona la RecordingSession a Failed con FailureReason='NoAudioStream'
 * — el frontend puede mostrar un mensaje especifico ("tu microfono estaba
 * mudo") en vez del generico "grabacion fallida".
 */
export interface MeetingRecordingValidationFailedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_validation_failed.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly failureReason: string;
  readonly detectedAtUtc: string;
}

/**
 * @since Fase Backend 10. Publicado en TODOS los caminos que transicionan una
 * RecordingSession a `Failed` — a diferencia de RecordingValidationFailedEvent
 * (especifico de "no audio track", bug #245, publicado por el worker ANTES de
 * fallar la session), este cubre el resto: ConsentTimeout (nadie acepto
 * grabar a tiempo, recording-consent-timeout-scheduler.ts) y TranscriptFailed
 * (ffmpeg/whisper fallaron post-attach, transcript-consumers.ts). Existe para
 * que Notification pueda avisar "tu grabacion fallo" sin importar la causa, y
 * Analytics/Audit puedan trackear la tasa de fallos de grabacion — ninguno de
 * los dos tenia como enterarse hoy salvo por el socket `state_changed:Failed`
 * (efimero, solo llega a quien sigue conectado al room).
 */
export interface MeetingRecordingFailedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_failed.v1';
  readonly meetingId: string;
  readonly reason: string;
  readonly failedAtUtc: string;
  /** Fase 1B — antes ausente; Notification lo usaba como recipient simbolico `meeting:{id}`. */
  readonly hostUserId: string;
}

/**
 * Reemplaza al viejo MeetingInvitationRequestedEvent (planos inviteeEmail/
 * inviteeUserId) con el shape real: tokenHash (nunca el token plano) +
 * joinUrl ya resuelto + inviteeKind para distinguir Employee/Customer/External.
 * Notification lo consume para el email/log de invitacion (stub in-app hasta
 * que exista envio real de correo).
 * @since Fase Backend 5 — create-meeting-invitations.ts
 */
export interface MeetingInvitationCreatedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_created.v1';
  readonly invitationId: string;
  readonly meetingId: string;
  readonly inviteeKind: 'Employee' | 'Customer' | 'External';
  readonly inviteeUserId: string | null;
  readonly inviteeEmail: string | null;
  readonly inviteeName: string | null;
  readonly tokenHash: string;
  readonly expiresAtUtc: string;
  readonly joinUrl: string;
  /** F6 (T-64): email completo (titulo, hora local, duracion, host, codigo) y `.ics`. */
  readonly meetingTitle: string;
  readonly meetingStatus: 'Scheduled' | 'Live';
  readonly shortCode: string;
  readonly scheduledForUtc: string | null;
  readonly durationMinutes: number | null;
  readonly hostUserId: string;
  readonly hostDisplayName: string;
  readonly timeZone: string | null;
  readonly invitedByUserId: string;
  readonly invitedByDisplayName: string;
}

/** @since F6 — join-meeting.ts: un invitado con cuenta (reutilizable) o un guest (unico) uso su token. */
export interface MeetingInvitationUsedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_used.v1';
  readonly invitationId: string;
  readonly meetingId: string;
  readonly inviteeKind: 'Employee' | 'Customer' | 'External';
  readonly inviteeUserId: string | null;
  readonly usedByUserId: string;
  readonly usedAtUtc: string;
  readonly useCount: number;
}

/** @since F6 — revoke-meeting-invitation.ts; `ejected` = el guest estaba dentro y se le saco. */
export interface MeetingInvitationRevokedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_revoked.v1';
  readonly invitationId: string;
  readonly meetingId: string;
  readonly meetingTitle: string;
  readonly inviteeKind: 'Employee' | 'Customer' | 'External';
  readonly inviteeUserId: string | null;
  readonly inviteeEmail: string | null;
  readonly revokedByUserId: string;
  readonly revokedAtUtc: string;
  readonly ejected: boolean;
}

/** @since F6 — close-meeting-invitations.ts: la reunion termino/se cancelo/expiro con la invitacion abierta. */
export interface MeetingInvitationExpiredEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_expired.v1';
  readonly invitationId: string;
  readonly meetingId: string;
  readonly inviteeKind: 'Employee' | 'Customer' | 'External';
  readonly inviteeUserId: string | null;
  readonly inviteeEmail: string | null;
  readonly reason: 'Ended' | 'Cancelled' | 'Expired';
  readonly closedAtUtc: string;
}

/** @since F6 (T-64) — meeting-lifecycle-sweep.ts: T-N minutos antes de una reunion agendada, una sola vez. */
export interface MeetingReminderEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.reminder.v1';
  readonly meetingId: string;
  readonly meetingTitle: string;
  readonly shortCode: string;
  readonly scheduledForUtc: string;
  readonly durationMinutes: number | null;
  readonly minutesBefore: number;
  readonly hostUserId: string;
  readonly hostDisplayName: string;
  readonly timeZone: string | null;
  readonly participantUserIds: readonly string[];
  readonly inviteeUserIds: readonly string[];
  /** Externos sin cuenta: el email les recuerda usar el enlace de su invitacion y el codigo. */
  readonly invitedEmails: readonly string[];
  readonly staffJoinUrl: string;
  readonly portalJoinUrl: string;
}
