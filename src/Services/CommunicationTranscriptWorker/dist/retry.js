export async function withRetry(fn, opts) {
    const attemptsByPolicy = new Map();
    for (let attempt = 1;; attempt += 1) {
        try {
            return await fn();
        }
        catch (err) {
            if (!opts.isRetriable(err))
                throw err;
            const policy = opts.policyFor?.(err) ?? opts;
            const policyAttempt = (attemptsByPolicy.get(policy) ?? 0) + 1;
            attemptsByPolicy.set(policy, policyAttempt);
            if (policyAttempt >= policy.maxAttempts)
                throw err;
            const delayMs = policy.backoffMs[policyAttempt - 1] ?? policy.backoffMs[policy.backoffMs.length - 1] ?? 0;
            opts.onRetry(attempt, err, delayMs);
            await sleep(delayMs);
        }
    }
}
function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}
//# sourceMappingURL=retry.js.map