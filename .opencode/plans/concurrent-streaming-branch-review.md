# Concurrent Streaming Branch Review

Reviewed branch: `feature/concurrent-streaming`

Base branch: `dev`

Merge base: `2c34b747b904417c7f79c5bf87fa2eef782d7953`

Diff size: 62 files, about `7749` insertions and `1045` deletions.

## Verdict

Do not trust or merge this branch as production-quality yet.

The branch has useful ideas for concurrent/framed batch loading, but it is not yet a clean, reliable, high-quality implementation of highly effective parallel data loading.

The biggest blockers are:

- Server-side read failures can still be swallowed and turned into apparently successful output.
- The v2 stream protocol has no explicit success/error/completion semantics.
- High-level clients still buffer full results.
- Important cleanup/background-streaming paths are fragile.
- Tests do not cover the dangerous streaming failure modes.
- The branch has formatting/cleanup issues and mixes too many unrelated areas.

## Findings

## 1. High: Source read failures can become valid-looking stream output

Relevant code:

- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:512-515`
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:517-528`
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:1048-1054`
- `src/Nexus/API/v2/DataController.cs:40-43`

In `ReadOriginalAsync`, source exceptions are logged and swallowed:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Read original data period {Begin} to {End} failed", begin, end);
}
```

Then the code still completes every read request:

```csharp
foreach (var (readUnit, readRequestManager) in tuples)
{
    var readRequest = readRequestManager.Request;
    readingTasks.Add(readRequest.CompleteAsync());
}

await Task.WhenAll(readingTasks).ConfigureAwait(false);
```

This is dangerous. If a source fails, the data/status buffers may remain zeroed or partially filled. Completion still flushes those buffers through the pipeline. Depending on status handling, the client may receive `NaN` values or partial output rather than a stream failure.

The outer chunk loop also catches and logs generic exceptions:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Process period {Begin} to {End} failed", currentBegin, currentEnd);
}
```

Then it waits for all tasks and continues to the next chunk.

This is the strongest reason not to trust the branch. For a streaming API, once `DataController.GetStreamAsync()` returns:

```csharp
var stream = await _dataService.ReadBatchAsStreamAsync(request, cancellationToken);
return File(stream, "application/octet-stream", "data.bin");
```

the HTTP response is already committed as success. Late source failures must be represented reliably. The current design does not do that.

Recommended changes:

- For v2, do not swallow source exceptions in `ReadOriginalAsync` or the chunk-processing loop.
- On first source failure, fault the relevant pipe or emit an explicit error frame.
- Avoid silently falling back to `CompleteAsync()` after a failed source read.
- Add tests where a source throws after partially filling one resource.

## 2. High: The v2 protocol cannot prove successful completion

Relevant code:

- `src/Nexus/Services/DataService.cs:269-328`
- `src/clients/python/nexus_api/_client.py:276-339`

The frame format is effectively:

```text
int32 resourceIndex
int32 payloadLength
payload bytes
```

There is no:

- protocol version;
- stream header;
- resource count;
- expected byte lengths;
- per-resource completion frame;
- final success frame;
- error frame;
- checksum;
- trailer.

The Python client compensates by precomputing expected lengths from catalog metadata:

```python
expected_lengths = [
    int((end - begin) / catalog_item_map[path].representation.sample_period) * 8
    for path in resource_path_list]
```

and later checks:

```python
if offsets != expected_lengths:
    raise Exception("The batch stream ended before all data was received.")
