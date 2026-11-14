# UI history

Snapshots of the window as the design evolved, newest last. Captured with
`LlamaMonita.exe --screenshot <file> --size <W>x<H>` while a generation was running.

| # | Change | Snapshot |
|---|---|---|
| 01 | Initial version: cards for model, speed, GPU, slots, system | [01-initial.png](01-initial.png) |
| 02 | History graphs become the main element; details compressed into a bottom row; layout adapts to window size (standard / tall / compact) | [02-hero-graphs.png](02-hero-graphs.png), [tall](02-hero-graphs-tall.png), [compact](02-hero-graphs-compact.png) |
| 03 | GPU utilization and generation speed merged into one nvtop-style graph: GPU % on the left axis, tok/s on the right; one line per GPU | [03-combined-graph.png](03-combined-graph.png), [wide](03-combined-graph-wide.png), [compact](03-combined-graph-compact.png) |
| 04 | Headline values keep their position (fixed-width tiles, tabular digits); details row stays visible at smaller sizes; server box labelled; power hidden on integrated GPUs; token counts corrected | [04-fixed-layout.png](04-fixed-layout.png), [medium](04-fixed-layout-medium.png), [compact](04-fixed-layout-compact.png) |
