import { Client as MinioClient } from 'minio';
import { config } from '../config.js';

/**
 * Fase D2 — credenciales MinIO propias (IAM scoped a s3:PutObject en
 * taxvision-temp/transcript/*, ver deploy/docker/minio/policies/transcript-source.json).
 * Nunca las credenciales root de CloudStorage.
 */
export const minioClient = new MinioClient({
  endPoint: config.minio.endpoint,
  port: config.minio.port,
  useSSL: config.minio.useSSL,
  accessKey: config.minio.accessKey,
  secretKey: config.minio.secretKey,
});

export async function putSourceObject(objectKey: string, filePath: string, contentType: string): Promise<void> {
  await minioClient.fPutObject(config.minio.tempBucket, objectKey, filePath, { 'Content-Type': contentType });
}
