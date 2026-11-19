import { describe, expect, it } from 'vitest';
import { WorkLimits, clipWorkTitle } from '../../src/contracts/http/work-http.js';

/** Igual que WorkLimits.ClipTitle (C#): una línea, espacios colapsados, máximo 120 con "…". */
describe('clipWorkTitle', () => {
  it('colapsa espacios y saltos de línea', () => {
    expect(clipWorkTitle('  Review \n\t W-2   for Ana ')).toBe('Review W-2 for Ana');
  });

  it.each([null, undefined, '', '   \n '])('vacío o en blanco: null (%#)', (raw) => {
    expect(clipWorkTitle(raw)).toBeNull();
  });

  it('deja intacto un título de exactamente 120 y recorta uno más largo con "…"', () => {
    const exact = 'a'.repeat(WorkLimits.TitleMaxLength);
    const clipped = clipWorkTitle('b'.repeat(WorkLimits.TitleMaxLength + 1));

    expect(clipWorkTitle(exact)).toBe(exact);
    expect(clipped).toBe(`${'b'.repeat(WorkLimits.TitleMaxLength - 1)}…`);
  });
});
