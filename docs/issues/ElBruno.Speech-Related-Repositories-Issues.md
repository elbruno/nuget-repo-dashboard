# ElBruno.Speech — Issues for Related Repositories

This file contains ready-to-copy GitHub issue definitions for the repositories needed by `ElBruno.Speech`.

**New repository:** https://github.com/elbruno/ElBruno.Speech  
**Source inspiration:** https://github.com/huggingface/speech-to-speech  
**Date:** July 3, 2026

---

## Dependency summary

| Repository | File MVP | Realtime | Status |
|---|---:|---:|---|
| ElBruno.Whisper | Required | Required | Runtime exists; MEAI, stream input, and incremental STT needed |
| ElBruno.LocalLLMs | Required | Required | Already IChatClient; cancellation hardening needed |
| ElBruno.VibeVoiceTTS | Recommended | Required | In-memory output exists; MEAI and streaming needed |
| ElBruno.QwenTTS | Optional first MVP | Required for advanced provider | MEAI, in-memory core, and streaming needed |
| ElBruno.HuggingFace.Downloader | Required | Required | Revision, integrity, and resume enhancements needed |

---

# ElBruno.Whisper

Repository:

https://github.com/elbruno/ElBruno.Whisper

The repository already includes local ONNX inference, model download, multiple models, multilingual support, DI, timestamps, tests, and OIDC publishing. The issues below extend the existing library and preserve its current API.

---

## W-001 — Implement Microsoft.Extensions.AI ISpeechToTextClient

**Title**

```text
feat: implement Microsoft.Extensions.AI ISpeechToTextClient
```

**Priority:** P0  
**Labels:** `enhancement`, `microsoft-extensions-ai`, `speech-to-text`, `ElBruno.Speech`

### Context

`ElBruno.Speech` composes providers through Microsoft.Extensions.AI. `ElBruno.Whisper` currently exposes `WhisperClient`, but needs a standard provider contract.

Reference:

https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ispeechtotextclient

### Scope

- Add `ISpeechToTextClient`.
- Preserve existing `WhisperClient` APIs.
- Reference only `Microsoft.Extensions.AI.Abstractions`.
- Implement `GetTextAsync`.
- Implement `GetService`.
- Expose `SpeechToTextClientMetadata`.
- Map `SpeechToTextOptions`.
- Return text, language, duration, timestamps, model, and execution-provider metadata.
- Do not dispose caller-owned streams.
- Support cancellation.
- Add DI registration as `ISpeechToTextClient`.

### Preferred design

Use an adapter first to isolate experimental MEAI changes:

```csharp
public sealed class WhisperSpeechToTextClient :
    ISpeechToTextClient
{
    public WhisperSpeechToTextClient(WhisperClient inner);
}
```

### Options mapping

| MEAI option | Whisper behavior |
|---|---|
| ModelId | Select or validate configured model |
| Language | Language/task token |
| Prompt | Initial context when supported |
| AdditionalProperties | Whisper decoding options |
| Media type | Input decoder selection |

### Metadata

```text
elbruno.whisper.detected_language
elbruno.whisper.audio_duration_ms
elbruno.whisper.segments
elbruno.whisper.words
elbruno.whisper.model_id
elbruno.whisper.execution_provider
```

### Acceptance criteria

- Provider conformance tests pass.
- Input stream remains open.
- Cancellation is observed.
- DI resolves `ISpeechToTextClient`.
- Existing APIs remain compatible.
- Unit tests avoid model downloads.
- Integration test transcribes a small WAV.
- README includes MEAI usage.

### Out of scope

- Incremental transcription, tracked separately
- VAD
- Microphone capture

---

## W-002 — Add raw PCM and arbitrary Stream input

**Title**

```text
feat: support raw PCM and arbitrary Stream input with explicit audio format
```

**Priority:** P0  
**Labels:** `enhancement`, `audio`, `streaming`, `ElBruno.Speech`

### Context

Realtime pipelines receive PCM chunks and streams, not file paths. Avoid temporary WAV files.

### Proposed API

```csharp
public sealed record WhisperAudioFormat(
    int SampleRate,
    int Channels,
    WhisperAudioSampleFormat SampleFormat);

public Task<TranscriptionResult> TranscribeAsync(
    Stream audio,
    WhisperAudioFormat format,
    TranscriptionOptions? options = null,
    CancellationToken cancellationToken = default);

public Task<TranscriptionResult> TranscribeAsync(
    ReadOnlyMemory<float> monoAudio,
    int sampleRate,
    TranscriptionOptions? options = null,
    CancellationToken cancellationToken = default);
```

### Requirements