```

That is better than nothing, but the protocol itself is under-specified. Raw v2 consumers must know to perform the same separate catalog lookup and length calculation. If the server completes early in a way that matches expected byte counts but contains invalid/fallback data, there is no protocol-level signal.

Recommended changes:

- Add a protocol header with version, resource count, and expected lengths.
- Add explicit per-resource completion or stream-final completion.
- Add explicit error frames for failures after the HTTP status is committed.
- Document the binary protocol in API docs and generated client docs.
- Add conformance tests for truncation, malformed frames, invalid resource indexes, invalid lengths, and mid-frame EOF.

## 3. High: This is not true end-to-end streaming

Relevant code:

- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:416-528`
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:977-1074`
- `src/clients/python/nexus_api/_client.py:223-240`
- `src/clients/python/nexus_api/_client.py:282-339`

The implementation streams framed bytes over HTTP, but the overall system still buffers heavily.

Server side:

- `ReadRequestManager` allocates data/status buffers for a whole chunk.
- `ReadOriginalAsync()` passes a batch of `ReadRequest`s into each data source.
- Early per-resource flushing only happens if the source cooperatively calls `ReadRequest.CompleteAsync()`.
- If the source does not call `CompleteAsync()`, flushing happens after `ReadAsync()` returns.
- Time is still processed sequentially chunk-by-chunk in `ReadCoreAsync()`.

Client side:

```python
buffers = [bytearray(length) for length in expected_lengths]
```

The Python client allocates full buffers for every requested resource before returning arrays. The async client has the same pattern.

So this is not highly effective parallel streaming in the strong sense. It is framed batch transport with optional early per-resource flush.

Recommended changes:

- Keep the current `load()` as a convenience buffered API.
- Add lower-level client APIs that yield frames/chunks asynchronously.
- Make the API name/docs clear that high-level clients buffer full results.
- If the goal is real streaming, design from source to transport to client iterator, not only HTTP framing.

## 4. High: Background stream completion is fire-and-forget and cleanup is not robust

Relevant code:

- `src/Nexus/Services/DataService.cs:219`
- `src/Nexus/Services/DataService.cs:330-380`

The service starts stream completion in the background:

```csharp
_ = CompleteAsync(reading, pumping, readingGroups, dataReaders, outputPipe.Writer, writeGate, cts);
return outputPipe.Reader.AsStream();
```

Inside `CompleteAsync`, cleanup is done in `finally`:

```csharp
foreach (var group in groups)
    group.Controller.Dispose();

foreach (var (_, reader) in readers)
    await reader.CompleteAsync().ConfigureAwait(false);

await output.CompleteAsync(error).ConfigureAwait(false);
writeGate.Dispose();
cts.Dispose();
```

If `Dispose()`, `reader.CompleteAsync()`, or `output.CompleteAsync()` throws, later cleanup may not happen. Since the task is fire-and-forget, such exceptions may be unobserved. More importantly, if cleanup fails before `output.CompleteAsync(error)`, the HTTP stream can hang or terminate unclearly.

The branch does use `NexusUtilities.WhenAllFailFastAsync` here, which is good. The issue is the unobserved background task and unguarded cleanup.

Recommended changes:

- Wrap each cleanup step independently.
- Ensure `output.CompleteAsync(error)` always gets a chance to run.
- Log exceptions from the detached background task.
- Avoid raw `_ = CompleteAsync(...)`; use a helper that observes/logs failures.

## 5. High: The concurrent read loop catches exceptions and keeps going

Relevant code:

- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:1010-1054`

The chunk loop creates one task per reading group. Each task catches broad exceptions:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Process period {Begin} to {End} failed", currentBegin, currentEnd);
}
```

Then the outer code does:

```csharp
await Task.WhenAll(readingTasks).ConfigureAwait(false);
```

Because the exceptions are swallowed inside the tasks, `Task.WhenAll` often has nothing to propagate. The chunk loop then advances:

```csharp
consumedPeriod += currentPeriod;
remainingPeriod -= currentPeriod;
```

This is plainly wrong for a reliable streaming API unless missing data becomes `NaN` and the stream still succeeds is an explicit, documented contract.

Recommended changes:

- Remove the broad catch or rethrow after logging.
- Cancel `chunkCancellation` on first group failure.
- Add a test proving one failing data source faults the whole v2 stream or emits an explicit error frame.

## 6. Medium: Holding the global write gate while flushing hurts parallelism

Relevant code:

- `src/Nexus/Services/DataService.cs:291-314`

Every pumper writes to the shared output pipe under a semaphore:

```csharp
await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

try
{
    ...
    var flushResult = await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    ...
}
finally
{
    writeGate.Release();
}
```

Serializing access to `PipeWriter` is reasonable. Holding the lock during `FlushAsync()` is a performance/concurrency concern. If the HTTP client is slow, `FlushAsync()` can block due to backpressure. While blocked, every other resource pumper is also blocked from writing.

This creates head-of-line blocking and weakens the parallel part of the implementation.

Recommended changes:

- Consider a single dedicated output writer task that owns the `PipeWriter`.
- Have resource pumpers send completed frames to a channel.
- Let one writer serialize frames and handle backpressure.
- Add slow-consumer tests with multiple fast producers.

## 7. Medium: `ReadRequest.CompleteAsync()` is not a clean public extensibility contract

Relevant code:

- `src/extensibility/dotnet-extensibility/Extensibility/DataSource/DataSourceTypes.cs:40-98`

`ReadRequest` is a public record, but completion state is stored externally:

```csharp
private static readonly ConditionalWeakTable<ReadRequest, CompletionState> CompletionStates = new();
```

Problems:

- Completion behavior is invisible in the record constructor.
- `with` copies create new `ReadRequest` instances without configured completion callbacks.
- `IsCompleted` documentation says whether `CompleteAsync` has been called, but implementation only returns true if the task ran to completion:

```csharp
public bool IsCompleted => CompletionStates.TryGetValue(this, out var state) &&
    state.Task?.Status == TaskStatus.RanToCompletion;
