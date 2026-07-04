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

namespace PaperBanana.Web.Services;

/// <summary>Example inputs mirroring the demo.py "PaperVizAgent Framework" example.</summary>
public static class ExampleContent
{
    public const string Method = """
## Methodology: The PaperBanana Framework

In this section, we present the architecture of PaperBanana, a reference-driven agentic framework for
automated academic illustration. PaperBanana orchestrates a collaborative team of five specialized
agents—Retriever, Planner, Stylist, Visualizer, and Critic—to transform raw scientific content into
publication-quality diagrams and plots.

### Retriever Agent
Given the source context S and the communicative intent C, the Retriever Agent identifies the N most
relevant examples from a fixed reference set to guide the downstream agents. It adopts a generative
retrieval approach where a VLM ranks candidates by matching both research domain and diagram type, with
visual structure prioritized over topic similarity.

### Planner Agent
The Planner Agent serves as the cognitive core of the system. Taking the source context, communicative
intent, and retrieved examples, it performs in-context learning to translate the input into a
comprehensive and detailed textual description P of the target illustration.

### Stylist Agent
To ensure the output adheres to modern academic aesthetic standards, the Stylist Agent refines the
initial description into a stylistically optimized version using an automatically synthesized aesthetic
guideline covering color palette, shapes, lines, layout and typography.

### Visualizer Agent
The Visualizer Agent leverages an image generation model to transform the textual description into a
visual output. For statistical plots, it instead emits executable Python matplotlib code for numerical
precision.

### Critic Agent
The Critic Agent forms a closed-loop refinement mechanism with the Visualizer. It inspects the generated
image against the source context, identifies factual misalignments and visual glitches, and produces a
refined description that is fed back to the Visualizer. The loop iterates for T=3 rounds.
""";

    public const string Caption =
        "Figure 1: Overview of our PaperBanana framework. Given the source context and communicative " +
        "intent, we first apply a Linear Planning Phase to retrieve relevant reference examples and " +
        "synthesize a stylistically optimized description. We then use an Iterative Refinement Loop " +
        "(Visualizer and Critic agents) to transform the description into visual output and conduct " +
        "multi-round refinements to produce the final academic illustration.";
}
