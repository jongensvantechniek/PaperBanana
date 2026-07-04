// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using PaperBanana.Core.Agents;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Evaluation;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Pipeline;
using PaperBanana.Core.Utils;

namespace PaperBanana.Core;

/// <summary>
/// Builds a fully-wired <see cref="PaperVizProcessor"/> for a given experiment
/// configuration. Shared by the CLI and the Blazor web app.
/// </summary>
public sealed class PaperVizFactory
{
    private readonly GenerationClient _generation;
    private readonly PlotExecutorClient _plotExecutor;

    public PaperVizFactory(GenerationClient generation, PlotExecutorClient plotExecutor)
    {
        _generation = generation;
        _plotExecutor = plotExecutor;
    }

    public PaperVizProcessor CreateProcessor(ExpConfig expConfig) => new(
        expConfig,
        new VanillaAgent(expConfig, _generation, _plotExecutor),
        new PlannerAgent(expConfig, _generation),
        new VisualizerAgent(expConfig, _generation, _plotExecutor),
        new StylistAgent(expConfig, _generation),
        new CriticAgent(expConfig, _generation),
        new RetrieverAgent(expConfig, _generation),
        new PolishAgent(expConfig, _generation),
        new EvalToolkit(_generation));
}
