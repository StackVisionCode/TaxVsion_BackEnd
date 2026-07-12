export const CallKind = { Audio: 'Audio', Video: 'Video' } as const;
export type CallKind = (typeof CallKind)[keyof typeof CallKind];

export function isCallKind(value: string): value is CallKind {
  return value === 'Audio' || value === 'Video';
}
