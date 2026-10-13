/** Lo que este servicio necesita de PlatformSettings (Tenant): versión, dominio y nombre de la plataforma. */
export interface PlatformSnapshot {
  readonly version: number;
  readonly baseDomain: string;
  readonly brandName: string;
}

/** Recibe una versión nueva de PlatformSettings (por evento). Devuelve si la aplicó. */
export interface PlatformSettingsSink {
  apply(next: PlatformSnapshot): boolean;
}
