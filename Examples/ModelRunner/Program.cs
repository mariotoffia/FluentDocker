using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelRunner
{
  /// <summary>
  /// Demonstrates FluentDocker's Docker Model Runner (local LLM) support. Each
  /// scenario uses its OWN fluent builder chain to *declare the setup* — select the
  /// model, set a persistent context size, pull it if absent — and then runs
  /// inference. Setup is expressed through the builder, not through imperative calls
  /// after the fact, mirroring the <c>Builder → WithinDriver → UseXxx</c> pattern
  /// used for containers, networks and volumes.
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
        if (!await RunnerIsReachableAsync(kernel))
        {
          Console.WriteLine("Docker Model Runner is not running.");
          Console.WriteLine("Enable it: Docker Desktop → Settings → AI → Enable Docker Model Runner (with host-side TCP).");
          return;
        }

        await ChatAsync(kernel);
        await StreamChatAsync(kernel);
        await EmbeddingsAsync(kernel);
        await ManagedServiceAsync(kernel);
        await ListModelsAsync(kernel);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\nModel Runner example aborted: {ex.Message}");
        Console.WriteLine("Ensure Docker Model Runner is enabled (Docker Desktop → Settings → AI) with host-side TCP.");
      }
    }

    /// <summary>Builds a minimal runner to confirm the runner is reachable.</summary>
    private static async Task<bool> RunnerIsReachableAsync(FluentDockerKernel kernel)
    {
      await using var runner = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .ForModel(ChatModel)
          .Build();

      var status = await runner.StatusAsync();
      if (status.Running)
        Console.WriteLine($"Model Runner reachable at {status.Endpoint}\n");

      return status.Running;
    }

    /// <summary>
    /// One-shot chat. The builder declares the setup — select the model, set a
    /// persistent context size, pull it if missing — then we run inference.
    /// </summary>
    private static async Task ChatAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Chat (one-shot) ==");
      await using var llm = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .ForModel(ChatModel)
          .WithContextSize(8192)
          .PullIfMissing()
          .Build();

      var reply = await llm.ChatAsync("Reply with exactly one word: the capital of France.");
      Console.WriteLine($"  {reply?.Trim()}\n");
    }

    /// <summary>Streaming chat — tokens arrive as they are generated.</summary>
    private static async Task StreamChatAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Chat (streaming) ==");
      await using var llm = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .ForModel(ChatModel)
          .PullIfMissing()
          .Build();

      Console.Write("  ");
      await foreach (var token in llm.ChatStreamAsync("Count from one to five."))
        Console.Write(token);
      Console.WriteLine("\n");
    }

    /// <summary>
    /// Embeddings — a separate runner set up for a dedicated embedding model. The
    /// builder pulls it if missing; <c>EmbedAsync</c> then uses that default model.
    /// </summary>
    private static async Task EmbeddingsAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Embeddings ==");
      await using var embedder = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .ForModel(EmbedModel)
          .PullIfMissing()
          .Build();

      var vector = await embedder.EmbedAsync("FluentDocker manages local LLMs.");
      var preview = string.Join(", ", vector.Take(4).Select(v => v.ToString("0.000")));
      Console.WriteLine($"  {vector.Count}-dim vector, first few: [{preview} …]\n");
    }

    /// <summary>
    /// A model as a managed service — the builder sets it up (pull, context size),
    /// then it loads on <c>StartAsync</c> and unloads on dispose.
    /// </summary>
    private static async Task ManagedServiceAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Managed model service ==");
      await using var model = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModel(ChatModel)
          .WithContextSize(4096)
          .PullIfMissing()
          .KeepRunning(false)   // unload on dispose
          .Build();

      await model.StartAsync();
      var answer = await model.Runner.ChatAsync("Say hello in French, one word.");
      Console.WriteLine($"  {answer?.Trim()}\n");
      // disposed here -> model unloaded
    }

    /// <summary>Lists local models — a runtime query over a fluently-built runner.</summary>
    private static async Task ListModelsAsync(FluentDockerKernel kernel)
    {
      Console.WriteLine("== Local models ==");
      await using var runner = new Builder()
          .WithinDriver(DriverId, kernel)
          .UseModelRunner()
          .Build();

      foreach (var m in await runner.ListAsync())
        Console.WriteLine($"  {m.Reference}  ({m.ParameterCount}, {m.Quantization}, {m.Size / (1024 * 1024)} MiB)");
      Console.WriteLine();
    }
  }
}
