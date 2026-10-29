using BuildingBlocks.Imaging;
using BuildingBlocks.Results;
using SkiaSharp;
using Xunit;
using static TaxVision.BuildingBlocks.Tests.Imaging.AvatarTestImages;

namespace TaxVision.BuildingBlocks.Tests.Imaging;

/// <summary>
/// El límite de decodificaciones simultáneas: un PNG de 40 MP ocupa ~160 MB, así que muchas subidas a la
/// vez podían tumbar el proceso. Se usa un normalizador falso que se queda bloqueado para contar cuántos
/// entran a la vez sin depender de tiempos.
/// </summary>
public sealed class ConcurrencyLimitedAvatarImageNormalizerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task NormalizeAsync_NoDejaPasarMasDeMaxConcurrencyALaVez()
    {
        var inner = new BlockingNormalizer();
        var limited = new ConcurrencyLimitedAvatarImageNormalizer(inner, new AvatarImageOptions { MaxConcurrency = 2 });

        var calls = Enumerable
            .Range(0, 5)
            .Select(_ => Task.Run(() => limited.NormalizeAsync(new MemoryStream([1, 2, 3]))))
            .ToArray();

        Assert.True(inner.WaitUntilInside(2, Timeout), "Two normalizations should be running.");
        // Con los dos primeros bloqueados, ningún otro puede entrar.
        await Task.Delay(200);
        Assert.Equal(2, inner.Inside);

        inner.Release();
        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, inner.Peak);
        Assert.Equal(5, inner.Calls);
    }

    [Fact]
    public async Task Normalize_SincronoTambienRespetaElLimite()
    {
        var inner = new BlockingNormalizer();
        var limited = new ConcurrencyLimitedAvatarImageNormalizer(inner, new AvatarImageOptions { MaxConcurrency = 1 });

        var calls = Enumerable.Range(0, 3).Select(_ => Task.Run(() => limited.Normalize([1, 2, 3]))).ToArray();

        Assert.True(inner.WaitUntilInside(1, Timeout));
        await Task.Delay(200);
        Assert.Equal(1, inner.Inside);

        inner.Release();
        await Task.WhenAll(calls);
        Assert.Equal(1, inner.Peak);
    }

    [Fact]
    public async Task NormalizeAsync_ArchivoDemasiadoGrande_NoOcupaTurnoNiLlamaAlNormalizador()
    {
        var inner = new BlockingNormalizer();
        inner.Release();
        var limited = new ConcurrencyLimitedAvatarImageNormalizer(inner, new AvatarImageOptions { MaxInputBytes = 4 });

        var result = await limited.NormalizeAsync(new MemoryStream(new byte[5]));

        Assert.Equal(AvatarImageErrors.TooLarge, result.Error);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task NormalizeAsync_ConElNormalizadorReal_DevuelveElMismoAvatar()
    {
        using var source = PhotoLike(800, 600);
        var bytes = Encode(source, SKEncodedImageFormat.Jpeg);
        var limited = new ConcurrencyLimitedAvatarImageNormalizer(new AvatarImageNormalizer());

        var result = await limited.NormalizeAsync(new MemoryStream(bytes));

        Assert.True(result.IsSuccess);
        Assert.Equal(new AvatarImageNormalizer().Normalize(bytes).Value.Sha256, result.Value.Sha256);
    }

    [Fact]
    public async Task NormalizeAsync_CanceladoMientrasEspera_NoConsumeTurno()
    {
        var inner = new BlockingNormalizer();
        var limited = new ConcurrencyLimitedAvatarImageNormalizer(inner, new AvatarImageOptions { MaxConcurrency = 1 });
        var first = Task.Run(() => limited.NormalizeAsync(new MemoryStream([1])));
        Assert.True(inner.WaitUntilInside(1, Timeout));

        using var cts = new CancellationTokenSource();
        var waiting = limited.NormalizeAsync(new MemoryStream([1]), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        inner.Release();
        Assert.True((await first).IsSuccess);
        Assert.True((await limited.NormalizeAsync(new MemoryStream([1]))).IsSuccess);
    }

    /// <summary>Se queda dentro de Normalize hasta que el test lo suelta; cuenta los que están dentro.</summary>
    private sealed class BlockingNormalizer : IAvatarImageNormalizer
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _inside;
        private int _peak;
        private int _calls;

        public int Inside => Volatile.Read(ref _inside);
        public int Peak => Volatile.Read(ref _peak);
        public int Calls => Volatile.Read(ref _calls);

        public void Release() => _release.Set();

        public bool WaitUntilInside(int expected, TimeSpan timeout) =>
            SpinWait.SpinUntil(() => Inside >= expected, timeout);

        public Task<Result<NormalizedAvatar>> NormalizeAsync(
            Stream input,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("The limiter must call the synchronous overload.");

        public Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input)
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _inside);
            int seen;
            while (now > (seen = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen)
            { }

            _release.Wait(Timeout);
            Interlocked.Decrement(ref _inside);
            return Result.Success(new NormalizedAvatar([0x52], "image/webp", 1, 1, "x"));
        }
    }
}
