var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithPgAdmin();
var db = postgres.AddDatabase("brewyou-db");

var mqtt = builder.AddMosquitto("mqtt", port: 1883)
    .WithDataVolume("brewyou_mqttdata");

var googleClientId = builder.Configuration["Authentication:Google:ClientId"]
    ?? builder.Configuration["GOOGLE_CLIENT_ID"];
var publicGoogleClientId = builder.Configuration["PUBLIC_GOOGLE_CLIENT_ID"]
    ?? googleClientId;

var apiService = builder.AddDotnetProject("apiservice", "../BrewYou.ApiService/BrewYou.ApiService.csproj")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(mqtt)
    .WaitFor(mqtt)
    .WithHttpEndpoint(port: 5000)
    .WithUrl("/scalar/v1", "Scalar")
    .WithEnvironment("NetworkSecurity__AllowPrivateNetworks", builder.Configuration["ALLOW_PRIVATE_NETWORKS"] ?? "true")
    .WithEnvironment("NetworkSecurity__AllowLoopback", builder.Configuration["ALLOW_LOOPBACK_NETWORKS"] ?? "false");

if (!string.IsNullOrWhiteSpace(googleClientId))
{
    apiService.WithEnvironment("Authentication__Google__ClientId", googleClientId);
}

var web = builder.AddNpmApp("web", "../BrewYou.Web", "dev")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("PUBLIC_APP_URL", builder.Configuration["PUBLIC_APP_URL"] ?? "")
    .WithEnvironment("PUBLIC_API_URL", builder.Configuration["PUBLIC_API_URL"] ?? "");

if (!string.IsNullOrWhiteSpace(publicGoogleClientId))
{
    web.WithEnvironment("PUBLIC_GOOGLE_CLIENT_ID", publicGoogleClientId);
}

builder.Build().Run();