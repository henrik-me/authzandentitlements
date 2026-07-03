var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

postgres.AddDatabase("bank");
postgres.AddDatabase("openfga");
postgres.AddDatabase("entitlements");
postgres.AddDatabase("governance");
postgres.AddDatabase("audit");

builder.Build().Run();
