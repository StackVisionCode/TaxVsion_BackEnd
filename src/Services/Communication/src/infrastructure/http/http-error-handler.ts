import type { FastifyError, FastifyInstance } from 'fastify';
import { ZodError } from 'zod';

/**
 * Errores no atrapados por las rutas. Un parámetro o cuerpo inválido (`Schema.parse` de zod, o la validación
 * de Fastify) es culpa del cliente → 400 con los campos; antes salía como 500 y ensuciaba las alertas.
 * Un error con `statusCode` 4xx (p. ej. @fastify/sensible) conserva su código. El resto → 500 sin detalles.
 */
export function registerHttpErrorHandler(app: FastifyInstance): void {
  app.setErrorHandler((error: FastifyError | ZodError, request, reply) => {
    if (error instanceof ZodError) {
      return reply.code(400).send({
        code: 'Request.Invalid',
        message: 'The request has invalid values.',
        errors: error.issues.map((issue) => ({ path: issue.path.join('.'), message: issue.message })),
      });
    }
    if (error.validation) {
      return reply.code(400).send({ code: 'Request.Invalid', message: error.message });
    }
    const status = typeof error.statusCode === 'number' ? error.statusCode : 500;
    if (status >= 400 && status < 500) {
      return reply.code(status).send({ code: error.code ?? 'Request.Rejected', message: error.message });
    }
    request.log.error({ err: error }, 'unhandled request error');
    return reply.code(500).send({ code: 'Server.Error', message: 'Unexpected error.' });
  });
}
