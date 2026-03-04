using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Placeholder: service registrations will be added by subsequent plans.

var app = builder.Build();

await app.RunAsync();
