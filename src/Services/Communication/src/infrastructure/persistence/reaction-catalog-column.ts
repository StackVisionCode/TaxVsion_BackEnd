import { isReactionCatalog } from '../../domain/settings/technical-limits.js';

/**
 * F5 (D-50): el catalogo de reacciones vive como JSON array en una columna NVARCHAR (SQL Server no
 * tiene tipo JSON en Prisma). Lectura fail-safe: null/invalido -> null; el dominio decide el fallback.
 */
export function parseReactionCatalogColumn(raw: string | null | undefined): string[] | null {
  if (raw === null || raw === undefined || raw.trim() === '') return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return isReactionCatalog(parsed) ? [...parsed] : null;
  } catch {
    return null;
  }
}
