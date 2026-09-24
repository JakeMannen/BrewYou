var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithPgAdmin();
var db = postgres.AddDatabase("brewyou-db");

var mqtt = builder.AddMosquitto("mqtt", port: 1883)
    .WithDataVolume("brewyou_mqttdata");

var apiService = builder.AddProject<Projects.BrewYou_ApiService>("apiservice")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(mqtt)
    .WaitFor(mqtt)
    .WithHttpEndpoint(port: 5000)
    .WithUrl("/scalar/v1", "Scalar");

builder.AddNpmApp("web", "../BrewYou.Web", "dev")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();