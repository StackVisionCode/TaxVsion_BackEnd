/**
 * Integration events de Meeting DECLARADOS pero AUN NO publicados por ningun
 * use case. Aislados aqui para que contracts/events/meeting-events.ts refleje
 * unicamente lo que el servicio realmente emite hoy.
 *
 * Historial de graduaciones:
 *  - Fase Backend 3: ciclo recording/consent (ConsentRequested/ConsentRecorded/
 *    Started/Stopped/ProcessingStarted).
 *  - Fase Backend 5: InvitationCreated.
 *  - Fase Backend 6: Cancelled, Rescheduled, ParticipantDenied,
 *    ParticipantRoleChanged (promote/demote cohost).
 *
 * A la fecha (2026-07-16) no queda ningun evento de meeting pendiente. Cuando
 * una fase futura declare uno nuevo (p.ej. `MeetingHostAudit` para exportar
 * el audit log estructurado como evento en vez de solo log), agregarlo aca
 * hasta que un use case lo publique.
 */
export const PendingMeetingEventTypes = {} as const;
