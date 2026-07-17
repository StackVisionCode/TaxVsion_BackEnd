import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config.js';
const execFileAsync = promisify(execFile);
/**
 * Fase Backend 8 (bug #245) — chequeo pre-transcode: ¿el file tiene siquiera
 * una track de audio? Si el MediaRecorder del frontend nunca capturo audio
 * (permisos de mic denegados, camera-only stream), ffmpeg iba a fallar
 * mucho mas adelante con un exit code opaco; ffprobe lo detecta al toque y
 * podemos publicar un evento de validation_failed con reason especifico.
 *
 * Comando: `ffprobe -v error -select_streams a -show_entries stream=codec_type -of csv=p=0 <input>`
 * Salida: una linea "audio" por cada track de audio. Vacio = ninguna.
 * Si ffprobe explota (file corrupto, formato no reconocido), se re-throw —
 * el pipeline caller lo interpreta igual que "sin audio" para efectos de
 * validation_failed. Con salida vacia (no crash) devolvemos false.
 */
export async function hasAudioStream(inputPath) {
    const { stdout } = await execFileAsync(config.ffmpeg.ffprobeBinPath, ['-v', 'error', '-select_streams', 'a', '-show_entries', 'stream=codec_type', '-of', 'csv=p=0', inputPath], { timeout: 30_000 });
    return stdout.trim().length > 0;
}
//# sourceMappingURL=audio-probe.js.map