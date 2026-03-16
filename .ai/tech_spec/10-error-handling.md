# 10. Error Handling

- All exceptions are caught at the top-level command executor and converted to the JSON error envelope written to stderr.
- `--no-input` flag: any code path that would prompt must throw `InvalidOperationException` with code `INVALID_ARGS` instead.
- HTTP errors from `ApiClient`: non-2xx responses are converted to `API_ERROR` with the response body included in `"detail"`.
- Cancellation (`Ctrl+C`): graceful cancellation via `CancellationTokenSource`; exit code `1`.
