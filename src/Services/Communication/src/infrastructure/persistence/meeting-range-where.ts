import type { Prisma } from '@prisma/client';

// Una reunión cae en el rango por su hora programada; la instantánea (sin programar) por su inicio.
export function scheduledBetweenWhere(range: { fromUtc: Date; toUtc: Date }): Prisma.MeetingWhereInput {
  const between = { gte: range.fromUtc, lt: range.toUtc };
  return {
    OR: [{ ScheduledForUtc: between }, { ScheduledForUtc: null, StartedAtUtc: between }],
  };
}
