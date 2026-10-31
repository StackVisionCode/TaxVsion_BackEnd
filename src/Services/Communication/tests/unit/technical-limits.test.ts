import { describe, expect, it } from 'vitest';
import {
  TECHNICAL_LIMITS,
  clampToTechnical,
  isMeetingSoundsPolicy,
  isTranscriptLanguage,
  isWithinTechnical,
} from '../../src/domain/settings/technical-limits.js';

describe('technical limits', () => {
  it('every range is coherent and its fallback is inside it', () => {
    for (const [key, range] of Object.entries(TECHNICAL_LIMITS)) {
      expect(range.min, key).toBeLessThanOrEqual(range.max);
      expect(range.fallback, key).toBeGreaterThanOrEqual(range.min);
      expect(range.fallback, key).toBeLessThanOrEqual(range.max);
    }
  });

  it('isWithinTechnical rejects non-integers and out-of-range values', () => {
    expect(isWithinTechnical('disconnectGraceSeconds', 30)).toBe(true);
    expect(isWithinTechnical('disconnectGraceSeconds', 4)).toBe(false);
    expect(isWithinTechnical('disconnectGraceSeconds', 121)).toBe(false);
    expect(isWithinTechnical('disconnectGraceSeconds', 30.5)).toBe(false);
  });

  it('clampToTechnical falls back on null and clamps on both ends', () => {
    expect(clampToTechnical('maxParticipants', null)).toBe(TECHNICAL_LIMITS.maxParticipants.fallback);
    expect(clampToTechnical('maxParticipants', Number.NaN)).toBe(TECHNICAL_LIMITS.maxParticipants.fallback);
    expect(clampToTechnical('maxParticipants', 1)).toBe(TECHNICAL_LIMITS.maxParticipants.min);
    expect(clampToTechnical('maxParticipants', 500)).toBe(TECHNICAL_LIMITS.maxParticipants.max);
    expect(clampToTechnical('maxParticipants', 12.9)).toBe(12);
  });

  it('validates sounds policies and language codes', () => {
    expect(isMeetingSoundsPolicy('hostOnly')).toBe(true);
    expect(isMeetingSoundsPolicy('loud')).toBe(false);
    expect(isTranscriptLanguage('auto')).toBe(true);
    expect(isTranscriptLanguage('es')).toBe(true);
    expect(isTranscriptLanguage('pt-BR')).toBe(true);
    expect(isTranscriptLanguage('english')).toBe(false);
  });
});
