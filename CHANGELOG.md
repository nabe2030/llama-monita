# Changelog

All notable changes to LlamaMonita are recorded here. Versions follow [Semantic Versioning](https://semver.org/):
the version lives in `src/LlamaMonita/LlamaMonita.csproj` (`<Version>`), is shown next to the name in the window,
and each release is tagged `vX.Y.Z` on GitHub with these notes.

## 0.2.1 — 2026-11-15

### Added
- App icon: a llama with a pulse line, in the colours of the graph (exe, window, taskbar and Start menu).

## 0.2.0 — 2026-11-15

### Added
- **Request log.** A list of the requests seen since the window connected: when it started, where it came from,
  which model handled it, slot, prompt and generated tokens, duration and speed.
  - "From" is the client's address for LAN clients, or `local (process name)` for programs on the same PC. It is read
    from the Windows TCP connection table, so it is only available when llama-server runs on the same machine.
  - The `Log` button decides whether the request log or the details row wins when the window is too small for both.
- **Durable request history.** Every finished request is appended to
  `%LOCALAPPDATA%\LlamaMonita\requests\requests-YYYY-MM.csv`. Today's totals are shown above the list, and the tooltip
  lists the last 14 days. Right-click → *Open request log folder*.
- **Prometheus endpoint (opt-in).** `--metrics-listen 9777` serves `http://127.0.0.1:9777/metrics`;
  `--metrics-listen 0.0.0.0:9777` makes it reachable from the LAN. It exposes request, prompt-token and
  generated-token counters by model and client, generation speed, GPU utilization, dedicated / shared GPU memory,
  GPU temperature, CPU and RAM. No administrator rights are needed.
- **Version in the window.** The version is shown in the title bar and at the right end of the top row, where it
  links to the release notes of that version.
- `--log` (start with the request log preferred) and `--log-dir PATH` (write the CSV somewhere else).

### Fixed
- Generation speed could spike to thousands of tok/s, and the generated-token total could be far too high, after a
  request with a long prompt finished between two polls. Speed is now computed only from tokens seen arriving, and the
  end-of-request correction is applied only when generation was actually observed.
- The engine label no longer truncates on "High priority 3D / Compute"; these are shown as "3D" / "Compute".

### Changed
- The details row is down to two cards. The model card now lists the router's models with the quantization and
  context size under the loaded one (it replaces the separate "Model" and "Models" cards). The "Slots" card and the
  line about local llama-server processes are gone: the request log shows the slot of each request, and the row is
  shorter, so the request log fits in smaller windows.
- The top bar buttons are shorter (`Log`, `On top`).
- Standard window size is 1080 × 820.

## 0.1.0 — 2026-11-14

First public release, made to celebrate llama.cpp 1.0.0.

- GPU utilization and generation speed on one dual-axis history graph.
- Dedicated and shared GPU memory, GPU temperature, and fan / power where the driver reports them.
- Model, quantization, context size, per-slot context usage, router-mode model list, CPU and RAM.
- Layout adapts to the window size, down to a compact always-on-top strip.
- Read-only: `GET` requests only, no administrator rights, never queries a model that is not loaded.
