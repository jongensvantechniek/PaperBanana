# PaperBanana — .NET 10 + Blazor

A faithful C# / .NET 10 port of the PaperBanana multi-agent framework, with a
**Blazor Web App (Interactive Server)** front-end that mirrors the original
Streamlit demo. It lives side-by-side with the original Python implementation
(in the repository root) — nothing in the Python code was removed.

## What this is

PaperBanana orchestrates five specialized agents — **Retriever → Planner →
Stylist → Visualizer → Critic** (plus **Vanilla** and **Polish**) — to turn a
paper's method section + figure caption into publication-quality diagrams, with
a Visualizer↔Critic refinement loop. This port reproduces that pipeline in C#
and calls the Gemini / Anthropic / OpenAI REST APIs directly.

## Solution layout

```
dotnet/
├── PaperBanana.sln(x)
├── PaperBanana.Core/          # the framework, no UI
│   ├── Configuration/         # ModelConfig (YAML + env), ExpConfig
│   ├── Models/                # QueryData (dynamic pipeline bag), ContentPart, GenerationOptions
│   ├── Llm/                   # GenerationClient: Gemini/Anthropic/OpenAI over HttpClient
│   ├── Agents/                # BaseAgent + 7 agents + AgentPrompts (verbatim prompts)
│   ├── Evaluation/            # EvalToolkit (referenced VLM-judge, embedded prompts)
│   ├── Pipeline/              # PaperVizProcessor (exp-mode orchestration, batch streaming)
│   ├── Utils/                 # ImageUtils (ImageSharp), JsonRepair, PlotExecutorClient
│   └── PaperVizFactory.cs     # wires a processor for an ExpConfig
├── PaperBanana.Web/           # Blazor Web App (Interactive Server)
│   └── Components/            # Generate Candidates + Refine Image tabs
├── PaperBanana.Cli/           # command-line runner (port of main.py)
└── plot-sidecar/              # Python matplotlib executor for the "plot" task
```

### Python → .NET mapping

| Python | .NET |
| --- | --- |
| `utils/config.py` | `Configuration/ExpConfig.cs`, `ModelConfig.cs` |
| `utils/generation_utils.py` | `Llm/GenerationClient.cs` |
| `utils/image_utils.py` | `Utils/ImageUtils.cs` (ImageSharp instead of Pillow) |
| `utils/eval_toolkits.py` + `prompts/*` | `Evaluation/EvalToolkit.cs` + `Resources/EvalPrompts/*.txt` |
| `utils/paperviz_processor.py` | `Pipeline/PaperVizProcessor.cs` |
| `agents/*.py` | `Agents/*.cs` |
| `demo.py` (Streamlit) | `PaperBanana.Web` (Blazor) |
| `main.py` | `PaperBanana.Cli` |
| matplotlib `exec` worker | `plot-sidecar/plot_service.py` (HTTP) + `PlotExecutorClient` |

## Prerequisites

- .NET 10 SDK (`dotnet --version` → `10.x`)
- Python 3 + matplotlib **only if you use the `plot` task** (for the sidecar)
- At least one API key (Google for Gemini models is the default path)

## Configuration

The .NET port reads the **same** `configs/model_config.yaml` as the Python code
(copy it from `configs/model_config.template.yaml`), with an added optional
`services.plot_service_url`. Environment variables take precedence:

- `GOOGLE_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`
- `MODEL_NAME`, `IMAGE_MODEL_NAME`
- `PLOT_SERVICE_URL`

The work directory (holding `data/`, `style_guides/`, `configs/`) is discovered
by walking up from the current directory to find `style_guides/`. Override it
with `--work_dir` (CLI) or `PaperBanana:WorkDir` (web `appsettings.json`).

## Run the Blazor web app

```bash
cd dotnet
dotnet run --project PaperBanana.Web
# open the printed URL (e.g. http://localhost:5172)
```

Two tabs:

- **Generate Candidates** — paste the method section + caption, pick a pipeline
  mode (`demo_planner_critic` / `demo_full`), retrieval setting, candidate count,
  aspect ratio and critic rounds, then generate. Candidates stream in as they
  finish; each card shows the final image, a download link, and an evolution
  timeline (Planner → Stylist → Critic rounds).
- **Refine Image** — upload a diagram, describe changes, choose 2K/4K + aspect
  ratio, and download the refined output.

## Run the CLI

```bash
cd dotnet
dotnet run --project PaperBanana.Cli -- \
  --dataset_name PaperBananaBench \
  --task_name diagram \
  --split_name test \
  --exp_mode dev_full \
  --retrieval_setting auto
```

Reads `data/<dataset>/<task>/<split>.json`, processes concurrently, and writes
results to `results/<dataset>_<task>/<exp_name>.json`.

## The `plot` task and the sidecar

For `plot`, the Visualizer/Vanilla agents produce Python **matplotlib code**,
which has no faithful .NET equivalent. To keep the plot pipeline fully working,
that code is executed by a small Python sidecar that reuses the original
execution worker and returns a base64 JPEG over HTTP:

```bash
cd dotnet/plot-sidecar
pip install -r requirements.txt
python plot_service.py            # listens on 0.0.0.0:8500
```

Point the .NET app at it via `services.plot_service_url` in
`configs/model_config.yaml` or the `PLOT_SERVICE_URL` env var. Diagram
generation does **not** need the sidecar.

## Notes

- `ImageSharp` 3.1.x (Six Labors Split License, free for OSS) replaces Pillow.
- The generic content-part list from `generation_utils.py` is modelled by
  `ContentPart`; each provider client converts it to its own wire format.
- `QueryData` is a dynamic string-keyed bag, matching the Python `data` dict
  whose keys (`target_diagram_desc0`, `..._base64_jpg`, …) are built at runtime.
```
