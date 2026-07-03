var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var bankDb = postgres.AddDatabase("bank");
postgres.AddDatabase("openfga");
postgres.AddDatabase("entitlements");
postgres.AddDatabase("governance");
postgres.AddDatabase("audit");

builder.AddProject<Projects.AuthzEntitlements_Bank_Api>("bank-api")
    .WithReference(bankDb)
    .WaitFor(bankDb);

builder.Build().Run();
