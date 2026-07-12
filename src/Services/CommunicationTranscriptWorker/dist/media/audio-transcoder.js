import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config.js';
const execFileAsync = promisify(execFile);
/**
 * whisper.cpp solo lee WAV PCM 16kHz mono — las grabaciones llegan en el
 * formato que produjo `MediaRecorder` en el navegador (webm/opus tipico).
 * ffmpeg hace la conversion; se instala via apt en el Dockerfile, no es
 * dependencia npm.
 */
export async function transcodeToWav16kMono(inputPath, outputWavPath) {
    await execFileAsync(config.ffmpeg.binPath, ['-y', '-i', inputPath, '-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', outputWavPath], { timeout: 5 * 60 * 1000 });
}
//# sourceMappingURL=audio-transcoder.js.map