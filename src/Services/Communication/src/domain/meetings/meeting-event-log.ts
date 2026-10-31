/**
 * MeetingEventLog (INV-13 T-32, Revision C.3): todo lo que pasa dentro de una reunión (o llamada),
 * en orden (`seq`), con actor y objetivo. Es la fuente de la ficha del meeting, de la composición de
 * la grabación (F7), del transcript por hablante y de las métricas; absorbe el antiguo
 * `meeting.host.action` solo-log. Las acciones de configuración van a ConfigurationAudit, no aquí.
 */
export const MeetingEventType = {
  Started: 'meeting.started',
  Ended: 'meeting.ended',
  Cancelled: 'meeting.cancelled',
  Rescheduled: 'meeting.rescheduled',
  Expired: 'meeting.expired',
  Locked: 'meeting.locked',
  Unlocked: 'meeting.unlocked',
  MutedAll: 'meeting.muted_all',
  DurationWarning: 'meeting.duration_warning',
  ParticipantWaiting: 'participant.waiting',
  ParticipantJoined: 'participant.joined',
  ParticipantRejoined: 'participant.rejoined',
  ParticipantAdmitted: 'participant.admitted',
  ParticipantDenied: 'participant.denied',
  ParticipantLeft: 'participant.left',
  ParticipantForcedLeave: 'participant.forced_leave',
  ParticipantRemoved: 'participant.removed',
  MediaChanged: 'media.changed',
  HandRaised: 'hand.raised',
  HandLowered: 'hand.lowered',
  HandLoweredByHost: 'hand.lowered_by_host',
  HandLoweredAll: 'hand.lowered_all',
  ScreenStarted: 'screen.started',
  ScreenStopped: 'screen.stopped',
  HostTransferred: 'host.transferred',
  HostSucceeded: 'host.succeeded',
  CohostPromoted: 'cohost.promoted',
  CohostDemoted: 'cohost.demoted',
  TransportFailed: 'transport.failed',
  // F4
  QualityChanged: 'quality.changed',
  SfuScore: 'sfu.score',
  // F5: resumen por minuto de reacciones (D-50) y linea de tiempo de hablantes (T-69).
  ReactionSummary: 'reaction.summary',
  SpeakerStarted: 'speaker.started',
  SpeakerStopped: 'speaker.stopped',
  // F6: invitaciones (T-64) y recordatorio.
  InvitationCreated: 'invitation.created',
  InvitationUsed: 'invitation.used',
  InvitationRevoked: 'invitation.revoked',
  InvitationExpired: 'invitation.expired',
  ReminderSent: 'meeting.reminder_sent',
  MediaSettings: 'media.settings',
  // F7: ciclo de la grabacion (auditoria de cortes por limite, INV-12; metricas ADD-08).
  RecordingStarted: 'recording.started',
  RecordingStopped: 'recording.stopped',
  RecordingLimitWarning: 'recording.limit_warning',
  RecordingLimitReached: 'recording.limit_reached',
  RecordingReady: 'recording.ready',
  RecordingFailed: 'recording.failed',
} as const;
export type MeetingEventType = (typeof MeetingEventType)[keyof typeof MeetingEventType];

/**
 * Eventos que la ficha muestra como actividad (quién entró, salió, compartió, cambió de rol...). El resto
 * (voz, calidad, media, reacciones por minuto, invitaciones) es telemetría y se filtra en la consulta.
 */
export const MEETING_ACTIVITY_TYPES: readonly MeetingEventType[] = [
  MeetingEventType.Started,
  MeetingEventType.Ended,
  MeetingEventType.Locked,
  MeetingEventType.Unlocked,
  MeetingEventType.MutedAll,
  MeetingEventType.DurationWarning,
  MeetingEventType.ParticipantWaiting,
  MeetingEventType.ParticipantJoined,
  MeetingEventType.ParticipantRejoined,
  MeetingEventType.ParticipantAdmitted,
  MeetingEventType.ParticipantDenied,
  MeetingEventType.ParticipantLeft,
  MeetingEventType.ParticipantForcedLeave,
  MeetingEventType.ParticipantRemoved,
  MeetingEventType.HandRaised,
  MeetingEventType.ScreenStarted,
  MeetingEventType.ScreenStopped,
  MeetingEventType.HostTransferred,
  MeetingEventType.HostSucceeded,
  MeetingEventType.CohostPromoted,
  MeetingEventType.CohostDemoted,
  MeetingEventType.RecordingStarted,
  MeetingEventType.RecordingStopped,
  MeetingEventType.RecordingLimitReached,
];

export const MeetingEventScope = { Meeting: 'Meeting', Call: 'Call' } as const;
export type MeetingEventScope = (typeof MeetingEventScope)[keyof typeof MeetingEventScope];

export interface MeetingEventLogEntry {
  readonly id: string;
  readonly tenantId: string;
  readonly scope: MeetingEventScope;
  readonly scopeId: string;
  /** Orden dentro del scope; lo asigna el repositorio al guardar. */
  readonly seq: number;
  readonly at: Date;
  readonly type: MeetingEventType;
  readonly actorUserId: string | null;
  readonly targetUserId: string | null;
  readonly payload: Readonly<Record<string, unknown>> | null;
  readonly correlationId: string | null;
}

/** Lo que aporta quien registra; id, seq y at los completa el escritor. */
export type NewMeetingEvent = Omit<MeetingEventLogEntry, 'id' | 'seq' | 'at'> & { readonly at?: Date };
