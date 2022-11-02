using Microsoft.AspNetCore.Connections;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace protohackers;

public class MeanPriceHandler : ConnectionHandler
{
    private readonly ILogger<MeanPriceHandler> _logger;
    private const int ResponseSizeBytes = 4;
    private const int MessageSizeBytes = 9;
    private ConcurrentDictionary<string, SortedSet<InsertCommand>> priceStore = new();

    public MeanPriceHandler(ILogger<MeanPriceHandler> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _logger.LogInformation($"{connection.ConnectionId} connected from {connection.RemoteEndPoint}");

        while (true)
        {
            var result = await connection.Transport.Input.ReadAtLeastAsync(MessageSizeBytes);

            var position = Process(connection, result.Buffer);
            await connection.Transport.Output.FlushAsync();

            if (result.IsCompleted || connection.ConnectionClosed.IsCancellationRequested)
                break;

            connection.Transport.Input.AdvanceTo(position);
        }

        priceStore.Remove(connection.ConnectionId, out _);
    }

    private SequencePosition Process(ConnectionContext context, ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (reader.TryReadExact(MessageSizeBytes, out var message))
        {
            if (!TryParse(message, out var command))
            {
                context.Abort();
                break;
            }

            // Process command
            ProcessCommand(command, context.ConnectionId, context.Transport.Output);
        }

        return reader.Position;
    }

    private void ProcessCommand(ICommand command, string connectionId, PipeWriter output)
    {
        switch (command)
        {
            case QueryCommand r:
                int average = 0;
                if (r.MinTime > r.MaxTime)
                {
                    average = 0;
                }
                else
                {
                    priceStore.TryGetValue(connectionId, out var rawPrices);
                    var prices = rawPrices?.GetViewBetween(new InsertCommand { Timestamp = r.MinTime, Price = 0 }, new InsertCommand { Timestamp = r.MaxTime, Price = 0 });
                    average = prices is not null & prices.Any() ? Convert.ToInt32(prices.Average(p => p.Price)) : 0;
                }

                Span<byte> buffer = stackalloc byte[ResponseSizeBytes];
                if (!BitConverter.TryWriteBytes(buffer, Convert.ToInt32(average)))
                    throw new Exception("could not write bytes :(");

                if (BitConverter.IsLittleEndian)
                    buffer.Reverse();

                output.Write(buffer);
                break;
            case InsertCommand r:
                priceStore.AddOrUpdate(connectionId, _ => new SortedSet<InsertCommand>(new InsertCommandComparer()) { { r } }, (_, set) =>
                {
                    set.Add(r);
                    return set;
                });
                break;
            default:
                throw new NotSupportedException($"Unknown request type: {command.GetType()}");
        }
    }

    private static bool TryParse(ReadOnlySequence<byte> input, out ICommand? request)
    {
        Span<byte> buffer = stackalloc byte[9];
        input.CopyTo(buffer);

        var requestType = (char)buffer[0];

        var firstBuffer = buffer.Slice(1, 4);
        var secondBuffer = buffer.Slice(5, 4);

        firstBuffer.Reverse();
        secondBuffer.Reverse();

        var first = BitConverter.ToInt32(firstBuffer);
        var second = BitConverter.ToInt32(secondBuffer);

        request = requestType switch
        {
            'I' => new InsertCommand
            {
                Timestamp = first,
                Price = second
            },
            'Q' => new QueryCommand
            {
                MinTime = first,
                MaxTime = second
            },
            _ => null
        };

        return request is not null;
    }

    private interface ICommand { }

    private readonly record struct InsertCommand : ICommand
    {
        public required int Timestamp { get; init; }
        public required int Price { get; init; }
    }

    private readonly record struct QueryCommand : ICommand
    {
        public required int MinTime { get; init; }
        public required int MaxTime { get; init; }
    }

    private class InsertCommandComparer : IComparer<InsertCommand>
    {
        public int Compare(InsertCommand x, InsertCommand y)
        {
            return x.Timestamp.CompareTo(y.Timestamp);
        }
    }
}