export function buildWhisperArgs(input) {
    const args = ['-m', input.modelPath, '-f', input.wavPath, '-oj', '-of', input.outPrefix, '-l', input.language];
    const prompt = input.prompt?.trim();
    if (prompt)
        args.push('--prompt', prompt);
    args.push('-mc', String(input.maxContext), '-et', String(input.entropyThold), '-bs', String(input.beamSize), '-t', String(input.threads));
    if (input.suppressNst)
        args.push('-sns');
    const vadModelPath = input.vadModelPath?.trim();
    if (vadModelPath)
        args.push('--vad', '--vad-model', vadModelPath);
    return args;
}
/** 0 = nucleos − 1 (deja uno para el event loop, ffmpeg y el SO); minimo 1. */
export function resolveWhisperThreads(configured, availableCores) {
    if (configured > 0)
        return configured;
    return Math.max(1, availableCores - 1);
}
/** Idioma: el del evento (oficina) manda; luego el env; luego auto. */
export function resolveWhisperLanguage(eventLanguage, envLanguage) {
    const fromEvent = eventLanguage?.trim();
    if (fromEvent)
        return fromEvent;
    const fromEnv = envLanguage?.trim();
    return fromEnv || 'auto';
}
//# sourceMappingURL=whisper-args.js.map