# RAC compatibility boundary

RasGate remains a domain-agnostic process executor. It exposes technical RAC
status and execution results; it does not expose clusters, infobases, sessions,
or other 1C:Enterprise resources.

RasHub Infrastructure owns RAC compatibility:

1. `HttpRasGateClient` reads the RAC version from the existing
   `GET /rac/status` endpoint.
2. The operation resolver selects the adapter with the highest
   `MinimumVersion` that does not exceed the detected RAC version.
3. Read operations resolve their output deserializer independently with the
   same latest-compatible rule. A changed output format therefore does not
   require a duplicate operation adapter while its command remains compatible.
4. The selected `IRacResourceAdapter<T>` builds the read-only RAC command and
   converts the complete execution result to an Application snapshot.
5. Application publishes only snapshots marked `Complete`. Conditional
   publication still checks the RasGate configuration revision, active state,
   and deletion state.

The registered production profiles cover the complete cluster collection, one
cluster by its RAC identifier, and cluster mutations:

| RAC version | Resource | Operation | Schema |
|---|---|---|---:|
| `>= 8.3.27.2214` | `clusters` | `snapshot` | 1 |
| `>= 8.3.27.2214` | `clusters` | `info` | 1 |
| `>= 8.3.27.2214` | `clusters` | `insert` | 1 |
| `>= 8.3.27.2214` | `clusters` | `update` | 1 |
| `>= 8.3.27.2214` | `clusters` | `remove` | 1 |

`8.3.27.2214` is the V1 minimum, not the only accepted build. Later RAC
versions continue to use V1 while their commands and output satisfy its
validation. Versions below the minimum fail closed before an operation is
executed. When a later version changes an operation incompatibly, add a new
adapter with that version as its `MinimumVersion`; overlapping older adapters
remain eligible, and the resolver selects the adapter with the latest
applicable minimum.

`RacClusterOutputV1Deserializer` is the shared V1 field mapper for both
`clusters.snapshot` and `clusters.info`. It implements
`IRacClusterOutputDeserializer`; `RacClusterOutputDeserializerResolver` selects
the latest compatible mapper by `MinimumVersion`. The generic key-value parser
remains independent of cluster schema versions.

`clusters.info` executes `cluster info --cluster=<uuid>` and validates that RAC
returned exactly one cluster with the requested identifier. Its result is
published as a targeted upsert and therefore never removes other cached
clusters. Only `clusters.snapshot` is authoritative for collection deletion.

`clusters.insert` executes `cluster insert` with the required host and port and
the settings supplied by the API caller. A successful result must contain
exactly one `cluster : <uuid>` record. RasHub then reads that cluster through
`clusters.info` and publishes only the validated authoritative snapshot.
`clusters.update` follows the same read-after-write publication rule. Neither
mutation is retried automatically. A failed insert (including an RAC port
conflict) or update leaves the previous local shadow state unchanged.

Optional `--agent-user` and `--agent-pwd` values are accepted for insert and
update. They remain request-scoped and are not persisted or included in logs,
errors, or responses. RAC stdout and stderr are treated as untrusted; the API
returns stable sanitized errors instead of the remote process text.

`clusters.remove` executes `cluster remove --cluster=<uuid>` and adds
`--cluster-user` and `--cluster-pwd` when the API caller supplies cluster
administrator credentials. Credentials remain request-scoped and are not
persisted or included in logs, errors, or responses. The adapter validates only
the process outcome and never publishes RAC output. Automatic retries are
disabled because removal is a remote mutation. After RAC confirms success, the
matching cached cluster is soft-deleted under the same RasGate
configuration-revision guard used by synchronization.

For the initial cluster profile, an empty successful stdout is marked
`Unknown`, not `Complete`. This prevents an ambiguous empty response from
soft-deleting the previously published cluster snapshot.

## Adding support for a changed resource format

If only one resource changes, add an adapter only for that resource. Other
resource adapters continue to serve their existing version ranges.

1. Capture representative successful, empty, malformed, and failed RAC output.
2. Implement the operation adapter for the new minimum version when its command
   or outcome semantics changed. When only the record format changed, add an
   `IRacClusterOutputDeserializer` with the new `MinimumVersion`; keep the
   existing operation adapter.
3. Map the version-specific fields to the normalized Application model.
4. Keep unavailable optional fields `null`; never substitute `0`, `false`, or
   a stale value.
5. Reject missing identity fields, duplicate identifiers, invalid enums, and
   malformed records.
6. Mark a snapshot `Complete` only when the command completed successfully and
   the entire authoritative result was validated.
7. Register the adapter in Infrastructure DI and add resolver/client tests.

Adapters are selected automatically by `MinimumVersion`. Operators do not
configure parser names or compatibility mappings.
