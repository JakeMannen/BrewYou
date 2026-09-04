var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var db = postgres.AddDatabase("brewyou-db");

var apiService = builder.AddProject<Projects.BrewYou_ApiService>("apiservice")
    .WithReference(db)
    .WaitFor(db);

builder.AddNpmApp("web", "../BrewYou.Web", "dev")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();

