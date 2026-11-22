import type { ChatMessagesMissedEvent } from '../../contracts/events/chat-events.js';

/** Participante con mensajes de otros sin leer (Direct o Group, no silenciado). */
export interface MissedMessagesCandidate {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly conversationKind: 'Direct' | 'Group';
  readonly userId: string;
  readonly unreadCount: number;
  readonly oldestUnreadAtUtc: Date;
  readonly latestMessageId: string;
  readonly latestSenderId: string;
  readonly lastReadAtUtc: Date | null;
  /** Hasta cuándo ya se avisó (null = nunca). */
  missedNoticeAtUtc: Date | null;
}

export interface MissedMessagesStore {
  /** Conversaciones con actividad desde `lastMessageSinceUtc` y un no leído anterior a `unreadBeforeUtc`. */
  findCandidates(input: {
    lastMessageSinceUtc: Date;
    unreadBeforeUtc: Date;
    limit: number;
  }): Promise<readonly MissedMessagesCandidate[]>;
  /** En una transacción: fija la marca del participante y encola el evento. false si otro ya avisó. */
  recordNotice(input: {
    candidate: MissedMessagesCandidate;
    noticedAtUtc: Date;
    event: ChatMessagesMissedEvent;
  }): Promise<boolean>;
}
