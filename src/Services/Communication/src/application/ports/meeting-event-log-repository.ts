import type { MeetingEventLogEntry, MeetingEventScope } from '../../domain/meetings/meeting-event-log.js';

export interface MeetingEventLogRepository {
  /** Asigna el `seq` siguiente del scope y persiste; devuelve la fila completa. */
  append(entry: Omit<MeetingEventLogEntry, 'seq'>): Promise<MeetingEventLogEntry>;
  list(input: {
    tenantId: string;
    scope: MeetingEventScope;
    scopeId: string;
    /** Solo filas con seq > afterSeq (paginación incremental de la ficha). */
    afterSeq?: number;
    /** Solo estos tipos (la actividad de la ficha); ausente = todos. */
    types?: readonly string[];
    take: number;
  }): Promise<readonly MeetingEventLogEntry[]>;
}
