# RAC compatibility boundary

RasGate remains a domain-agnostic process executor. It exposes technical RAC
status and execution results; it does not expose clusters, infobases, sessions,
or other 1C:Enterprise resources.

Application handlers reach the remote boundary through resource-specific
ports: `IRasGateStatusGateway`, `IRasClusterGateway`, and
`IRasInfobaseGateway`. Infrastructure implements those ports without exposing
RasGate transport DTOs or RAC output to Application.

For each remote operation, the Infrastructure gateway creates an internal
session bound to the loaded RasGate endpoint, API key, identifier, and
configuration revision. The session owns the shared HTTP envelope, sanitized
protocol errors, RAC execution outcome, RAC-version lookup, and capability
catalog. Resource gateways own command/adapter selection and output
interpretation; adding a resource does not add dependencies to
`RasGateSessionFactory`.

The status gateway uses the same session for `GET /rasgate/status` but does not
resolve a RAC version or an operation adapter. Compatibility-aware RAC resource
operations proceed as follows:

1. The session reads the RAC version from `GET /rac/status`. The shared cache is
   keyed by `(RasGateId, ConfigurationRevision)` and expires after five minutes.
2. The operation resolver selects the adapter with the highest
   `MinimumVersion` that does not exceed the detected RAC version.
3. Read operations resolve their output deserializer independently with the
   same latest-compatible rule. A changed output format therefore does not
   require a duplicate operation adapter while its command remains compatible.
4. The selected operation adapter builds the RAC command and validates the full
   execution result. Result-producing adapters also map it to a normalized
   Application model.
5. A RAC deserialization mismatch invalidates both the session-local version
   and the shared cache entry so a later attempt resolves compatibility again.
6. Application publishes collection snapshots only when marked `Complete`.
   Targeted `info` operations additionally require exactly one matching item.
7. Conditional publication still checks the RasGate configuration revision,
   active state, and deletion state.

The registered production profiles cover clusters and cluster-scoped
infobases:

| RAC version | Resource | Operation | Schema |
|---|---|---|---:|
| `>= 8.3.27.2214` | `clusters` | `snapshot` | 1 |
| `>= 8.3.27.2214` | `clusters` | `info` | 1 |
| `>= 8.3.27.2214` | `clusters` | `insert` | 1 |
| `>= 8.3.27.2214` | `clusters` | `update` | 1 |
| `>= 8.3.27.2214` | `clusters` | `remove` | 1 |
| `>= 8.3.27.2214` | `infobases` | `snapshot` | 1 |
| `>= 8.3.27.2214` | `infobases` | `info` | 1 |

Capability preflight is derived from all registered
`IRacResourceAdapterDescriptor` implementations for the detected RAC version;
handlers check the operations needed by their resource before executing them.

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
remains independent of resource schema versions.

`RacInfobaseOutputV1Deserializer` performs the same role for
`infobases.snapshot` and `infobases.info` through
`IRacInfobaseOutputDeserializer` and
`RacInfobaseOutputDeserializerResolver`. Its normalized summary contains the
remote identifier, name, and description.

`clusters.info` executes `cluster info --cluster=<uuid>` and validates that RAC
returned exactly one cluster with the requested identifier. Its result is
published as a targeted upsert and therefore never removes other cached
clusters. Only `clusters.snapshot` is authoritative for collection deletion;
removing an absent cluster also invalidates its cached infobases.

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
matching cached cluster and its infobases are soft-deleted under the same
RasGate configuration-revision guard used by synchronization.

`infobases.snapshot` executes
`infobase summary list --cluster=<cluster-uuid>`. A complete result reconciles
only the infobases owned by that cached cluster and may soft-delete absent
siblings. `infobases.info` adds `--infobase=<infobase-uuid>`, requires exactly
one matching result, and performs a targeted upsert without changing siblings.
Both operations may carry request-scoped `--cluster-user` and `--cluster-pwd`
credentials; those values are not persisted or logged.

For the initial cluster and infobase collection profiles, an empty successful
stdout is marked `Unknown`, not `Complete`. This prevents an ambiguous empty
response from destructively reconciling a previously published snapshot.

## Adding support for a changed resource format

If only one resource changes, add an adapter only for that resource. Other
resource adapters continue to serve their existing version ranges.

1. Capture representative successful, empty, malformed, and failed RAC output.
2. Implement the operation adapter for the new minimum version when its command
   or outcome semantics changed. When only the record format changed, add the
   resource-specific output deserializer with the new `MinimumVersion`; keep
   the existing operation adapter.
3. Map the version-specific fields to the normalized Application model.
4. Keep unavailable optional fields `null`; never substitute `0`, `false`, or
   a stale value.
5. Reject missing identity fields, duplicate identifiers, invalid enums, and
   malformed records.
6. Mark a snapshot `Complete` only when the command completed successfully and
   the entire authoritative result was validated.
7. Register a new operation adapter both through its typed interface and as an
   `IRacResourceAdapterDescriptor`; the latter makes it visible to capability
   preflight. Register a new output deserializer through its resource-specific
   deserializer interface.
8. Add resolver, gateway/session, and handler publication tests.

Adapters are selected automatically by `MinimumVersion`. Operators do not
configure parser names or compatibility mappings.
