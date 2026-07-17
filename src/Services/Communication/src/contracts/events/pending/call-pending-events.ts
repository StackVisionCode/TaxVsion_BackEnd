/**
 * Integration events de Call DECLARADOS pero AUN NO publicados por ningun
 * use case. Aislados aqui para que contracts/events/call-events.ts refleje
 * unicamente lo que el servicio realmente emite hoy.
 *
 * Historial de graduaciones:
 *  - Fase Backend 4: ciclo recording/consent (ConsentRequested/Recorded/
 *    Started/Stopped/ProcessingStarted).
 *  - Fase Backend 7: ScreenShareStarted/Stopped + UpgradedToVideo.
 *
 * A la fecha (2026-07-16) no queda ningun evento de call pendiente. Cuando
 * una fase futura declare uno nuevo (p.ej. multi-party call), agregarlo aca
 * hasta que un use case lo publique.
 */
export const PendingCallEventTypes = {} as const;
