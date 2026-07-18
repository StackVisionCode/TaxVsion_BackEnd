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
