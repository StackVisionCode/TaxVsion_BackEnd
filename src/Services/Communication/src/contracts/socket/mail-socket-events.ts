/**
 * Mail realtime. El inbox de Correspondence es CUSTOMER-céntrico y COMPARTIDO por tenant, así que
 * el aviso de "llegó un correo entrante" se emite a todo el tenant (`t:{tenantId}`) y el módulo Mail
 * del front decide si recarga (según el cliente seleccionado). Payload mínimo — solo ids, sin asunto
 * ni cuerpo: no se filtra contenido y el front pide los datos por HTTP como siempre.
 */

// ---------- Server -> Client ----------

export interface MailIncomingEmailDto {
  customerId: string;
  emailThreadId: string;
  incomingEmailId: string;
}

export const MailSocketEvents = {
  /** Correspondence persistió un correo entrante de un cliente; el front recarga los hilos. */
  IncomingEmail: 'mail.incoming',
} as const;
