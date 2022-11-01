using System.Buffers;
using System.Text;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.AspNetCore.Connections;

namespace protohackers;

public class PrimeTimeHandler : ConnectionHandler
{
    private static readonly byte[] newline = new byte[] { (byte)'\n' };
    private readonly ILogger<PrimeTimeHandler> _logger;
    private readonly JsonSerializerOptions options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public PrimeTimeHandler(ILogger<PrimeTimeHandler> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _logger.LogTrace($"{connection.ConnectionId} connected from {connection.RemoteEndPoint}");

        while (true)
        {
            var result = await connection.Transport.Input.ReadAsync();

            var position = ProcessItems(result.Buffer, result.IsCompleted, connection);

            if (result.IsCompleted || connection.ConnectionClosed.IsCancellationRequested)
                break;

            connection.Transport.Input.AdvanceTo(position);
            await connection.Transport.Output.FlushAsync();
        }
    }

    private SequencePosition ProcessItems(in ReadOnlySequence<byte> input, bool isCompleted, ConnectionContext context)
    {
        var reader = new SequenceReader<byte>(input);

        while (!reader.End)
        {
            if (reader.TryReadTo(out ReadOnlySequence<byte> itemBytes, newline, advancePastDelimiter: true))
            {
                _logger.LogTrace($"Received request");
                if (!TryProcess(itemBytes, context.Transport.Output))
                {
                    context.Abort();
                    break;
                }

            }
            else
            {
                break;
            }
        }

        return reader.Position;
    }

    private bool TryProcess(ReadOnlySequence<byte> input, PipeWriter output)
    {
        _logger.LogTrace($"Raw request: {Encoding.UTF8.GetString(input)}");
        var reader = new Utf8JsonReader(input);
        var writer = new Utf8JsonWriter(output);
        Request? request = null;
        try
        {
            request = JsonSerializer.Deserialize<Request>(ref reader, options);
        }
        catch (Exception e)
        {
            _logger.LogTrace($"Error reading JSON: {e}");
            return false;
        }

        // Validation
        if (!request.HasValue || request.Value.Method != "isPrime")
        {
            _logger.LogWarning($"Disconnecting client for malformed request");
            return false;
        }

        _logger.LogTrace($"Valid request: {request}");

        var response = new Response { Method = "isPrime", Prime = IsPrime(request.Value.Number) };
        JsonSerializer.Serialize(writer, response, options);
        writer.Flush();
        output.Write(newline);

        return true;
    }

    public static bool IsPrime(double number)
    {
        if (number % 1 != 0) return false;
        if (number <= 1) return false;
        if (number == 2) return true;
        if (number % 2 == 0) return false;

        var boundary = (int)Math.Floor(Math.Sqrt(number));

        for (int i = 3; i <= boundary; i += 2)
            if (number % i == 0)
                return false;

        return true;
    }

    private record struct Request
    {
        public required string Method { get; init; }
        public required double Number { get; init; }
    }

    private record struct Response
    {
        public required string Method { get; init; }
        public required bool Prime { get; init; }
    }
}