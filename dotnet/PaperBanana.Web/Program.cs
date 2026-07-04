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

using PaperBanana.Core;
using PaperBanana.Core.Configuration;
using PaperBanana.Core.Llm;
using PaperBanana.Core.Utils;
using PaperBanana.Web.Components;
using PaperBanana.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Allow large method-section pastes and image uploads over the SignalR circuit.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024 * 1024; // 64 MB
});

// PaperBanana core services.
var workDir = PaperBananaOptions.ResolveWorkDir(builder.Configuration["PaperBanana:WorkDir"]);
var options = new PaperBananaOptions { WorkDir = workDir };
var modelConfig = ModelConfig.Load(Path.Combine(workDir, "configs", "model_config.yaml"));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(modelConfig);

// LLM HTTP calls (including image generation) can be slow.
builder.Services.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient("plot", c => c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddSingleton<GenerationClient>(sp =>
    new GenerationClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"), modelConfig));
builder.Services.AddSingleton<PlotExecutorClient>(sp =>
    new PlotExecutorClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("plot"), modelConfig));
builder.Services.AddSingleton<PaperVizFactory>();
builder.Services.AddScoped<GenerationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
