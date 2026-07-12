import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile, rm } from 'node:fs/promises';
import { config } from '../config.js';
const execFileAsync = promisify(execFile);
/**
 * Shell-out a whisper.cpp (`whisper-cli`), compilado desde fuente en el
 * Dockerfile — deliberadamente NO un binario npm-wrapped, para no arrastrar
 * Python al stack (decision de Fase 6, ver elección whisper.cpp vs
 * faster-whisper). Requiere WAV 16kHz mono como input; la conversion corre
 * antes via `transcodeToWav16kMono`.
 */
export async function transcribeWav(wavPath, outPrefix) {
    const args = ['-m', config.whisper.modelPath, '-f', wavPath, '-otxt', '-of', outPrefix, '-nt'];
    args.push('-l', config.whisper.language ?? 'auto');
    const { stdout } = await execFileAsync(config.whisper.binPath, args, {
        maxBuffer: 32 * 1024 * 1024,
        timeout: 15 * 60 * 1000,
    });
    const txtPath = `${outPrefix}.txt`;
    const text = (await readFile(txtPath, 'utf-8')).trim();
    await rm(txtPath, { force: true });
    return { text, language: extractDetectedLanguage(stdout) ?? config.whisper.language };
}
function extractDetectedLanguage(stdout) {
    const match = /auto-detected language:\s*([a-z]{2})/i.exec(stdout);
    return match?.[1]?.toLowerCase() ?? null;
}
//# sourceMappingURL=whisper-transcriber.js.map