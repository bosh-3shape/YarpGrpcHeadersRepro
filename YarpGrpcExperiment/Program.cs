using System.Net;
using System.Net.Security;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy.Forwarder;
using YarpGrpcExperiment;
using YarpGrpcExperiment.Server;
using YarpTest;

var reverseProxyApp = BuildProxyAsync();
var serverApp = BuildServerAsync();

await DirectCall_Succeeds();
await ProxiedCall_WithMessageAfterHeaders_Succeeds();
await ProxiedCall_DoesNotFinish();

Console.WriteLine("DONE");

await Task.WhenAll(reverseProxyApp, serverApp);

// The scenario is the following:
// 1. The client sends the headers to the server
// 2. The server replies back to the client with some headers, and then waits for a message from the client.
// 3. The client waits for the headers from the server.
// 4. Once the client receives the headers, it sends a message to the server.
// 5. The server replies back with a message.
async Task DirectCall_Succeeds()
{
    using var channelDirectly = GrpcChannel.ForAddress("https://127.0.0.1:10000", new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }
    });
    var client = new Service.ServiceClient(channelDirectly);

    using var call = client.ExchangeHeadersOnly(new Metadata {{"client-header", "client-value"}});

    var serverHeaders = await call.ResponseHeadersAsync;

    await call.RequestStream.WriteAsync(new Message {Message_ = "Client Request"});

    // Direct call to the Server succeeds: the headers are successfully exchanged without any gRPC message sent.
    Console.WriteLine("DONE for direct connection.");
}

// The scenario is the following (it is identical to DirectCall_Succeeds):
// 1. The client sends the headers to the server
// 2. The server replies back to the client with some headers, and then waits for a message from the client.
// 3. The client waits for the headers from the server.
// 4. Once the client receives the headers, it sends a message to the server.
// 5. The server replies back with a message.
async Task ProxiedCall_DoesNotFinish()
{
    using var channelViaProxy = GrpcChannel.ForAddress("https://127.0.0.1:11000", new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }
    });
    var client = new Service.ServiceClient(channelViaProxy);

    using var call = client.ExchangeHeadersOnly(new Metadata {{"client-header", "client-value"}});

    // Execution blocks here and never continues, as YARP does not proxy the headers from the server to the client.
    var serverHeaders = await call.ResponseHeadersAsync;

    // This statement is never executed.
    await call.RequestStream.WriteAsync(new Message {Message_ = "Client Request"});

    // This message is never printed.
    Console.WriteLine("DONE for connection via YARP");
}

// The scenario is the following:
// 1. The client sends the headers to the server
// 2. The server replies back to the client with some headers, _sends a message to the client_,
//    and then waits for a message from the client.
// 3. The client waits for the headers from the server.
// 4. Once the client receives the headers, it sends a message to the server.
// 5. The server replies back with a message.
async Task ProxiedCall_WithMessageAfterHeaders_Succeeds()
{
    using var channelViaProxy = GrpcChannel.ForAddress("https://127.0.0.1:11000", new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }
    });
    var client = new Service.ServiceClient(channelViaProxy);

    using var call = client.ExchangeHeadersAndSendMessage(new Metadata {{"client-header", "client-value"}});

    var serverHeaders = await call.ResponseHeadersAsync;

    await call.ResponseStream.MoveNext();
    var serverMessage = call.ResponseStream;

    await call.RequestStream.WriteAsync(new Message {Message_ = "Client Request"});

    Console.WriteLine("DONE for connection via YARP with a message after headers");
}

static Task BuildServerAsync()
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddGrpc();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(
            IPAddress.Loopback,
            10_000,
            lo =>
            {
                lo.Protocols = HttpProtocols.Http2;
                var (cert, _) = SelfSignedSslCertificateProvider.GetSelfSignedCertificate("Proxy");
                lo.UseHttps(cert);
            });
    });

    var app = builder.Build();


    app.MapGrpcService<ServerService>();
    app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

    return app.RunAsync();
}

static Task BuildProxyAsync()
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddHttpForwarder();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(
            IPAddress.Loopback,
            11_000,
            lo =>
            {
                lo.Protocols = HttpProtocols.Http2;
                var (cert, _) = SelfSignedSslCertificateProvider.GetSelfSignedCertificate("Proxy");
                lo.UseHttps(cert);
            });
    });

    var app = builder.Build();

    app.MapForwarder(
        "/{**catch-all}",
        "https://localhost:10000/",
        new ForwarderRequestConfig
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        },
        HttpTransformer.Default,
        new HttpMessageInvoker(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            }
        }));

    return app.RunAsync();
}