- Accept `Stream`, `ReadOnlyMemory<byte>`, and `ReadOnlyMemory<float>`.
- Normalize sample rate and channels.
- Detect WAV input by media type or header.
- Reject unsupported formats clearly.
- Keep stream ownership with caller.
- Use pooled buffers.
- No temporary file.

### Acceptance criteria

- WAV `MemoryStream` works.
- PCM16 16 kHz mono works.
- Float32 16 kHz mono works.
- 48 kHz stereo normalizes correctly.
- No temporary file.
- Caller stream remains open.
- Cancellation works in preprocessing and inference.
- Malformed input gives a typed error.
- Add allocation and latency benchmarks.

---

## W-003 — Add incremental transcription

**Title**

```text
feat: add incremental transcription through GetStreamingTextAsync
```

**Priority:** P0 realtime, P1 file MVP  
**Labels:** `enhancement`, `streaming`, `speech-to-text`, `ElBruno.Speech`

### Context

Whisper is not a transducer-style streaming model, but rolling provisional transcription is valuable.

### Scope

- Implement `GetStreamingTextAsync`.
- Support rolling windows.
- Emit provisional and committed text.
- Emit exactly one final update.
- Avoid duplicate committed text.
- Support cancellation.
- Document limitations.

### Proposed options

```csharp
public sealed class WhisperStreamingOptions
{
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan StepSize { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ContextOverlap { get; set; } = TimeSpan.FromSeconds(2);
    public bool UseLocalAgreement { get; set; } = true;
    public int AgreementIterations { get; set; } = 2;
}
```

### Algorithm

1. Buffer incoming audio.
2. Infer on a rolling window.
3. Compare recent hypotheses.
4. Commit stable text.
5. Keep overlap context.
6. Flush at end-of-stream.
7. Emit final update.

### Acceptance criteria

- Updates are ordered.
- Exactly one final successful update.
- Nothing emitted after cancellation.
- No duplicate committed text.
- Empty audio does not fabricate text.
- Fake backend tests deterministic hypotheses.
- Real integration test demonstrates provisional and final output.
- Docs clearly label provisional text.

---

## W-004 — Define concurrency, pooling, and cancellation behavior

**Title**

```text
perf: define thread-safety and add model-session pooling for concurrent transcription
```

**Priority:** P1  
**Labels:** `performance`, `concurrency`, `reliability`, `ElBruno.Speech`

### Scope

- Document concurrent-call support.
- Separate shared model resources from request state.
- Add configurable concurrency limit.
- Add optional session pooling.
- Add cancellation checkpoints.
- Emit queue-wait and inference metrics.

### Proposed options

```csharp
public sealed class WhisperConcurrencyOptions
{
    public int MaximumConcurrentRequests { get; set; } = 1;
    public TimeSpan QueueTimeout { get; set; } =
        TimeSpan.FromSeconds(30);
}
```

### Acceptance criteria

- Concurrent behavior documented.
- Parallel fake requests have no state leakage.
- Real-model tests run at concurrency 1 and 2.
- Waiting is cancellable.
- Cancellation during decoding is observed at safe points.
- Metrics expose wait and inference duration.

---

## W-005 — Map existing timestamps to MEAI responses

**Title**

```text
feat: expose segment and word timestamps through SpeechToText responses
```

**Priority:** P1  
**Labels:** `enhancement`, `timestamps`, `microsoft-extensions-ai`, `ElBruno.Speech`

### Scope

- Map current segment timestamps.
- Map word timestamps when enabled.
- Preserve existing result fields.
- Use stable metadata keys.
- Add JSON round-trip tests.

### Acceptance criteria

- Existing timestamp tests pass.
- MEAI response includes segments.
- Streaming final output matches non-streaming final segments.
- Metadata serializes without non-portable provider CLR types.

---

# ElBruno.VibeVoiceTTS

Repository:

https://github.com/elbruno/ElBruno.VibeVoiceTTS

Recommended as the first TTS provider because it already returns in-memory `float[]`.

---

## V-001 — Implement Microsoft.Extensions.AI ITextToSpeechClient

**Title**

```text
feat: implement Microsoft.Extensions.AI ITextToSpeechClient
```

**Priority:** P0  
**Labels:** `enhancement`, `microsoft-extensions-ai`, `text-to-speech`, `ElBruno.Speech`

### Scope

- Add an `ITextToSpeechClient` adapter.
- Preserve `VibeVoiceSynthesizer`.
- Implement `GetAudioAsync`.
- Implement `GetService`.
- Expose metadata.
- Map voice, sample rate, and provider options.
- Return `DataContent` with correct MIME type.
- Support cancellation.
- Register through DI.

