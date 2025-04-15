using Microsoft.AspNetCore.ResponseCompression;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Samples.Blazor.Components;
using OpenSleigh.InMemory;
using OpenSleigh.Samples.Blazor.Sagas;
using OpenSleigh.Samples.Blazor.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

builder.Services.AddOpenSleigh(cfg =>
{
    cfg.UseInMemoryPersistence()
        .UseInMemoryTransport();

    cfg.AddSaga<StepsSaga, StepsSagaState>();
});

var app = builder.Build();
app.UseResponseCompression();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<SagaHub>("/sagahub");

app.Run();
