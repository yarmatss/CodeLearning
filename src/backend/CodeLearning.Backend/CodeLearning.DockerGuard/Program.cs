using CodeLearning.DockerGuard;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GuardOptions>().BindConfiguration(GuardOptions.SectionName);
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<DockerSocketClient>();

var app = builder.Build();

var handler = new GuardRequestHandler(
    app.Services.GetRequiredService<IOptions<GuardOptions>>().Value,
    app.Services.GetRequiredService<DockerSocketClient>(),
    app.Services.GetRequiredService<IHttpForwarder>(),
    app.Logger);

app.Run(handler.HandleAsync);

app.Run();
