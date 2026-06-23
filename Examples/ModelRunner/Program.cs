using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelRunner
{
  /// <summary>
  /// Demonstrates FluentDocker's Docker Model Runner (local LLM) support: pull/list,
  /// one-shot + streaming chat, embeddings, and a managed model lifecycle — all
  /// behind the same <c>Builder → WithinDriver → UseModelRunner()</c> pattern used
  /// for containers, networks and volumes.
  ///
  /// Prerequisite: Docker Model Runner must be enabled (Docker Desktop →
  /// Settings → AI → Enable Docker Model Runner, with host-side TCP turned on).
  /// The sample uses tiny models so it stays quick to pull.
  /// </summary>
  internal static class Program
  {
    private const string DriverId = "docker";
    private const string ChatModel = "ai/smollm2";          // ~256 MiB chat model
    private const string EmbedModel = "ai/embeddinggemma";  // small embeddings model

    private static async Task Main()
    {
      using var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();

      try
      {
        await RunAsync(kernel);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\nModel Runner example aborted: {ex.Message}");
        Console.WriteLine("Ensure Docker Model Runner is enabled (Docker Desktop → Settings → AI) with host-side TCP.");
      }
    }

    private static async Task RunAsync(FluentDockerKernel kernel)
    {
      await using var runner = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .ForModel(ChatModel)
          .Build();

      var status = await runner.StatusAsync();
      if (!status.Running)
      {
        Console.WriteLine("Docker Model Runner is not running.");
        Console.WriteLine("Enable it: Docker Desktop → Settings → AI → Enable Docker Model Runner (with host-side TCP).");
        return;
      }

      Console.WriteLine($"Model Runner reachable at {status.Endpoint}\n");

      await ManageModelsAsync(runner);
      await ChatAsync(runner);
      await StreamChatAsync(runner);
      await EmbeddingsAsync(runner);
      await ManagedServiceAsync(kernel);
    }

    /// <summary>1) Pull (with progress) and list local models.</summary>
    private static async Task ManageModelsAsync(IModelRunner runner)
    {
      Console.WriteLine("== Managing models ==");
      await runner.PullAsync(ModelReference.Parse(ChatModel),
          new Progress<ModelPullProgress>(p =>
          {
            if (p.Total > 0)
              Console.WriteLine($"  {p.Status} {p.Fraction:P0}");
          }));

      foreach (var m in await runner.ListAsync())
        Console.WriteLine($"  {m.Reference}  ({m.ParameterCount}, {m.Quantization}, {m.Size / (1024 * 1024)} MiB)");
      Console.WriteLine();
    }

    /// <summary>2) One-shot chat against the default model.</summary>
    private static async Task ChatAsync(IModelRunner runner)
    {
      Console.WriteLine("== Chat (one-shot) ==");
      var reply = await runner.ChatAsync("Reply with exactly one word: the capital of France.");
      Console.WriteLine($"  {reply?.Trim()}\n");
    }

    /// <summary>3) Streaming chat — tokens arrive as they are generated.</summary>
    private static async Task StreamChatAsync(IModelRunner runner)
    {
      if (!runner.Capabilities.SupportsStreaming)
        return;

      Console.WriteLine("== Chat (streaming) ==");
      Console.Write("  ");
      await foreach (var token in runner.ChatStreamAsync("Count from one to five."))
        Console.Write(token);
      Console.WriteLine("\n");
    }

    /// <summary>4) Embeddings — needs a dedicated embedding model.</summary>
    private static async Task EmbeddingsAsync(IModelRunner runner)
    {
      if (!runner.Capabilities.SupportsEmbeddings)
        return;

      Console.WriteLine("== Embeddings ==");
      await runner.PullAsync(ModelReference.Parse(EmbedModel));
      var vector = await runner.EmbedAsync("FluentDocker manages local LLMs.", ModelReference.Parse(EmbedModel));
      var preview = string.Join(", ", vector.Take(4).Select(v => v.ToString("0.000")));
      Console.WriteLine($"  {vector.Count}-dim vector, first few: [{preview} …]\n");
    }

    /// <summary>5) A model as a managed service — loads on Start, unloads on dispose.</summary>
    private static async Task ManagedServiceAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Managed model service ==");
      await using var model = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModel(ChatModel)
          .WithContextSize(4096)
          .KeepRunning(false)   // unload on dispose
          .Build();

      await model.StartAsync();
      var answer = await model.Runner.ChatAsync("Say hello in French, one word.");
      Console.WriteLine($"  {answer?.Trim()}");
      // disposed here -> model unloaded
    }
  }
}
