import { describe, expect, it } from 'vitest';
import { chatPushRecipients } from '../../src/application/services/chat-push-recipients.js';

const base = {
  conversationKind: 'Group' as const,
  recipientUserIds: ['muted', 'viewing', 'active', 'free-1', 'free-2'],
  mutedUserIds: ['muted'],
  viewingUserIds: ['viewing'],
  activeUserIds: ['active'],
};

describe('chatPushRecipients', () => {
  it('excludes muted, viewing and active recipients', () => {
    expect(chatPushRecipients(base)).toEqual(['free-1', 'free-2']);
  });

  it('an active recipient is pushed when the option is off', () => {
    expect(chatPushRecipients({ ...base, suppressWhenActive: false })).toEqual(['active', 'free-1', 'free-2']);
  });

  it('a meeting chat pushes nobody', () => {
    expect(chatPushRecipients({ ...base, conversationKind: 'Meeting' })).toEqual([]);
  });

  it('without focus data only the muted are left out', () => {
    expect(
      chatPushRecipients({ conversationKind: 'Direct', recipientUserIds: ['a', 'b'], mutedUserIds: ['b'] }),
    ).toEqual(['a']);
  });
});
