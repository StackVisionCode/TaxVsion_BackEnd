/**
 * Kinds soportados. Meeting (Fase 8) es un chat 1:1 con un `Meeting` — su
 * lista de participantes se sincroniza con quien esta Joined en el meeting
 * (ver `ensureMeetingConversation` + wiring en join/leave/admit/remove),
 * nunca la edita el usuario directamente como en Group.
 */
export const ConversationKind = {
  Direct: 'Direct',
  Group: 'Group',
  Support: 'Support',
  Meeting: 'Meeting',
} as const;

export type ConversationKind = (typeof ConversationKind)[keyof typeof ConversationKind];

export function isConversationKind(value: string): value is ConversationKind {
  return value === 'Direct' || value === 'Group' || value === 'Support' || value === 'Meeting';
}
