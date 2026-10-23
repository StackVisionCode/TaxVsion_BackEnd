// Reenvío de mensajes de wolverine-dead-letter-queue a su cola de origen (README §48.3).
//
// Por qué no basta un shovel: al mandar un mensaje de un inbox durable a la DLQ, Wolverine deja su
// fila en wolverine_incoming_envelopes como Handled durante KeepAfterMessageHandling (5 min). Un
// shovel conserva el message_id, así que dentro de esa ventana el inbox lo toma por duplicado, lo
// confirma y lo descarta: el mensaje se pierde (medido en T-7 de Client Forms, 2026-10-23).
//
// Esta herramienta reenvía con un message_id NUEVO. Es seguro porque un mensaje en la DLQ nunca se
// confirmó: todos sus intentos se revirtieron. El id original viaja en la cabecera replayed-from-id.
// Cada mensaje se saca de la DLQ solo después de que el broker confirma el reenvío.
//
// Uso (RABBITMQ_URI en el entorno, p. ej. amqp://usuario:clave@localhost:5672):
//   dotnet run --project tools/DlqReplay -- list [--type <texto>] [--source <texto>]
//   dotnet run --project tools/DlqReplay -- replay --queue <cola-de-origen> (--id <id>[,<id>...] | --type <texto> [--source <texto>]) [--dry-run]
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

const string DeadLetterQueue = "wolverine-dead-letter-queue";
string[] droppedHeaders = ["exception-type", "exception-message", "exception-stack", "failed-at"];

if (args.Length == 0 || args[0] is not ("list" or "replay"))
    return Usage();

var mode = args[0];
var ids = Option("--id")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet();
var type = Option("--type");
var source = Option("--source");
var queue = Option("--queue");
var dryRun = args.Contains("--dry-run");

if (mode == "replay" && (queue is null || (ids is null && type is null)))
    return Usage();

var uri = Environment.GetEnvironmentVariable("RABBITMQ_URI");
if (string.IsNullOrWhiteSpace(uri))
{
    Console.Error.WriteLine("Falta RABBITMQ_URI.");
    return 2;
}

var factory = new ConnectionFactory { Uri = new Uri(uri) };
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync(
    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)
);

if (mode == "replay")
    await channel.QueueDeclarePassiveAsync(queue!);

var expected = (await channel.QueueDeclarePassiveAsync(DeadLetterQueue)).MessageCount;
await channel.BasicQosAsync(0, (ushort)Math.Min(expected + 100, ushort.MaxValue), false);

// Se reciben todos sin confirmar; los no elegidos vuelven a la DLQ con nack(requeue) al final.
var received = new List<BasicDeliverEventArgs>();
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += (_, ea) =>
{
    lock (received)
        received.Add(
            new BasicDeliverEventArgs(
                ea.ConsumerTag,
                ea.DeliveryTag,
                ea.Redelivered,
                ea.Exchange,
                ea.RoutingKey,
                ea.BasicProperties,
                ea.Body.ToArray(),
                ea.CancellationToken
            )
        );
    return Task.CompletedTask;
};
var consumerTag = await channel.BasicConsumeAsync(DeadLetterQueue, autoAck: false, consumer);
var deadline = DateTime.UtcNow.AddSeconds(15);
while (Count() < expected && DateTime.UtcNow < deadline)
    await Task.Delay(100);
await channel.BasicCancelAsync(consumerTag);

Console.WriteLine($"{DeadLetterQueue}: {expected} mensajes, {received.Count} leídos.");
var replayed = 0;
foreach (var message in received)
{
    var props = message.BasicProperties;
    var messageType = props.Type ?? Header(props, "message-type") ?? "?";
    var selected = Selected(props, messageType);

    if (mode == "list")
    {
        if (selected)
            Console.WriteLine(
                $"{props.MessageId} | {messageType} | source={Header(props, "source")} | attempts={Header(props, "attempts")} | {Trim(Header(props, "exception-message"))}"
            );
    }
    else if (selected && dryRun)
    {
        Console.WriteLine($"[dry-run] {props.MessageId} ({messageType}) -> {queue}");
    }
    else if (selected)
    {
        var newId = Guid.NewGuid().ToString();
        await channel.BasicPublishAsync("", queue!, mandatory: true, Replay(props, newId), message.Body);
        await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
        replayed++;
        Console.WriteLine($"reenviado {props.MessageId} como {newId} ({messageType}) -> {queue}");
        continue;
    }

    await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true);
}

if (mode == "replay")
    Console.WriteLine(dryRun ? "dry-run: no se movió nada." : $"reenviados: {replayed}");
return 0;

int Count()
{
    lock (received)
        return received.Count;
}

bool Selected(IReadOnlyBasicProperties props, string messageType) =>
    (ids is null || (props.MessageId is not null && ids.Contains(props.MessageId)))
    && (type is null || messageType.Contains(type, StringComparison.OrdinalIgnoreCase))
    && (source is null || (Header(props, "source") ?? "").Contains(source, StringComparison.OrdinalIgnoreCase));

BasicProperties Replay(IReadOnlyBasicProperties original, string newId)
{
    var headers = new Dictionary<string, object?>();
    foreach (var (key, value) in original.Headers ?? new Dictionary<string, object?>())
        if (!droppedHeaders.Contains(key))
            headers[key] = value;
    headers["attempts"] = Encoding.UTF8.GetBytes("0");
    headers["replayed-from-id"] = Encoding.UTF8.GetBytes(original.MessageId ?? "");
    headers["replayed-at"] = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));

    return new BasicProperties(original) { MessageId = newId, Headers = headers };
}

static string? Header(IReadOnlyBasicProperties props, string key) =>
    props.Headers is not null && props.Headers.TryGetValue(key, out var value)
        ? value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            null => null,
            _ => value.ToString(),
        }
        : null;

static string Trim(string? text) =>
    text is null ? ""
    : text.Length > 140 ? text[..140]
    : text;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine(
        "Uso:\n"
            + "  list [--type <texto>] [--source <texto>]\n"
            + "  replay --queue <cola-de-origen> (--id <id>[,<id>...] | --type <texto> [--source <texto>]) [--dry-run]"
    );
    return 2;
}
