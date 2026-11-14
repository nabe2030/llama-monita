# LlamaMonita

An nvtop-style desktop monitor for [llama.cpp](https://github.com/ggml-org/llama.cpp) on Windows 11, for both integrated and discrete GPUs.

"Monita" is モニタ, Japanese for *monitor*. Released to celebrate llama.cpp 1.0.

![LlamaMonita watching a llama-server on a Radeon 780M: GPU utilization in blue, generation speed in green](docs/history/04-fixed-layout.png)

The blue area is GPU utilization (left axis) and the green line is generation speed (right axis). The window can shrink down to a small always-on-top strip:

![Compact view](docs/history/04-fixed-layout-compact.png)

How the screen got here is recorded in [docs/history](docs/history/README.md).

LlamaMonita shows, in one window:

- **llama-server**: loaded model, quantization, context size, per-slot activity and context usage, live generation speed (tok/s), tokens processed this session, and the status of every model in router mode
- **GPU**: utilization, dedicated and shared GPU memory, temperature, fan speed and power where the driver reports them
- **System**: CPU, RAM, and the GPU memory held by local `llama-server` processes

## Why another monitor

- **Shared GPU memory is a first-class value.** On integrated GPUs (Radeon 780M / 890M / 8060S, Intel Arc, …) models live in *shared* memory, which most GPU monitors do not show.
- **Vendor-neutral.** It uses the same Windows sources as Task Manager (DXGI, the WDDM `GPU *` performance counters and D3DKMT adapter data), so AMD, NVIDIA and Intel adapters work without vendor SDKs.
- **No administrator rights, no helper process, no kernel driver.**
- **Read-only.** It only sends `GET` requests. In router mode it never queries a model that is not already loaded, so watching cannot trigger a model load or swap.
- **Works without `--metrics`.** Live speed and session token counts come from `/slots`. If the server was started with `--metrics`, server-wide totals are shown as well.

## Download

Get `LlamaMonita-<version>-win-x64.zip` from the [Releases page](https://github.com/nabe2030/llama-monita/releases/latest), unzip it anywhere and run `LlamaMonita.exe`. There is no installer and nothing is written outside the folder except the remembered server address (`%LOCALAPPDATA%\LlamaMonita\settings.json`).

The exe is not code-signed, so Windows SmartScreen may ask for confirmation the first time ("More info" → "Run anyway").

## Requirements

- Windows 11 (Windows 10 2004 or later should work, untested)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running `llama-server` (local or on the LAN)

## Usage

```
LlamaMonita.exe [--url http://127.0.0.1:8080] [--api-key KEY]
```

The API key can also be supplied through the `LLAMA_API_KEY` environment variable. The last URL you connect to is remembered.

`LlamaMonita.exe --probe out.json` writes a few samples of every data source to a file without opening a window. Attach it to bug reports.

## Build

```
dotnet build src/LlamaMonita -c Release
```

## Notes and limits

- GPU values describe the machine LlamaMonita runs on. When you point it at a remote server you get that server's model and token data, with the local machine's GPU.
- Temperature, fan and power come from the display driver. Integrated GPUs commonly report no fan, and some drivers report nothing at all; missing values are simply not shown.
- GPU utilization is the busiest engine of the adapter (for example *Compute* under Vulkan or CUDA), the same way Task Manager picks its headline number.
- Power is shown for discrete adapters only. The figure integrated GPUs report does not follow load (a Radeon 780M reports 5% of maximum at full load).
- Token counts come from `/slots`: `n_prompt_tokens` there is the whole slot context (prompt plus generated tokens), so the prompt size is taken from `n_prompt_tokens_processed + n_prompt_tokens_cache`.
- In router mode llama-server does not serve `/metrics` (HTTP 501, checked on b12110 and b12359), so server-wide totals are only shown for a single-model server started with `--metrics`.
- Tested on: Ryzen 7 8845HS / Radeon 780M, Vulkan builds of llama.cpp b12110 and b12359 (v1.0.0). Reports from other hardware are welcome.

## License

[MIT](LICENSE)
