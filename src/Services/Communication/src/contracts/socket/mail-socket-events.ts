/**
 * Mail realtime. El inbox de Correspondence es CUSTOMER-céntrico y COMPARTIDO por tenant, así que
 * el aviso de "llegó un correo entrante" se emite a todo el tenant (`t:{tenantId}`) y el módulo Mail
 * del front decide si recarga (según el cliente seleccionado). Payload mínimo — solo ids, sin asunto
 * ni cuerpo: no se filtra contenido y el front pide los datos por HTTP como siempre.
 */

// ---------- Server -> Client ----------

export interface MailIncomingEmailDto {
  /** null = conversación con alguien que no es cliente (aviso de no entrega de un correo a un no cliente). */
  customerId: string | null;
  emailThreadId: string;
  /** Vacío cuando el aviso es de entrega (`kind = 'delivery_notice'`). */
  incomingEmailId: string;
  /** Ausente = correo entrante de un cliente. */
  kind?: 'delivery_notice';
  deliveryNoticeId?: string;
  /** Delayed (informativo, no sube ni cuenta como no leído) o NotDelivered. */
  outcome?: string;
  category?: string | null;
}

/** Estado visible de un hilo tras leerlo, archivarlo o mover un mensaje a la papelera (solo ids y contadores). */
export interface MailThreadStateDto {
  /** null = conversación sin cliente. */
  customerId: string | null;
  emailThreadId: string;
  status: string;
  unreadCount: number;
  messageCount: number;
  actorUserId: string | null;
}

export const MailSocketEvents = {
  /**
   * Correspondence persistió un correo entrante de un cliente, o un aviso de entrega de un correo enviado
   * desde el CRM (`kind = 'delivery_notice'`); el front recarga los hilos.
   */
  IncomingEmail: 'mail.incoming',
  /** Cambió el estado de un hilo; el front lo aplica en vivo (otras pestañas y empleados). */
  ThreadState: 'mail.thread_state',
} as const;
