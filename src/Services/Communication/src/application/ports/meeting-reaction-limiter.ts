/**
 * F5 (D-50): cupos de reacciones. Los topes vienen de la politica efectiva (BD); aqui solo se consume
 * la ventana. Un rechazo NO consume cupo (ventana fija de `consumeWithinLimit`).
 */
export interface MeetingReactionLimiter {
  /** true si el usuario todavia tiene cupo en su ventana de 10 s dentro de esa reunion. */
  consumeForUser(input: { tenantId: string; meetingId: string; userId: string; maxPer10s: number }): Promise<boolean>;
  /** true si la reunion todavia tiene cupo en su ventana de 60 s. */
  consumeForMeeting(input: { tenantId: string; meetingId: string; maxPerMinute: number }): Promise<boolean>;
}
