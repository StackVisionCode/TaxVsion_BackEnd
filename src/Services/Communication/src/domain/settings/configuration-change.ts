/**
 * Cambio de una clave de configuración: lo que se audita y lo que viaja en los eventos
 * `*_settings_changed`. Los valores se serializan a texto para que la auditoría no dependa del tipo.
 */
export interface ConfigurationChange {
  readonly key: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
}

export function serializeConfigValue(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  return typeof value === 'string' ? value : JSON.stringify(value);
}

/**
 * Diferencia campo a campo entre el estado actual y un patch parcial. Solo las claves presentes
 * en el patch y con valor distinto cuentan como cambio (bucle simple, sin reducers).
 */
export function diffPatch<T extends object>(current: T, patch: Partial<T>): ConfigurationChange[] {
  const changes: ConfigurationChange[] = [];
  for (const key of Object.keys(patch) as Array<keyof T>) {
    const next = patch[key];
    if (next === undefined) continue;
    const previous = current[key];
    // Por contenido: un catalogo (array) igual no es un cambio aunque sea otra instancia.
    if (serializeConfigValue(previous) === serializeConfigValue(next)) continue;
    changes.push({
      key: String(key),
      oldValue: serializeConfigValue(previous),
      newValue: serializeConfigValue(next),
    });
  }
  return changes;
}
