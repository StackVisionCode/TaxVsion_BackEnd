using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.DataModel.ILM;
using Minio.DataModel.Tags;
using Minio.Exceptions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class MinioObjectStorage(IMinioClient client, [FromKeyedServices("public")] IMinioClient publicClient)
    : IObjectStorage
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

        // URL presignada -> debe apuntar al dominio publico que el navegador puede
        // alcanzar directo, no a la red interna de Docker.
        var (url, formData) = await publicClient.PresignedPostPolicyAsync(policy);

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
        var url = await publicClient.PresignedGetObjectAsync(
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
        var url = await publicClient.PresignedGetObjectAsync(
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

    public Task SetObjectTagsAsync(
        string bucket,
        string objectKey,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    ) =>
        client.SetObjectTagsAsync(
            new SetObjectTagsArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithTagging(new Tagging(tags.ToDictionary(t => t.Key, t => t.Value), isObjects: true)),
            ct
        );

    public async Task DeleteAllVersionsAsync(string bucket, string objectKey, CancellationToken ct)
    {
        // El listado por prefijo trae también claves que empiezan igual: solo se borra la clave exacta.
        var versions = new List<string>();
        await foreach (
            var item in client.ListObjectsEnumAsync(
                new ListObjectsArgs().WithBucket(bucket).WithPrefix(objectKey).WithRecursive(true).WithVersions(true),
                ct
            )
        )
        {
            if (string.Equals(item.Key, objectKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(item.VersionId))
                versions.Add(item.VersionId);
        }

        foreach (var versionId in versions)
            await client.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey).WithVersionId(versionId),
                ct
            );

        // Sin versiones listadas (bucket sin versionado): borrado normal, idempotente.
        if (versions.Count == 0)
            await DeleteAsync(bucket, objectKey, ct);
    }
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

        await EnsureMainBucketAvatarRuleAsync(options.MainBucket, cancellationToken);
    }

    // Se mezcla con las reglas que ya tenga el bucket principal (un Set reemplaza la config entera).
    private async Task EnsureMainBucketAvatarRuleAsync(string mainBucket, CancellationToken cancellationToken)
    {
        LifecycleConfiguration? current;
        try
        {
            current = await client.GetBucketLifecycleAsync(
                new GetBucketLifecycleArgs().WithBucket(mainBucket),
                cancellationToken
            );
        }
        catch (MinioException ex) when (MainBucketLifecycle.IsMissingConfiguration(ex))
        {
            current = null;
        }

        var merged = MainBucketLifecycle.MergeAvatarRule(current);
        if (merged is null)
            return;
        await client.SetBucketLifecycleAsync(
            new SetBucketLifecycleArgs().WithBucket(mainBucket).WithLifecycleConfiguration(merged),
            cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Regla de lifecycle del bucket principal (versionado): las versiones NO actuales de objetos con la
/// etiqueta kind=avatar expiran al día. Así una foto de perfil borrada o reemplazada no queda para
/// siempre como versión vieja. Los demás documentos conservan su historial de versiones intacto.
/// </summary>
public static class MainBucketLifecycle
{
    public const string AvatarRuleId = "taxvision-avatars-noncurrent-1d";

    public static LifecycleRule AvatarRule() =>
        new(
            abortIncompleteMultipartUpload: null,
            id: AvatarRuleId,
            expiration: null,
            transition: null,
            filter: new RuleFilter
            {
                // And(prefijo + etiqueta): forma XML estándar de S3 para filtrar por etiqueta.
                TheAndOperator = new AndOperator(
                    "tenants/",
                    new Dictionary<string, string> { [StorageObjectTags.KindKey] = StorageObjectTags.AvatarKind }
                ),
            },
            noncurrentVersionExpiration: new NoncurrentVersionExpiration(1, null),
            noncurrentVersionTransition: null,
            status: LifecycleRule.LifecycleRuleStatusEnabled
        );

    /// <summary>Null si la regla ya está tal cual (no hace falta escribir); si no, la config con la regla agregada o reemplazada.</summary>
    public static LifecycleConfiguration? MergeAvatarRule(LifecycleConfiguration? current)
    {
        var rules = current?.Rules?.ToList() ?? [];
        var existing = rules.FirstOrDefault(r => r.ID == AvatarRuleId);
        if (existing is not null && IsUpToDate(existing))
            return null;

        rules.RemoveAll(r => r.ID == AvatarRuleId);
        rules.Add(AvatarRule());
        return new LifecycleConfiguration(rules);
    }

    /// <summary>
    /// Bucket sin lifecycle. El SDK 7.0.0 no lo reporta como ErrorResponseException sino como
    /// UnexpectedMinioException con este texto (medido contra MinIO local); se aceptan las dos formas.
    /// Cualquier otro error se propaga: tratarlo como "vacío" pisaría las reglas existentes.
    /// </summary>
    public static bool IsMissingConfiguration(MinioException ex) =>
        IsMissingConfiguration((ex as ErrorResponseException)?.Response?.Code)
        || ex.Message.Contains("lifecycle configuration does not exist", StringComparison.OrdinalIgnoreCase)
        || (
            ex.ServerMessage?.Contains("lifecycle configuration does not exist", StringComparison.OrdinalIgnoreCase)
            ?? false
        );

    public static bool IsMissingConfiguration(string? errorCode) =>
        string.Equals(errorCode, "NoSuchLifecycleConfiguration", StringComparison.Ordinal);

    private static bool IsUpToDate(LifecycleRule rule) =>
        rule.Status == LifecycleRule.LifecycleRuleStatusEnabled
        && rule.NoncurrentVersionExpirationObject?.NoncurrentDays == 1
        && rule.Filter?.TheAndOperator?.Tags?.Any(t =>
            t.Key == StorageObjectTags.KindKey && t.Value == StorageObjectTags.AvatarKind
        ) == true;
}
