export async function withRetry(fn, opts) {
    for (let attempt = 1;; attempt += 1) {
        try {
            return await fn();
        }
        catch (err) {
            if (attempt >= opts.maxAttempts || !opts.isRetriable(err)) {
                throw err;
            }
            const delayMs = opts.backoffMs[attempt - 1] ?? opts.backoffMs[opts.backoffMs.length - 1] ?? 0;
            opts.onRetry(attempt, err, delayMs);
            await sleep(delayMs);
        }
    }
}
function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}
//# sourceMappingURL=retry.js.map