using Microsoft.AspNetCore.Connections;
using protohackers;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(8080, builder =>
    {
        builder.UseConnectionHandler<EchoConnectionHandler>();
    });

    options.ListenAnyIP(8081, builder =>
    {
        builder.UseConnectionHandler<PrimeTimeHandler>();
    });

    options.ListenAnyIP(8082, builder =>
    {
        builder.UseConnectionHandler<MeanPriceHandler>();
    });
});

var app = builder.Build();

app.Run();
