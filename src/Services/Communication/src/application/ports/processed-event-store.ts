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
}
