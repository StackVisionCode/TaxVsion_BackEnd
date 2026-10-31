/**
 * F8d (T-16): enlaces de un mensaje de texto, para la pestaña "Links" de Info. Solo http(s) (un `www.` se toma
 * como https), sin credenciales en la URL, como mucho 10 por mensaje y 2048 caracteres cada uno. Los fronts
 * pintan el enlace como texto clicable (`rel="noopener noreferrer"`), nunca como HTML.
 */
export interface MessageLinkCandidate {
  readonly url: string;
  readonly domain: string;
}

export const MAX_LINKS_PER_MESSAGE = 10;
export const MAX_LINK_LENGTH = 2048;

// Termina antes de espacios y de signos que suelen cerrar una frase o un paréntesis.
const LINK_PATTERN = /\b(?:https?:\/\/|www\.)[^\s<>"'`]+/giu;
const TRAILING_PUNCTUATION = /[.,;:!?)\]}]+$/u;

export function extractMessageLinks(body: string | null | undefined): MessageLinkCandidate[] {
  if (!body) return [];
  const seen = new Set<string>();
  const links: MessageLinkCandidate[] = [];
  for (const match of body.matchAll(LINK_PATTERN)) {
    const raw = match[0].replace(TRAILING_PUNCTUATION, '');
    const candidate = normalizeLink(raw);
    if (!candidate || seen.has(candidate.url)) continue;
    seen.add(candidate.url);
    links.push(candidate);
    if (links.length >= MAX_LINKS_PER_MESSAGE) break;
  }
  return links;
}

function normalizeLink(raw: string): MessageLinkCandidate | null {
  const withScheme = /^www\./iu.test(raw) ? `https://${raw}` : raw;
  if (withScheme.length > MAX_LINK_LENGTH) return null;
  let parsed: URL;
  try {
    parsed = new URL(withScheme);
  } catch {
    return null;
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null;
  if (parsed.username || parsed.password || !parsed.hostname.includes('.')) return null;
  return { url: parsed.toString(), domain: parsed.hostname.toLowerCase().replace(/^www\./u, '') };
}
