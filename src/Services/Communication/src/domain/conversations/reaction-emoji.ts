/**
 * F8d (T-25): una reacción es UN emoji (un grafema), nunca texto. `\p{RGI_Emoji}` cubre tonos de piel, banderas,
 * secuencias ZWJ y keycaps. La regex se construye en ejecución porque el flag `v` no existe en el target ES2022
 * del compilador (Node 20+ sí lo soporta).
 */
const RGI_EMOJI = new RegExp('^\\p{RGI_Emoji}$', 'v');
const MAX_EMOJI_UTF16 = 16;

export function isReactionEmoji(value: string): boolean {
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > MAX_EMOJI_UTF16) return false;
  const graphemes = [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(trimmed)];
  return graphemes.length === 1 && RGI_EMOJI.test(trimmed);
}
