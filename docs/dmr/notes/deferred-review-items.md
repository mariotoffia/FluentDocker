# Deferred hardening items (from Phase 3/4 code review)

Applied: C1 (HttpClient.Timeout=Infinite for inference/SSE), N3 (propagate
OperationCanceledException in CLI mgmt/runtime catch blocks), S4 (guard
non-string JSON id/type/message in ModelJsonParser).

Deferred (lower-risk / bounded impact — revisit if needed):
- **S1/S2**: `ExecuteStreamingCommandAsync` (shared base) does not surface the
  process exit code / stderr, so a failed `docker model pull`/`logs` ends the
  stream silently. CLI `PullAsync` mitigates by confirming via `InspectAsync`
  (genuine new-pull failures are caught; a failed *re-pull* of an already-present
  model is reported as success, which is acceptable since the model is available).
  Changing the shared base class risks existing stream drivers — defer.
- **S3**: native `/models/create` NDJSON error events aren't surfaced as failures
  (only progress is parsed). PullAsync confirms via inspect. Consider parsing
  `type:"error"` events later.
- **S5**: `StatusAsync` running-detection is substring-based ("is running") and
  does not distinguish "installed-but-down" from "not installed"
  (RunnerNotInstalled). Adequate for v1; revisit with richer status parsing.
