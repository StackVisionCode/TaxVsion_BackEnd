using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.DataModel.ILM;
using Minio.Exceptions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class MinioObjectStorage(IMinioClient client) : IObjectStorage
{
    public async Task<PresignedUpload> CreateUploadPolicyAsync(
        string bucket,
        string objectKey,
        string contentType,
        long exactSizeBytes,
        TimeSpan lifetime,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var policy = new PostPolicy();
        policy.SetBucket(bucket);
        policy.SetKey(objectKey);
        policy.SetExpires(DateTime.UtcNow.Add(lifetime));
        policy.SetContentType(contentType);

        // S3 aplica content-length-range al cuerpo multipart completo, no solo al archivo.
        // Se admite margen para los campos y boundaries; CompleteUploadHandler comprueba
        // después que el objeto tenga exactamente el tamaño declarado.
        const long multipartOverheadBytes = 64 * 1024;
        policy.SetContentRange(exactSizeBytes, checked(exactSizeBytes + multipartOverheadBytes));

        var (url, formData) = await client.PresignedPostPolicyAsync(policy);

        // El SDK de MinIO agrega la condicion Content-Type al policy firmado (SetContentType
        // arriba) pero NO devuelve ese campo en formData — solo los campos de firma
        // (bucket/key/policy/x-amz-*). Sin esto, ningun caller (Postman, un frontend) puede
        // satisfacer su propio policy: el POST a MinIO siempre rechaza con
        // AccessDenied "Policy Condition failed" por falta del campo Content-Type.
        var result = new Dictionary<string, string>(formData, StringComparer.Ordinal)
        {
            ["Content-Type"] = contentType,
        };
        return new PresignedUpload(url, result);
    }

    public async Task<Uri> PresignGetAsync(string bucket, string objectKey, TimeSpan lifetime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var url = await client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs().WithBucket(bucket).WithObject(objectKey).WithExpiry((int)lifetime.TotalSeconds)
        );
        return new Uri(url);
    }

    public async Task<Uri> PresignGetAsync(
        string bucket,
        string objectKey,
        TimeSpan lifetime,
        string contentDisposition,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var url = await client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithExpiry((int)lifetime.TotalSeconds)
                .WithHeaders(new Dictionary<string, string> { ["response-content-disposition"] = contentDisposition })
        );
        return new Uri(url);
    }

    public async Task<long> GetSizeAsync(string bucket, string objectKey, CancellationToken ct)
    {
        var stat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
        return stat.Size;
    }

    public async Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken ct)
    {
        try
        {
            await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public Task DownloadAsync(string bucket, string objectKey, Stream destination, CancellationToken ct) =>
        client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                // OJO: WithCallbackStream tiene 2 overloads — Action<Stream> (sincrono) y
                // Func<Stream, CancellationToken, Task> (async de verdad). Un lambda de UN
                // solo parametro (`source => ...`) matchea el overload Action<Stream>, y el
                // compilador lo compila como `async void`: el SDK lo trata como sincrono, no
                // lo espera, y dispone el stream de la respuesta HTTP antes de que la
                // continuacion async termine de copiar -> ObjectDisposedException en un hilo
                // aparte que ningun try/catch del caller puede atrapar. Pasando el callback de
                // 2 parametros forzamos el overload Task-returning, que el SDK SI espera.
                .WithCallbackStream(async (source, innerCt) => await source.CopyToAsync(destination, innerCt)),
            ct
        );

    public Task CopyAsync(string sourceBucket, string objectKey, string destinationBucket, CancellationToken ct) =>
        client.CopyObjectAsync(
            new CopyObjectArgs()
                .WithBucket(destinationBucket)
                .WithObject(objectKey)
                .WithCopyObjectSource(new CopySourceObjectArgs().WithBucket(sourceBucket).WithObject(objectKey)),
            ct
        );

    public Task CopyAsync(
        string sourceBucket,
        string sourceObjectKey,
        string destinationBucket,
        string destinationObjectKey,
        CancellationToken ct
    ) =>
        client.CopyObjectAsync(
            new CopyObjectArgs()
                .WithBucket(destinationBucket)
                .WithObject(destinationObjectKey)
                .WithCopyObjectSource(new CopySourceObjectArgs().WithBucket(sourceBucket).WithObject(sourceObjectKey)),
            ct
        );

    public Task DeleteAsync(string bucket, string objectKey, CancellationToken ct) =>
        client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), ct);
}

public sealed class MinioBucketBootstrapper(IMinioClient client, IOptions<CloudStorageOptions> storageOptions)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await EnsureBucketsAsync(cancellationToken);
                return;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    private async Task EnsureBucketsAsync(CancellationToken cancellationToken)
    {
        var options = storageOptions.Value;
        foreach (var bucket in new[] { options.MainBucket, options.TempBucket, options.QuarantineBucket })
        {
            var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken);
            if (!exists)
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken);
        }

        await client.SetVersioningAsync(
            new SetVersioningArgs().WithBucket(options.MainBucket).WithVersioningEnabled(),
            cancellationToken
        );

        // Fase D0 — defensa en profundidad: SaveFileFromSourceHandler borra el objeto
        // fuente tras copiarlo, pero si ese borrado falla (o un servicio llamador nunca
        // llega a publicar el evento tras el PUT) esta regla lo limpia sola a las 24h en
        // vez de dejar basura indefinida bajo el prefijo de cada servicio.
        var lifecycle = new LifecycleConfiguration([
            new LifecycleRule(
                abortIncompleteMultipartUpload: null,
                id: "taxvision-temp-24h-ttl",
                expiration: new Expiration { Days = 1 },
                transition: null,
                filter: null,
                noncurrentVersionExpiration: null,
                noncurrentVersionTransition: null,
                status: LifecycleRule.LifecycleRuleStatusEnabled
            ),
        ]);
        await client.SetBucketLifecycleAsync(
            new SetBucketLifecycleArgs().WithBucket(options.TempBucket).WithLifecycleConfiguration(lifecycle),
            cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
