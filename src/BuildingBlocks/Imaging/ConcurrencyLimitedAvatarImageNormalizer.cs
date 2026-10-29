using BuildingBlocks.Results;

namespace BuildingBlocks.Imaging;

/// <summary>
/// Limita cuántas normalizaciones decodifican a la vez (<see cref="AvatarImageOptions.MaxConcurrency"/>).
/// La lectura del stream va fuera del límite: solo la decodificación, que es lo que reserva memoria,
/// espera turno. Es lo que registra <see cref="ImagingServiceCollectionExtensions.AddAvatarImageNormalizer"/>.
/// </summary>
public sealed class ConcurrencyLimitedAvatarImageNormalizer : IAvatarImageNormalizer
{
    private readonly IAvatarImageNormalizer _inner;
    private readonly long _maxInputBytes;
    private readonly SemaphoreSlim _gate;

    public ConcurrencyLimitedAvatarImageNormalizer(IAvatarImageNormalizer inner, AvatarImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var resolved = options ?? new AvatarImageOptions();
        resolved.Validate();

        _inner = inner;
        _maxInputBytes = resolved.MaxInputBytes;
        _gate = new SemaphoreSlim(resolved.MaxConcurrency, resolved.MaxConcurrency);
    }

    public async Task<Result<NormalizedAvatar>> NormalizeAsync(
        Stream input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var buffer = await AvatarImageNormalizer.ReadBoundedAsync(input, _maxInputBytes, cancellationToken);
        if (buffer is null)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.TooLarge);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _inner.Normalize(buffer);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input)
    {
        _gate.Wait();
        try
        {
            return _inner.Normalize(input);
        }
        finally
        {
            _gate.Release();
        }
    }
}
