using System.Text;
using Microsoft.AspNetCore.Connections;

namespace protohackers;

public class EchoConnectionHandler : ConnectionHandler
{
    private readonly ILogger<EchoConnectionHandler> _logger;

    public EchoConnectionHandler(ILogger<EchoConnectionHandler> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _logger.LogInformation($"{connection.ConnectionId} connected from {connection.RemoteEndPoint}");

        while (true)
        {
            var result = await connection.Transport.Input.ReadAsync();
            var buffer = result.Buffer;

            foreach (var segment in buffer)
            {
                var payload = Encoding.UTF8.GetString(segment.Span);
                _logger.LogTrace($"{connection.ConnectionId} sent {payload}");
                await connection.Transport.Output.WriteAsync(segment);
            }

            if (result.IsCompleted)
            {
                break;
            }

            connection.Transport.Input.AdvanceTo(buffer.End);
        }

        _logger.LogInformation(connection.ConnectionId + " disconnected");
    }
}