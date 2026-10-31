import { execFile } from 'node:child_process';
import { availableParallelism } from 'node:os';
import { promisify } from 'node:util';
import { readFile, rm } from 'node:fs/promises';
import { config } from '../config.js';
import { ProcessTimeoutError, isExecTimeout } from '../errors.js';
import { buildWhisperArgs, resolveWhisperThreads } from './whisper-args.js';
import { extractDetectedLanguage, parseWhisperJson } from './whisper-output.js';
const execFileAsync = promisify(execFile);
/**
 * Shell-out a whisper-cli (compilado en el Dockerfile, sin Python). Salida
 * `-oj` (segmentos con offsets en ms); el JSON se lee y se borra aqui.
 */
export async function transcribeWav(wavPath, outPrefix, opts) {
    const args = buildWhisperArgs({
        modelPath: config.whisper.modelPath,
        wavPath,
        outPrefix,
        language: opts.language,
        prompt: opts.prompt,
        maxContext: config.whisper.maxContext,
        entropyThold: config.whisper.entropyThold,
        beamSize: config.whisper.beamSize,
        threads: resolveWhisperThreads(config.whisper.threads, availableParallelism()),
        suppressNst: config.whisper.suppressNst,
        vadModelPath: config.whisper.vadModelPath,
    });
    const startedAt = process.hrtime.bigint();
    let stdout;
    try {
        ({ stdout } = await execFileAsync(config.whisper.binPath, args, {
            maxBuffer: 64 * 1024 * 1024,
            timeout: opts.timeoutMs,
        }));
    }
    catch (err) {
        if (isExecTimeout(err))
            throw new ProcessTimeoutError(`whisper-cli exceeded ${opts.timeoutMs} ms`);
        throw err;
    }
    const processingSeconds = Number(process.hrtime.bigint() - startedAt) / 1e9;
    const jsonPath = `${outPrefix}.json`;
    const raw = await readFile(jsonPath, 'utf-8');
    await rm(jsonPath, { force: true });
    const parsed = parseWhisperJson(raw);
    const fixedLanguage = opts.language === 'auto' ? null : opts.language;
    return {
        segments: parsed.segments,
        detectedLanguage: parsed.language ?? extractDetectedLanguage(stdout) ?? fixedLanguage,
        processingSeconds,
    };
}
//# sourceMappingURL=whisper-transcriber.js.map