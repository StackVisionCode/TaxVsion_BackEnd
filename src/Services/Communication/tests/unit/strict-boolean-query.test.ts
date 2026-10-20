import { describe, expect, it } from 'vitest';
import { NotificationListQuerySchema } from '../../src/api/http/routes/notifications.route.js';
import { OptionalBooleanQuery } from '../../src/api/http/schemas/strict-boolean-query.js';

describe('strict boolean query parsing', () => {
  it('keeps unreadOnly=false as false', () => {
    expect(NotificationListQuerySchema.parse({ unreadOnly: 'false' }).unreadOnly).toBe(false);
  });

  it('keeps unreadOnly=true as true', () => {
    expect(NotificationListQuerySchema.parse({ unreadOnly: 'true' }).unreadOnly).toBe(true);
  });

  it('keeps an absent unreadOnly filter undefined', () => {
    expect(NotificationListQuerySchema.parse({}).unreadOnly).toBeUndefined();
  });

  it('rejects truthy strings that are not booleans', () => {
    expect(OptionalBooleanQuery.safeParse('1').success).toBe(false);
    expect(OptionalBooleanQuery.safeParse('yes').success).toBe(false);
  });
});
