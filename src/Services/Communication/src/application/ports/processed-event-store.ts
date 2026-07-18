/**
 * Inbox durable — evita procesar el mismo evento dos veces si RabbitMQ redelivery
 * ocurre (por nack o crash). Guarda la marca por `EventId` una vez procesado.
 */
export interface ProcessedEventStore {
  /**
   * Intenta marcar el evento como procesado. Devuelve `true` si es fresh (el
   * consumer debe procesar); `false` si ya estaba procesado (skip).
   */
  tryMarkProcessed(input: {
    eventId: string;
    source: string;
    eventType: string;
    tenantId?: string | null;
  }): Promise<boolean>;

  /**
   * Elimina la marca de un evento previamente marcado como procesado. Se usa
   * cuando el handler explota tras haber tomado el lock inbox y el mensaje se
   * enrutara al DLQ: si en el futuro se reprocesa manualmente, no debe
   * saltarselo por "duplicado". Idempotente: no falla si la marca no existe.
   */
  unmark(input: { eventId: string; source: string }): Promise<void>;
}
