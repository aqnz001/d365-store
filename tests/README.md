# Tests

Two test projects, split by what they touch.

## Unit (`tests/unit` — `PartsPortal.Tests.Unit`)
Fast, in-process, no I/O. References `integration/shared` (`PartsPortal.Shared`) and
exercises pure logic in isolation: mapping, idempotency keying, saga steps,
correlation propagation, availability-band math. No network, no broker, no clock.
These run on every build and in CI.

```
dotnet test tests/unit
```

## Integration (`tests/integration` — `PartsPortal.Tests.Integration`)
Exercises the system against the **running mock services** — `ivs-sim`,
`odata-sim`, and `pricing-credit-sim` under `mocks/` — over HTTP, asserting the
contracts in `integration/contracts` hold end-to-end (availability bands,
reservation idempotency, pricing/credit, order writeback de-dup).

Currently a single passing smoke test; the real suite is wired up in **T11**,
once the mocks (T3) and contracts (T2) are in place. These tests require the
mock services to be running first (endpoints come from configuration, never
hardcoded) and are intended for CI stages that boot the mocks.

```
# T11: start mocks, then
dotnet test tests/integration
```

## Coverage of negative paths
Per project conventions, suites must cover the unhappy cases as they are built:
shortfall, duplicate (idempotent replay), transient vs. permanent failure, and
credit hold. Note here as they are added.