```

That is not the same thing. If completion is in progress or faulted, `IsCompleted` is false. That makes the public API misleading.

Recommended changes:

- Replace hidden `ConditionalWeakTable` state with an explicit internal wrapper or interface.
- Rename/clarify `IsCompleted`.
- Add separate state if needed: `CompletionStarted`, `CompletionSucceeded`, `CompletionFaulted`.
- Add tests for `with` clones, double completion, completion failure, and completion before configuration.

## 8. Medium: Failure behavior is inconsistent across original, aggregated, and resampled reads

Relevant code:

- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:512-515`
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:660-665`
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:764-769`

Original reads log and continue.

Aggregated reads log and fill target with `NaN`:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Read aggregation data period {Begin} to {End} failed", begin, end);
    targetBuffer.Span.Fill(double.NaN);
}
```

Resampled reads do the same:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Read resampling data period {Begin} to {End} failed", roundedBegin, roundedEnd);
    targetBuffer.Span.Fill(double.NaN);
}
```

This might be old behavior, but v2 streaming needs a clear failure contract. Silent `NaN` substitution is not acceptable unless it is explicitly part of the API and clients expose it as partial/faulted data.

Recommended changes:

- Define v2 failure behavior separately from legacy v1 behavior.
- If v2 allows partial data, encode status/error metadata in the stream.
- If v2 does not allow partial data, fail the stream.

## 9. Medium: Controller maps status codes by parsing exception messages

Relevant code:

- `src/Nexus/API/v2/DataController.cs:49-56`

```csharp
catch (Exception ex) when (ex.Message.StartsWith("Could not find resource path"))
{
    return NotFound(ex.Message);
}
catch (Exception ex) when (ex.Message.StartsWith("The current user is not permitted to access the catalog"))
{
    return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
}
```

This is brittle. A wording change changes HTTP behavior.

The same pattern exists in v1, but adding it to a new v2 API is not a good direction.

Recommended changes:

- Use typed exceptions such as `ResourceNotFoundException` and `CatalogForbiddenException`.
- Or have the service return a typed result/error.
- Do not use English exception messages as API routing logic.

## 10. Medium: Tests are not sufficient for a streaming/concurrency change of this size

Relevant files:

- `tests/Nexus.Tests/API/v2/DataControllerTests.cs`
- `tests/Nexus.Tests/Services/DataServiceTests.cs`
- `tests/Nexus.Tests/DataSource/DataSourceControllerTests.cs`
- `tests/clients/python-tests/sync-client-tests.py`
- `tests/clients/python-tests/async-client-tests.py`

There are useful tests, especially around `ReadRequest.CompleteAsync()` and generated clients. But the risky behavior is not covered deeply enough.

Missing tests:

- full `/api/v2/data` integration test using the real HTTP pipeline;
- multiple resource streams interleaving correctly;
- multiple containers/controllers reading concurrently;
- source throws before any output;
- source throws after partial output;
- one resource succeeds while another fails;
- client disconnect/cancellation;
- slow consumer/backpressure;
- malformed frames;
- frame header split across network chunks;
- payload split across network chunks;
- duplicate paths;
- mixed sample periods;
- authorization/not-found behavior in v2.

Also, one test pattern is suspicious:

- `tests/Nexus.Tests/DataSource/DataSourceControllerTests.cs:205`

The grep result shows:

```csharp
await Task.WhenAll(writing1, writing2, writing3);
```

while a read task is created earlier in that test. If the read task is not awaited, late failures can be missed.

Recommended changes:

- Add end-to-end v2 streaming integration tests.
- Add controlled fake data sources that delay, fail, partially complete, and ignore completion.
- Ensure tests await both reader and writer sides.
- Add protocol parser fuzz/chunk-boundary tests for clients.

## 11. Low: `git diff --check` fails

Verified output:

