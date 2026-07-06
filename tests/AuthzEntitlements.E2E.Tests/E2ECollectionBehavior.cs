using Xunit;

// CS58 — disable xUnit assembly-level parallelization for this project. Both the CS57 smoke e2e
// and the CS58 authenticated e2e boot the FULL Aspire stack, and the CS58 test additionally pins
// Keycloak to the fixed host port 8088 (DcpPublisher:RandomizePorts=false) so the token
// issuer/JWKS/authority all agree. Running two full-stack boots at once would thrash Docker and
// collide on 8088, so the two e2e facts must run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