### Proposed API

```csharp
public sealed class VibeVoiceTextToSpeechClient :
    ITextToSpeechClient
{
    public VibeVoiceTextToSpeechClient(
        VibeVoiceSynthesizer synthesizer,
        IOptions<VibeVoiceTextToSpeechOptions> options);
}
```

### Minimum output types

```text
audio/wav
audio/pcm;rate=24000;channels=1;format=f32le
audio/pcm;rate=24000;channels=1;format=s16le
```

### Acceptance criteria

- No file write required.
- Voice selection works.
- MIME type and metadata are correct.
- Cancellation works.
- DI resolves `ITextToSpeechClient`.
- Existing API remains compatible.
- Provider conformance tests pass.

---

## V-002 — Add streaming audio output

**Title**

```text
feat: stream generated PCM chunks through GetStreamingAudioAsync
```

**Priority:** P0 realtime  
**Labels:** `enhancement`, `streaming`, `audio`, `ElBruno.Speech`

### Proposed API

```csharp
public IAsyncEnumerable<VibeVoiceAudioChunk> GenerateAudioStreamingAsync(
    string text,
    VibeVoicePreset voice,
    VibeVoiceGenerationOptions? options = null,
    CancellationToken cancellationToken = default);
```

```csharp
public sealed record VibeVoiceAudioChunk(
    ReadOnlyMemory<float> Samples,
    int SampleRate,
    long SequenceNumber,
    bool IsFinal);
```

### Requirement

If the model cannot expose audio before inference completes, implement chunked delivery but report:

```text
SupportsProgressiveGeneration = false
SupportsChunkedDelivery = true
```

### Acceptance criteria

- Chunks are ordered.
- Concatenated chunks match complete output within tolerance.
- Exactly one final update.
- No audio after cancellation.
- No unnecessary second complete audio copy.
- Capability metadata is accurate.

---

## V-003 — Add cancellation and concurrency policy

**Title**

```text
perf: add generation cancellation checkpoints and document concurrent-use behavior
```

**Priority:** P1  
**Labels:** `performance`, `concurrency`, `reliability`, `ElBruno.Speech`

### Scope

- Pass cancellation through preprocessing, encoding, diffusion, and decoding.
- Document thread safety.
- Add pooling or semaphore-based limiting.
- Add first-audio and total-duration metrics.
- Prevent voice state leakage.

### Acceptance criteria

- Cancellation returns at the next safe point.
- Concurrent calls are safe or explicitly serialized.
- Waiting for capacity is cancellable.
- Stress tests switch voices repeatedly without corruption.
- Concurrency behavior is documented.

---

# ElBruno.QwenTTS

Repository:

https://github.com/elbruno/ElBruno.QwenTTS

---

## Q-001 — Implement Microsoft.Extensions.AI ITextToSpeechClient

**Title**

```text
feat: implement Microsoft.Extensions.AI ITextToSpeechClient
```

**Priority:** P0  
**Labels:** `enhancement`, `microsoft-extensions-ai`, `text-to-speech`, `ElBruno.Speech`

### Scope

- Add `ITextToSpeechClient`.
- Keep `TtsPipeline`.
- Use an adapter.
- Implement `GetAudioAsync`.
- Expose voice, language, instruct, model variant, and execution provider.
- Return in-memory audio.
- Use correct MIME type.
- Support cancellation.

### Suggested metadata

```text
elbruno.qwentts.variant
elbruno.qwentts.speaker
elbruno.qwentts.language
elbruno.qwentts.instruct
elbruno.qwentts.voice_cloning
elbruno.qwentts.execution_provider
```

### Acceptance criteria

- No temporary file.
- Preset speaker works through MEAI.
- Instruct works through additional properties.
- Unsupported voice-clone input returns a clear capability error.
- DI resolves `ITextToSpeechClient`.
- Existing APIs remain compatible.
- Conformance tests pass.

---

## Q-002 — Add in-memory synthesis as the core API

**Title**

```text
feat: return in-memory PCM/WAV audio without requiring an output file
```

**Priority:** P0  
**Labels:** `enhancement`, `audio`, `api`, `ElBruno.Speech`

### Proposed API

```csharp
public sealed record TtsAudioResult(
    ReadOnlyMemory<float> Samples,
    int SampleRate,
    int Channels,
    TimeSpan Duration);

public Task<TtsAudioResult> SynthesizeAsync(
    string text,
    string speaker,
    string language,
    string? instruct = null,
    CancellationToken cancellationToken = default);
```

Optional:

