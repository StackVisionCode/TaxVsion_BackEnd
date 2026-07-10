import type { ConversationKind } from './conversation-kind.js';

/**
 * Genera la clave de unicidad de una conversacion. La usamos para el indice
 * unique (TenantId, UniquenessKey) — evita duplicados por race condition.
 *
 * Direct: `direct:{userA}:{userB}` con ids ordenados alfabeticamente para
 *   que `startDirect(A,B)` y `startDirect(B,A)` colapsen a la misma fila.
 * Group: `group:{groupId}` — el groupId lo genera el creador.
 * Support: `support:{ticketId}` — el ticketId lo genera Communication.
 */
export function computeDirectUniquenessKey(userA: string, userB: string): string {
  if (userA === userB) {
    throw new Error('Cannot start a direct conversation with yourself.');
  }
  const [first, second] = [userA, userB].sort();
  return `direct:${first}:${second}`;
}

export function computeGroupUniquenessKey(groupId: string): string {
  return `group:${groupId}`;
}

export function computeSupportUniquenessKey(ticketId: string): string {
  return `support:${ticketId}`;
}

export function extractDirectParticipants(uniquenessKey: string): [string, string] | null {
  if (!uniquenessKey.startsWith('direct:')) return null;
  const parts = uniquenessKey.slice('direct:'.length).split(':');
  if (parts.length !== 2 || !parts[0] || !parts[1]) return null;
  return [parts[0], parts[1]];
}

/**
 * Helper de guarda para uso en factories del aggregate.
 */
export function assertKindMatchesKey(kind: ConversationKind, key: string): void {
  if (kind === 'Direct' && !key.startsWith('direct:')) throw new Error('Direct kind requires direct: key.');
  if (kind === 'Group' && !key.startsWith('group:')) throw new Error('Group kind requires group: key.');
  if (kind === 'Support' && !key.startsWith('support:')) throw new Error('Support kind requires support: key.');
}
