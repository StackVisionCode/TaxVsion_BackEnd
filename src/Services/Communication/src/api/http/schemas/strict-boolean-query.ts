import { z } from 'zod';

export const OptionalBooleanQuery = z
  .union([z.boolean(), z.enum(['true', 'false']).transform((value) => value === 'true')])
  .optional();