```csharp
public Task<ReadOnlyMemory<byte>> SynthesizeWavAsync(...);
```

### Requirements

- Existing file API delegates to in-memory core.
- No duplicate inference.
- Avoid float → WAV → float.
- Document sample range and normalization.

### Acceptance criteria

- File and memory output share one inference path.
- WAV saved from result is valid.
- Duration matches sample count.
- Cancellation works.
- Unit tests avoid mandatory model downloads.

---

## Q-003 — Add streaming TTS

**Title**

```text
feat: implement streaming TTS output and GetStreamingAudioAsync
```

**Priority:** P0 realtime  
**Labels:** `enhancement`, `streaming`, `audio`, `ElBruno.Speech`

### Scope

- Stream vocoder output where technically possible.
- Otherwise provide chunked delivery with accurate capability metadata.
- Preserve order.
- Include format metadata.
- Support cancellation.
- Avoid a second complete audio copy.

### Proposed API

```csharp
public IAsyncEnumerable<QwenTtsAudioChunk> SynthesizeStreamingAsync(
    QwenTtsRequest request,
    CancellationToken cancellationToken = default);
```

### Acceptance criteria

- MEAI streaming returns audio updates.
- First update includes format metadata.
- Concatenated chunks form valid audio.
- Cancellation stops future chunks.
- Exactly one final update.
- Metadata distinguishes progressive inference from post-generation chunking.

---

## Q-004 — Add pooling and fast cancellation

**Title**

```text
perf: add reusable inference sessions, concurrency limits, and fast cancellation
```

**Priority:** P1  
**Labels:** `performance`, `concurrency`, `reliability`, `ElBruno.Speech`

### Scope

- Reuse immutable model sessions.
- Keep request state isolated.
- Add maximum concurrency.
- Add cancellable waiting.
- Check cancellation in talker, code predictor, and vocoder loops.
- Record first-audio and total latency.

### Acceptance criteria

- Repeated calls do not reload models.
- Concurrent behavior documented.
- No speaker/request state leakage.
- Cancellation observed at safe loop boundaries.
- GPU sessions disposed correctly.
- Memory stable across 100 sequential requests.

---

# ElBruno.HuggingFace.Downloader

Repository:

https://github.com/elbruno/ElBruno.HuggingFace.Downloader

---

## HF-001 — Add revision and commit pinning

**Title**

```text
feat: support Hugging Face revision and commit pinning
```

**Priority:** P0  
**Labels:** `enhancement`, `reproducibility`, `huggingface`, `ElBruno.Speech`

### Context

Production model bundles must not silently change when `main` changes.

### Proposed API

```csharp
public sealed class DownloadRequest
{
    public required string RepoId { get; init; }
    public string Revision { get; init; } = "main";
    public string? ExpectedCommitSha { get; init; }
}
```

### Requirements

- Revision may be branch, tag, or commit.
- Resolve immutable SHA when possible.
- Cache includes repo and resolved revision.
- Store resolution metadata.
- Existing callers default to `main`.
- CLI supports `--revision`.

### Acceptance criteria

- Download by tag.
- Download by commit.
- Revisions do not overwrite each other.
- Result reports resolved commit.
- Existing API compatible.
- Unit tests use injectable HTTP.

---

## HF-002 — Add manifests and SHA-256 validation

**Title**

```text
feat: add manifest-based model bundles with SHA-256 integrity validation
```

**Priority:** P0  
**Labels:** `enhancement`, `security`, `integrity`, `ElBruno.Speech`

### Proposed API

```csharp
public sealed record ModelBundleManifest(
    string RepoId,
    string Revision,
    IReadOnlyList<ModelBundleFile> Files);

public sealed record ModelBundleFile(
    string Path,
    long? Size,
    string? Sha256,
    bool Required);
```

```csharp
public Task<ModelBundleResult> EnsureBundleAsync(
    ModelBundleManifest manifest,
    string localDirectory,
    CancellationToken cancellationToken = default);
```

### Requirements

- Validate hash and size.
- Download to temp.
- Atomic move after validation.
- Write local resolved manifest.
- Detect mixed revisions.
- Redownload corruption.
- Progress includes validation.
- CLI accepts manifest JSON.

### Acceptance criteria

- Corrupt file detected.
- Missing optional file allowed.
- Missing required file fails.
- Mixed revision rejected.
- Manifest JSON round-trips.
- Tokens and auth headers never logged.

---

## HF-003 — Add resumable downloads

**Title**

```text
feat: resume interrupted large model downloads with HTTP range requests
```

**Priority:** P1  
**Labels:** `enhancement`, `performance`, `reliability`, `ElBruno.Speech`

