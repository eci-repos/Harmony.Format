
// /Harmony.Format.SemanticKernel/ToolInvoker.cs
using Harmony.Format.SemanticKernel.Tooling;
using Harmony.Tooling.Execution;
using Harmony.Tooling.Models;
using System.Text.Json;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel;

// Optional: compose the registry invoker for SK-backed registries.
public sealed class ToolInvoker : IToolInvoker
{
   private readonly RegistryToolInvoker _inner;
   public ToolInvoker(KernelToolRegistry registry) => _inner = new RegistryToolInvoker(registry);
   public Task<ToolResult> InvokeAsync(
      string recipient, JsonDocument input, ToolExecutionContext? ctx = null, 
      CancellationToken ct = default)
       => _inner.InvokeAsync(recipient, input, ctx, ct);
}
