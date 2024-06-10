namespace YarpGrpcExperiment.Server;

using Grpc.Core;
using YarpTest;

public class ServerService : Service.ServiceBase
{
    /// <inheritdoc />
    public override async Task ExchangeHeadersOnly(IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        // Read the client's headers
        var clientHeaders = context.RequestHeaders;

        // Send the headers to the client - we expect them to be sent immediately.
        await context.WriteResponseHeadersAsync(
            new Metadata
            {
                new ("server-header", "server"),
                new ("client-header", clientHeaders.GetValue("client-header")),
            });

        await requestStream.MoveNext();
        var clientMessage = requestStream.Current;

        await responseStream.WriteAsync(new Message {Message_ = "reply-from-server"});
    }

    /// <inheritdoc />
    public override async Task ExchangeHeadersAndSendMessage(IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        // Read the client's headers
        var clientHeaders = context.RequestHeaders;

        // Send the headers to the client
        await context.WriteResponseHeadersAsync(
            new Metadata
            {
                new ("server-header", "server"),
                new ("client-header", clientHeaders.GetValue("client-header")),
            });
        // Send a message to the client
        await responseStream.WriteAsync(new Message {Message_ = "message-from-server"});

        await requestStream.MoveNext();
        var clientMessage = requestStream.Current;

        await responseStream.WriteAsync(new Message {Message_ = "reply-from-server"});
    }
}