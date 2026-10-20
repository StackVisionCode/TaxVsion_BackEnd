// Wolverine publica los eventos .NET en camelCase sobre RabbitMQ (JsonNamingPolicy.CamelCase);
// la variante PascalCase se mantiene como fallback para fixtures escritos a mano y para servicios
// que todavía no apliquen el serializer configurado. Siempre priorizar camelCase: refleja el cable
// real y evita un lookup innecesario en el 99% de los mensajes vivos.
export function readDotNetField(
  payload: Record<string, unknown>,
  pascalCaseName: string,
  camelCaseName: string,
): unknown {
  if (Object.prototype.hasOwnProperty.call(payload, camelCaseName)) {
    return payload[camelCaseName];
  }
  return payload[pascalCaseName];
}
