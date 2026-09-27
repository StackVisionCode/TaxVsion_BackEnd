/**
 * A6 — decide si el gate de modulo DENIEGA o solo registra, **modulo por modulo**. Espejo de
 * `BuildingBlocks.Web.ActorTypeAuthorization.ModuleGateSettings` (.NET).
 *
 * Este servicio emite permisos de DOS modulos: `comms` (chat, llamadas y video, en todos los planes)
 * y `meetings` (reuniones, que se venden aparte). Con un solo booleano se encenderian los dos a la
 * vez, y el plan pide subir el escalon de uno en uno porque encenderlo es visible para los tenants:
 * un dato malo se paga con 403 en algo que si pagaron.
 *
 * Vive aparte de `main.ts` para poder probarlo: dentro del closure del composition root, lo unico que
 * se podia hacer era reescribir la regla en el test y comprobar la copia.
 */
export interface ModuleGateConfig {
  /** Interruptor general. `false` = nada deniega, pase lo que pase en la lista. */
  readonly enforce: boolean;
  /** Modulos del escalon. Vacia = todos (estado final). */
  readonly enforcedModules: readonly string[];
}

/** ¿Este modulo en concreto debe DENEGAR, o solo registrarse? */
export function shouldEnforceModule(config: ModuleGateConfig, module: string): boolean {
  if (!config.enforce) return false;
  if (config.enforcedModules.length === 0) return true;
  return config.enforcedModules.includes(module.toLowerCase());
}
