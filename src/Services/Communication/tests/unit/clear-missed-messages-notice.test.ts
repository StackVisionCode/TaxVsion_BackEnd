import { describe, expect, it } from 'vitest';
import {
  clearMissedMessagesNotice,
  missedMessagesGroupKey,
} from '../../src/application/use-cases/clear-missed-messages-notice.js';

describe('clearMissedMessagesNotice', () => {
  it('the group key matches the one Notification writes', () => {
    expect(missedMessagesGroupKey('6E6F2E0A-1B2C-4D5E-8F90-ABCDEF012345')).toBe('chat:6e6f2e0a1b2c4d5e8f90abcdef012345');
  });

  it('marks the grouped bell row as read and returns its id', async () => {
    const calls: Array<{ ids: readonly string[] }> = [];
    const notifications = {
      findUnreadIdsByGroupKey: async (input: { groupKey: string }) =>
        input.groupKey === 'chat:6e6f2e0a1b2c4d5e8f90abcdef012345' ? ['n-1'] : [],
      markManyRead: async (input: { ids: readonly string[] }) => {
        calls.push({ ids: input.ids });
        return { markedCount: input.ids.length, unreadCount: 0 };
      },
    };

    const ids = await clearMissedMessagesNotice(
      { tenantId: 't-1', userId: 'u-1', conversationId: '6e6f2e0a-1b2c-4d5e-8f90-abcdef012345' },
      { notifications },
    );

    expect(ids).toEqual(['n-1']);
    expect(calls).toEqual([{ ids: ['n-1'] }]);
  });

  it('without a row nothing is written', async () => {
    let wrote = false;
    const notifications = {
      findUnreadIdsByGroupKey: async () => [],
      markManyRead: async () => {
        wrote = true;
        return { markedCount: 0, unreadCount: 0 };
      },
    };

    expect(await clearMissedMessagesNotice({ tenantId: 't', userId: 'u', conversationId: 'c' }, { notifications })).toEqual([]);
    expect(wrote).toBe(false);
  });
});
