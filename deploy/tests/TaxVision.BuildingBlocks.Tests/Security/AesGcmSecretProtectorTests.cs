using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

/// <summary>
/// BB-05 — el protector no-rotativo no tenía ningún test pese a cifrar tokens OAuth, contraseñas
/// SMTP y secretos TOTP en reposo. Lo que se fija acá: el formato del envelope
/// (<c>nonce[12] || ciphertext || tag[16]</c>), la unicidad del nonce, y que TODA manipulación
/// del ciphertext sea rechazada — un AEAD que descifra un mensaje alterado no sirve de nada.
/// </summary>
public sealed class AesGcmSecretProtectorTests
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Clave fija para que el vector golden sea reproducible entre corridas.</summary>
    private static readonly byte[] GoldenKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private static AesGcmSecretProtector Build(byte[]? key = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:MasterKey"] = Convert.ToBase64String(key ?? RandomNumberGenerator.GetBytes(32)),
                }
            )
            .Build();

        return new AesGcmSecretProtector(configuration);
    }

    // ---------------------------------------------------------------- corrección funcional

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("0123456789abcdef")] // 16 bytes exactos — un bloque AES completo
    [InlineData("0123456789abcdef0123456789abcdef0123456789")] // más de un bloque
    [InlineData("contraseña con acentos, 日本語 y emoji 🔐")]
    public void Protect_ThenUnprotect_DevuelveElOriginal(string plaintext)
    {
        var protector = Build();

        Assert.True(protector.TryUnprotect(protector.Protect(plaintext), out var roundTripped, out _));
        Assert.Equal(plaintext, roundTripped);
    }

    [Fact]
    public void Protect_ThenUnprotect_SoportaUnMegabyte()
    {
        var protector = Build();
        var plaintext = new string('a', 1024 * 1024);

        Assert.True(protector.TryUnprotect(protector.Protect(plaintext), out var roundTripped, out _));
        Assert.Equal(plaintext, roundTripped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("0123456789abcdef")]
    public void Protect_ProduceUnEnvelopeDeNonceMasCiphertextMasTag(string plaintext)
    {
        // Congela el contrato de formato: si alguien reordena las partes o cambia el tamaño del
        // nonce, esto falla antes de que llegue a producción y vuelva ilegibles los datos viejos.
        var protector = Build();

        var envelope = Convert.FromBase64String(protector.Protect(plaintext));

        Assert.Equal(NonceSize + Encoding.UTF8.GetByteCount(plaintext) + TagSize, envelope.Length);
    }

    [Fact]
    public void Unprotect_DescifraUnVectorGoldenConElLayoutEsperado()
    {
        // El envelope se arma acá a mano con AES-GCM crudo, no con Protect(): si el protector
        // cambiara a tag||nonce||ciphertext, este test falla aunque su round-trip siga cerrando.
        const string plaintext = "taxvision-golden-vector";
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = Enumerable.Range(0, NonceSize).Select(i => (byte)(0xA0 + i)).ToArray();
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(GoldenKey, TagSize))
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var envelope = new byte[NonceSize + cipherBytes.Length + TagSize];
        nonce.CopyTo(envelope, 0);
        cipherBytes.CopyTo(envelope, NonceSize);
        tag.CopyTo(envelope, NonceSize + cipherBytes.Length);

        Assert.True(Build(GoldenKey).TryUnprotect(Convert.ToBase64String(envelope), out var decrypted, out _));
        Assert.Equal(plaintext, decrypted);
    }

    // ---------------------------------------------------------------- nonce

    [Fact]
    public void Protect_UsaUnNonceDistintoCadaVez()
    {
        // El test crítico del modo GCM: reutilizar un nonce con la misma clave filtra el XOR de
        // los plaintexts Y la subclave de autenticación, lo que permite forjar tags.
        const int iterations = 1000;
        var protector = Build();
        var nonces = new HashSet<string>();
        var ciphertexts = new HashSet<string>();

        for (var i = 0; i < iterations; i++)
        {
            var envelope = Convert.FromBase64String(protector.Protect("mismo plaintext siempre"));
            nonces.Add(Convert.ToBase64String(envelope.AsSpan(0, NonceSize).ToArray()));
            ciphertexts.Add(Convert.ToBase64String(envelope));
        }

        Assert.Equal(iterations, nonces.Count);
        Assert.Equal(iterations, ciphertexts.Count);
    }

    // ---------------------------------------------------------------- integridad (AEAD)

    [Theory]
    [InlineData(0)] // primer byte del nonce
    [InlineData(NonceSize)] // primer byte del ciphertext
    [InlineData(-1)] // último byte del tag
    public void Unprotect_RechazaUnEnvelopeConUnBitCambiado(int offset)
    {
        var protector = Build();
        var envelope = Convert.FromBase64String(protector.Protect("secreto que no debe alterarse"));
        var index = offset < 0 ? envelope.Length + offset : offset;

        envelope[index] ^= 0x01;

        Assert.False(protector.TryUnprotect(Convert.ToBase64String(envelope), out _, out var failure));
        Assert.Equal(SecretUnprotectFailure.AuthenticationFailed, failure);
    }

    [Fact]
    public void Unprotect_RechazaUnEnvelopeTruncadoEnUnByte()
    {
        var protector = Build();
        var envelope = Convert.FromBase64String(protector.Protect("secreto"));

        var truncated = envelope.AsSpan(0, envelope.Length - 1).ToArray();

        Assert.False(protector.TryUnprotect(Convert.ToBase64String(truncated), out _, out var failure));
        Assert.Equal(SecretUnprotectFailure.AuthenticationFailed, failure);
    }

    [Fact]
    public void Unprotect_RechazaUnEnvelopeSinTag()
    {
        var protector = Build();
        var envelope = Convert.FromBase64String(protector.Protect("secreto"));

        var withoutTag = envelope.AsSpan(0, envelope.Length - TagSize).ToArray();

        // Sin el tag el envelope cae por debajo del mínimo, así que se rechaza por forma antes de
        // intentar descifrar — no llega a ser un fallo de autenticación.
        Assert.False(protector.TryUnprotect(Convert.ToBase64String(withoutTag), out _, out var failure));
        Assert.Equal(SecretUnprotectFailure.MalformedInput, failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NonceSize + TagSize - 1)] // justo por debajo del mínimo
    public void Unprotect_RechazaLimpiamenteUnaEntradaMasCortaQueElMinimo(int length)
    {
        // Debe fallar limpio, NO con IndexOutOfRange: la longitud se valida antes de cortar spans.
        var protector = Build();

        Assert.False(protector.TryUnprotect(Convert.ToBase64String(new byte[length]), out _, out var failure));
        Assert.Equal(
            length == 0 ? SecretUnprotectFailure.NoValueStored : SecretUnprotectFailure.MalformedInput,
            failure
        );
    }

    [Fact]
    public void Unprotect_RechazaUnEnvelopeCifradoConOtraClave()
    {
        var ciphertext = Build().Protect("secreto de otro despacho");

        Assert.False(Build().TryUnprotect(ciphertext, out _, out var failure));
        Assert.Equal(SecretUnprotectFailure.AuthenticationFailed, failure);
    }

    [Theory]
    [InlineData("", SecretUnprotectFailure.NoValueStored)]
    [InlineData("   ", SecretUnprotectFailure.NoValueStored)]
    [InlineData("esto no es base64 !!!", SecretUnprotectFailure.MalformedInput)]
    [InlineData("texto plano sin cifrar", SecretUnprotectFailure.MalformedInput)]
    public void Unprotect_RechazaEntradasQueNoSonUnEnvelopeValido(string input, SecretUnprotectFailure expected)
    {
        Assert.False(Build().TryUnprotect(input, out _, out var failure));
        Assert.Equal(expected, failure);
    }

    [Fact]
    public void Unprotect_ConNull_DevuelveNull()
    {
        Assert.False(Build().TryUnprotect(null, out _, out var failure));
        Assert.Equal(SecretUnprotectFailure.NoValueStored, failure);
    }

    // ---------------------------------------------------------------- configuración

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_SinMasterKey_Revienta(string? configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:MasterKey"] = configured })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(configuration));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(64)]
    public void Constructor_ConUnaMasterKeyQueNoMide32Bytes_Revienta(int keySize)
    {
        Assert.Throws<InvalidOperationException>(() => Build(new byte[keySize]));
    }

    // ---------------------------------------------------------------- concurrencia

    [Fact]
    public void Protect_EsSeguroDesdeMultiplesHilos()
    {
        // AesGcm NO es thread-safe (hay estado en el contexto de OpenSSL). El protector lo crea
        // por operación dentro de un using; si alguien lo moviera a un campo compartido para
        // "optimizar", este test lo destapa.
        const int iterations = 1000;
        var protector = Build();
        var envelopes = new ConcurrentBag<string>();

        Parallel.For(0, iterations, _ => envelopes.Add(protector.Protect("concurrente")));

        Assert.Equal(iterations, envelopes.Count);
        Assert.Equal(iterations, envelopes.Distinct().Count());
        Assert.All(
            envelopes,
            e =>
            {
                Assert.True(protector.TryUnprotect(e, out var plaintext, out _));
                Assert.Equal("concurrente", plaintext);
            }
        );
    }
}
