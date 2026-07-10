/**
 * Kinds soportados. Direct es lo unico activo en Fase 1;
 * Group y Support quedan modelados para fases posteriores.
 */
export const ConversationKind = {
  Direct: 'Direct',
  Group: 'Group',
  Support: 'Support',
} as const;

export type ConversationKind = (typeof ConversationKind)[keyof typeof ConversationKind];

export function isConversationKind(value: string): value is ConversationKind {
  return value === 'Direct' || value === 'Group' || value === 'Support';
}
