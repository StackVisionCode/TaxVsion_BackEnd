/**
 * Construccion pura de la linea de comandos de whisper-cli (v1.9.2). Flags
 * contrastados con `whisper-cli --help` de esa version: -mc (max-context),
 * -et (entropy-thold), -bs (beam-size), -t (threads), -sns (suppress-nst),
 * --vad / -vm (vad-model), -oj (salida JSON con offsets en ms).
 */
export interface WhisperArgsInput {
  readonly modelPath: string;
  readonly wavPath: string;
  readonly outPrefix: string;
  /** 'auto' o codigo ISO (es, en...). */
  readonly language: string;
  readonly prompt: string | null;
  readonly maxContext: number;
  readonly entropyThold: number;
  readonly beamSize: number;
  readonly threads: number;
  readonly suppressNst: boolean;
  /** null o vacio = VAD apagado. */
  readonly vadModelPath: string | null;
}

export function buildWhisperArgs(input: WhisperArgsInput): string[] {
  const args = ['-m', input.modelPath, '-f', input.wavPath, '-oj', '-of', input.outPrefix, '-l', input.language];
  const prompt = input.prompt?.trim();
  if (prompt) args.push('--prompt', prompt);
  args.push(
    '-mc',
    String(input.maxContext),
    '-et',
    String(input.entropyThold),
    '-bs',
    String(input.beamSize),
    '-t',
    String(input.threads),
  );
  if (input.suppressNst) args.push('-sns');
  const vadModelPath = input.vadModelPath?.trim();
  if (vadModelPath) args.push('--vad', '--vad-model', vadModelPath);
  return args;
}

/** 0 = nucleos − 1 (deja uno para el event loop, ffmpeg y el SO); minimo 1. */
export function resolveWhisperThreads(configured: number, availableCores: number): number {
  if (configured > 0) return configured;
  return Math.max(1, availableCores - 1);
}

/** Idioma: el del evento (oficina) manda; luego el env; luego auto. */
export function resolveWhisperLanguage(eventLanguage: string | null | undefined, envLanguage: string | null): string {
  const fromEvent = eventLanguage?.trim();
  if (fromEvent) return fromEvent;
  const fromEnv = envLanguage?.trim();
  return fromEnv || 'auto';
}
