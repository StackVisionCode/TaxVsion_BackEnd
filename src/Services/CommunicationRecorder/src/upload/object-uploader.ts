import { Client as MinioClient } from 'minio';

export interface ObjectUploader {
  readonly bucket: string;
  putFile(objectKey: string, filePath: string, contentType: string): Promise<void>;
}

/** Cuenta MinIO propia con política acotada a `{bucket}/{prefix}/*`; nunca las credenciales root. */
export function createMinioUploader(opts: {
  endpoint: string;
  port: number;
  useSSL: boolean;
  accessKey: string;
  secretKey: string;
  bucket: string;
}): ObjectUploader {
  const client = new MinioClient({
    endPoint: opts.endpoint,
    port: opts.port,
    useSSL: opts.useSSL,
    accessKey: opts.accessKey,
    secretKey: opts.secretKey,
  });
  return {
    bucket: opts.bucket,
    async putFile(objectKey, filePath, contentType) {
      await client.fPutObject(opts.bucket, objectKey, filePath, { 'Content-Type': contentType });
    },
  };
}
