/**
 * Escapado para valores dentro de `-filter_complex_script`. Hay dos niveles de análisis en ffmpeg:
 *  1. Grafo (av_get_token con "[],;"): comillas simples literales; una ' se escribe '\''.
 *  2. Opción (clave=valor separados por ':'): \ ' y : se escapan con barra invertida.
 * Con `expansion=none` en drawtext el texto resultante se dibuja tal cual (sin secuencias %{}).
 */
export function escapeOptionValue(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/:/g, '\\:');
}

export function quoteGraph(value: string): string {
  return `'${value.replace(/'/g, "'\\''")}'`;
}

/** Valor literal arbitrario (texto de usuario, rutas) listo para una opción de filtro. */
export function filterValue(value: string): string {
  return quoteGraph(escapeOptionValue(value));
}

/** Ruta de fuente: barras normales (Windows acepta `C:/...`) y escapado de dos niveles. */
export function fontPath(path: string): string {
  return filterValue(path.replace(/\\/g, '/'));
}

/** Quita controles/saltos de línea que drawtext pintaría como cajas. */
export function sanitizeText(text: string): string {
  return text.replace(/[\p{Cc}\p{Cf}]/gu, ' ').replace(/\s+/g, ' ').trim();
}

/** Recorta con "..." para que el texto quepa aproximadamente en `maxWidth` px. */
export function fitText(text: string, fontSize: number, maxWidth: number): string {
  const clean = sanitizeText(text);
  const maxChars = Math.max(3, Math.floor(maxWidth / (fontSize * 0.58)));
  return clean.length <= maxChars ? clean : `${clean.slice(0, Math.max(1, maxChars - 3)).trimEnd()}...`;
}

/** Línea `file '...'` del demuxer concat (misma regla de comillas que el grafo). */
export function concatLine(path: string): string {
  return `file ${quoteGraph(path.replace(/\\/g, '/'))}`;
}
