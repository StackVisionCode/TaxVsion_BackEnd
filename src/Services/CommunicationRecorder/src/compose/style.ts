/**
 * Estilo visual de la composición (no son límites de producto): colores, proporciones y duraciones
 * de los avisos. Los tamaños escalan con la altura del lienzo (referencia 720p).
 */
export const STYLE = {
  backgroundColor: '0x111827',
  tileColor: '0x1f2937',
  slateColor: '0x374151',
  overflowColor: '0x4b5563',
  screenBackground: 'black',
  speakerBorderColor: '0x22c55e',
  mutedBoxColor: '0xdc2626@0.85',
  handBoxColor: '0xf59e0b@0.9',
  labelBoxColor: 'black@0.55',
  toastBoxColor: 'black@0.7',
  reactionBoxColor: '0x7c3aed@0.85',
  /** Ancho del área de pantalla compartida respecto del lienzo. */
  screenWidthRatio: 0.78,
  toastMs: 4000,
  reactionWindowMs: 60_000,
  /** Huecos menores entre speaker.started/stopped se funden (evita parpadeo del borde). */
  speakerMergeGapMs: 750,
  tileAspect: 16 / 9,
  maxGridTiles: 9,
  /** Lado del PNG circular que se prepara una vez por foto (cada tile lo escala). */
  avatarRenderPx: 256,
  /** Diámetro de la foto respecto del lado menor de la tile. */
  avatarDiameterRatio: 0.5,
} as const;

export interface Metrics {
  readonly gap: number;
  readonly margin: number;
  readonly toastFont: number;
  readonly toastStep: number;
  readonly reactionFont: number;
  readonly borderWidth: number;
}

export function canvasMetrics(height: number): Metrics {
  const s = height / 720;
  const toastFont = Math.max(12, Math.round(22 * s));
  return {
    gap: even(Math.max(2, Math.round(8 * s))),
    margin: Math.max(4, Math.round(14 * s)),
    toastFont,
    toastStep: toastFont + Math.max(8, Math.round(22 * s)),
    reactionFont: Math.max(10, Math.round(18 * s)),
    borderWidth: Math.max(2, Math.round(4 * s)),
  };
}

export interface TileFonts {
  readonly initials: number;
  readonly slateName: number;
  readonly label: number;
  readonly badge: number;
  readonly pad: number;
}

export function tileFonts(tileHeight: number): TileFonts {
  return {
    initials: Math.max(14, Math.round(tileHeight * 0.3)),
    slateName: Math.max(10, Math.round(tileHeight * 0.085)),
    label: Math.max(10, Math.round(Math.min(18, tileHeight * 0.1))),
    badge: Math.max(9, Math.round(Math.min(15, tileHeight * 0.085))),
    pad: Math.max(4, Math.round(tileHeight * 0.04)),
  };
}

export function even(n: number): number {
  return Math.max(2, Math.floor(n / 2) * 2);
}