```text
src/Nexus.UI/Components/DataView.razor:181: trailing whitespace.
src/clients/dotnet/NexusClient.g.cs:3384: trailing whitespace.
src/clients/python/nexus_api/V2.py:21: trailing whitespace.
src/clients/python/nexus_api/V2.py:28: trailing whitespace.
src/clients/python/nexus_api/V2.py:47: trailing whitespace.
src/clients/python/nexus_api/V2.py:66: trailing whitespace.
src/clients/python/nexus_api/V2.py:73: trailing whitespace.
src/clients/python/nexus_api/V2.py:92: trailing whitespace.
src/clients/python/nexus_api/V2.py:128: new blank line at EOF.
```

This is not a deep bug, but it is a quality signal. It is especially concerning in generated files because it suggests generator output may not be clean/reproducible.

## 12. Low: Unused/unclean code

Examples:

- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:422`: `targetByteCount` is passed into `ReadOriginalAsync` but not used.
- `src/Nexus/Extensibility/DataSource/DataSourceController.cs:532`: deconstructs `readUnit` but does not use it.
- `src/clients/python/nexus_api/_client.py:12-13`: `Callable` is imported twice.
- `src/clients/python/nexus_api/_client.py:16`: `UUID` import should be checked; it was added but is not visible as used in the shown section.

These are easy fixes, but they reinforce that the branch needs cleanup.

## 13. Low/Process: The branch mixes too many concerns

The branch changes:

- v2 API surface;
- server data-source concurrency;
- public extensibility contracts;
- generated C# client;
- generated Python client;
- OpenAPI generation;
- Blazor data loading;
- WebGPU charting;
- CSS;
- docs;
- CI.

The WebGPU/chart files alone are large enough for a separate review. Mixing them with backend streaming makes it much harder to reason about correctness and regressions.

Recommended changes:

- Split backend streaming/API/client generation from UI/WebGPU work if possible.
- If not split, require separate focused reviews.

## Verification Results

`git diff --check dev...HEAD` failed due trailing whitespace and EOF whitespace issues listed above.

`dotnet test tests/Nexus.Tests/Nexus.Tests.csproj --no-restore` built and ran, but failed 4 tests because the external `frictionless` executable is missing:

```text
Failed: 4, Passed: 135, Total: 139
```

The failing tests are `DataWriter.CsvDataWriterTests.CanWriteFiles(...)`, all failing with:

```text
An error occurred trying to start process 'frictionless' ... No such file or directory
```

That does not appear directly caused by this branch, but it means the local test run is not fully green in this environment.

## Assessment Of The Parallel Loading Approach

The approach is directionally useful but overstated.

Good parts:

- Per-resource pipes are a reasonable concurrency primitive.
- Framed binary output allows interleaving resource data.
- `ReadRequest.CompleteAsync()` allows cooperative early flush.
- The v2 endpoint can reduce sequential per-resource HTTP request overhead.
- The Python client validates frame indexes, frame sizes, overflow, and early EOF.
- `DataService.CompleteAsync` uses `NexusUtilities.WhenAllFailFastAsync`, which is good for its coordination layer.

Weak parts:

- Source implementations must opt in to early completion.
- The framework still processes time chunks sequentially.
- The server still uses full chunk buffers.
- High-level clients still fully buffer all resources.
- Output writes are globally serialized and hold the lock while flushing.
- Source failures can be swallowed.
- The stream protocol does not have explicit success/error semantics.
- Tests do not cover the most important failure modes.

So the honest description is:

```text
This branch implements framed multi-resource batch transfer with some concurrent pumping and optional cooperative early flush.
```

It is not yet:

```text
A robust end-to-end parallel streaming data-loading architecture.
```

## Required Changes Before Trusting It

1. Stop swallowing source exceptions in v2 streaming paths.
2. Decide whether v2 allows partial/`NaN` data and encode that explicitly.
3. Add stream-level completion/error semantics.
4. Make background stream cleanup robust and logged.
5. Add real v2 endpoint integration tests.
6. Add failure-after-partial-output tests.
7. Add cancellation/disconnect/backpressure tests.
8. Add lower-level streaming client APIs if true streaming is a goal.
9. Clean generated artifacts and whitespace.
10. Replace message-prefix exception handling with typed errors.
11. Split or separately review the WebGPU/charting changes.

## Final Recommendation

Do not merge as-is.

There is a promising foundation here, but it is not high-quality enough to trust yet. The main issue is correctness under failure, not raw performance. A streaming API that can silently convert source failures into successful-looking output is not safe. Fix the failure semantics, protocol contract, and integration tests first; then reassess performance and client streaming behavior.