### Scope

- Retain safe partial files.
- Resume when range is supported.
- Restart when ETag or revision changes.
- Validate final size and checksum.
- Report resumed bytes.
- Add `--no-resume`.

### Acceptance criteria

- Interrupted download resumes.
- Changed ETag invalidates partial.
- No-range server safely restarts.
- Final file is atomic.
- Cancellation preserves partial only when safe.

---

# ElBruno.LocalLLMs

Repository:

https://github.com/elbruno/ElBruno.LocalLLMs

No new model interface is required. It already implements `IChatClient` and streaming.

---

## LLM-001 — Guarantee fast cancellation for barge-in

**Title**

```text
test: guarantee fast cancellation of streaming generation for voice barge-in
```

**Priority:** P1  
**Labels:** `reliability`, `cancellation`, `streaming`, `ElBruno.Speech`

### Context

A realtime speech pipeline cancels LLM generation when the user starts speaking.

### Scope

- Add deterministic cancellation tests with injectable generation backend.
- Add real-model cancellation integration test.
- Check cancellation between generated tokens.
- Emit no update after cancellation is observed.
- Confirm the client remains usable.
- Measure cancellation latency.
- Emit cancellation metrics/activity status.

### Acceptance criteria

- Cancelling streaming stops enumeration.
- Documented cancellation behavior occurs.
- Next request succeeds.
- No stale tokens leak into next request.
- Behavior documented.
- Real-model test uses a reasonable upper bound.

---

## LLM-002 — Expose generation lifecycle diagnostics

**Title**

```text
feat: emit structured generation lifecycle diagnostics for realtime orchestration
```

**Priority:** P2  
**Labels:** `observability`, `opentelemetry`, `ElBruno.Speech`

### Scope

Emit diagnostics for:

- Queued
- Model ready
- Generation started
- First token
- Token count
- Completed
- Cancelled
- Failed

Prefer OpenTelemetry and `DiagnosticSource`.

### Acceptance criteria

- Prompt and generated text excluded by default.
- Time-to-first-token measurable.
- Cancellation differs from failure.
- Works through `IChatClient`.
- Aspire dashboard displays activities when configured.

---

# Coordination

## Recommended implementation order

1. W-001 and W-002
2. V-001
3. Q-002 and Q-001
4. HF-001 and HF-002
5. `ElBruno.Speech` Phase 0 and Phase 1
6. Silero VAD
7. File-based MVP
8. W-003
9. V-002 and Q-003
10. LLM-001
11. Realtime barge-in
12. ASP.NET Core/WebSockets

---

## Cross-repository rules

- Provider libraries reference `Microsoft.Extensions.AI.Abstractions`.
- Applications and orchestration may reference `Microsoft.Extensions.AI`.
- Centralize package versions.
- Provider repos must not reference `ElBruno.Speech.Pipeline`.
- A provider may reference `ElBruno.Speech.Abstractions` or `.Audio` only if no cycle is created.
- Preserve existing APIs.
- New async APIs accept `CancellationToken`.
- Caller-owned streams stay open.
- Media types are explicit.
- Thread safety is documented.
- Streaming APIs emit one terminal outcome.
- No content logging by default.
- Model downloads excluded from default unit CI.

---

## Provider conformance checklist

Every STT/TTS provider documents:

```text
Supports complete response:
Supports streaming response:
Streaming is truly progressive:
Supports cancellation:
Supports concurrent calls:
Recommended maximum concurrency:
Input formats:
Output formats:
Sample rate:
Channels:
Returns timestamps:
Returns language:
Uses shared model session:
Requires GPU:
Supports CPU:
Supports DirectML:
Supports CUDA:
Downloads models automatically:
```

---

## Suggested milestones

### Speech Provider Contracts

- W-001
- W-002
- V-001
- Q-001
- Q-002

### Realtime Streaming

- W-003
- V-002
- Q-003
- LLM-001

### Production Reliability

- W-004
- W-005
- V-003
- Q-004
- HF-001
- HF-002
- HF-003
- LLM-002

---

## References

- https://github.com/elbruno/ElBruno.Whisper
- https://github.com/elbruno/ElBruno.LocalLLMs
- https://github.com/elbruno/ElBruno.VibeVoiceTTS
- https://github.com/elbruno/ElBruno.QwenTTS
- https://github.com/elbruno/ElBruno.HuggingFace.Downloader
- https://github.com/huggingface/speech-to-speech
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.ispeechtotextclient
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.itexttospeechclient
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.irealtimeclient
- https://www.nuget.org/packages/Microsoft.Extensions.AI.Abstractions
- https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
