/**
 * SMS realtime. El microservicio SMS (.NET) publica eventos de resultado por mensaje
 * (Accepted/Delivered/Failed/Suppressed); acá se relayan a la sala STAFF del tenant (`t:{tenantId}:staff`)
 * para que el listado del módulo SMS (y la pestaña SMS del perfil del cliente) se actualice sin que el
 * usuario refresque — igual que `campaign.run.updated` y `wallet.updated`. El front re-consulta por HTTP;
 * el payload lleva solo lo mínimo para una posible actualización puntual.
 */

// ---------- Server -> Client ----------

export const SmsSocketEvents = {
  /** Cambió el estado de un mensaje SMS; el front refresca el listado + stats. */
  MessageUpdated: 'sms.message.updated',
} as const;

/** Payload de `sms.message.updated`. `status` resume el evento de origen que lo disparó. */
export interface SmsMessageUpdatedPayload {
  messageId: string;
  customerId: string;
  status: 'Accepted' | 'Delivered' | 'Failed' | 'Suppressed';
}
