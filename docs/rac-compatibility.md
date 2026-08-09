# RAC compatibility boundary

RasGate remains a domain-agnostic process executor. It exposes technical RAC
status and execution results; it does not expose clusters, infobases, sessions,
or other 1C:Enterprise resources.

RasHub Infrastructure owns RAC compatibility:

1. `HttpRasGateClient` reads the RAC version from the existing
   `GET /rac/status` endpoint.
2. `RacResourceAdapterResolver<T>` selects the highest supported schema for
   the resource, operation, and detected RAC version.
3. The selected `IRacResourceAdapter<T>` builds the read-only RAC command and
   converts the complete execution result to an Application snapshot.
4. Application publishes only snapshots marked `Complete`. Conditional
   publication still checks the RasGate configuration revision, active state,
   and deletion state.

The first registered production profiles cover the complete cluster collection
and one cluster by its RAC identifier:

| RAC version | Resource | Operation | Schema |
|---|---|---|---:|
| `>= 8.3.27.2214, < 8.4.0.0` | `clusters` | `snapshot` | 1 |
| `>= 8.3.27.2214, < 8.4.0.0` | `clusters` | `info` | 1 |

`8.3.27.2214` is the V1 baseline, not the only accepted build. Later `8.3.x`
versions use the same adapter while their output satisfies its complete
structural validation. Versions below the baseline and `8.4.x` fail closed
before `cluster list` is executed. When a later version breaks the schema, its
version becomes the upper bound of V1 and gets a new adapter/schema backed by
representative output fixtures.

`clusters.info` executes `cluster info --cluster=<uuid>` and validates that RAC
returned exactly one cluster with the requested identifier. Its result is
published as a targeted upsert and therefore never removes other cached
clusters. Only `clusters.snapshot` is authoritative for collection deletion.

For the initial cluster profile, an empty successful stdout is marked
`Unknown`, not `Complete`. This prevents an ambiguous empty response from
soft-deleting the previously published cluster snapshot.

## Adding support for a changed resource format

If only one resource changes, add an adapter only for that resource. Other
resource adapters continue to serve their existing version ranges.

1. Capture representative successful, empty, malformed, and failed RAC output.
2. Implement `IRacResourceAdapter<T>` for the new tested version range.
3. Map the version-specific fields to the normalized Application model.
4. Keep unavailable optional fields `null`; never substitute `0`, `false`, or
   a stale value.
5. Reject missing identity fields, duplicate identifiers, invalid enums, and
   malformed records.
6. Mark a snapshot `Complete` only when the command completed successfully and
   the entire authoritative result was validated.
7. Register the adapter in Infrastructure DI and add resolver/client tests.

Adapters are selected automatically. Operators do not configure parser names
or compatibility mappings.
