/**
 * Kind del mensaje. Attachment cubre imagen/voice/video/documento/screenshot;
 * la distincion visual la hace el mime en CloudStorage, no un enum aparte.
 * System reservado para eventos generados por el server (participant-added,
 * etc.) — no lo emite un usuario.
 */
export const MessageKind = {
  Text: 'Text',
  Attachment: 'Attachment',
  System: 'System',
} as const;

export type MessageKind = (typeof MessageKind)[keyof typeof MessageKind];

export function isMessageKind(value: string): value is MessageKind {
  return value === 'Text' || value === 'Attachment' || value === 'System';
}

/**
 * Origen de un mensaje System, para que el hilo elija su ícono sin leer el texto: Call = evento de llamada,
 * Membership = alguien entra/sale o cambia de rol, Notice = cualquier otro aviso (nombre del grupo, cola...).
 */
export const SystemMessageKind = {
  Call: 'Call',
  Membership: 'Membership',
  Notice: 'Notice',
} as const;

export type SystemMessageKind = (typeof SystemMessageKind)[keyof typeof SystemMessageKind];

export function isSystemMessageKind(value: string | null | undefined): value is SystemMessageKind {
  return value === 'Call' || value === 'Membership' || value === 'Notice';
}
