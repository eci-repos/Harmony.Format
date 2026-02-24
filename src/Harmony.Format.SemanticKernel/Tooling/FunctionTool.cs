using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

// Harmony.Format.SemanticKernel (thin DLL)
using Harmony.Tooling.Discovery;
using Harmony.Tooling.Contracts;
using Harmony.Tooling.Models;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel.Tooling;

/// <summary>
/// Function Tool is an ITool implementation that wraps a Semantic Kernel function (KernelFunction).
/// </summary>
public sealed class FunctionTool : ITool
{
   private readonly KernelFunction _kf;

   public FunctionTool(string name, KernelFunction kf)
   {
      Name = name; _kf = kf;
      Descriptor = new ToolDescriptor
      {
         Name = name,
         Version = "1.0.0",
         Flags = ToolFlags.Replayable, // adjust per function if you have metadata
         Provider = "SemanticKernel"
      };
   }

   public string Name { get; }
   public string Version => Descriptor.Version;
   public ToolDescriptor Descriptor { get; }

   public async Task<ToolResult> ExecuteAsync(
      JsonDocument input, ToolExecutionContext? context = null, CancellationToken ct = default)
   {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      try
      {
         var args = new KernelArguments();

         // Preserve nested JSON by passing raw strings for complex types
         if (input.RootElement.ValueKind == JsonValueKind.Object)
         {
            foreach (var p in input.RootElement.EnumerateObject())
            {
               args[p.Name] = p.Value.ValueKind switch
               {
                  JsonValueKind.String => p.Value.GetString()!,
                  JsonValueKind.Number => p.Value.ToString(),
                  JsonValueKind.True or JsonValueKind.False => p.Value.GetBoolean().ToString(),
                  JsonValueKind.Null => null!,
                  _ => p.Value.ToString()
               };
            }
         }
         else if (input.RootElement.ValueKind != JsonValueKind.Undefined)
         {
            args["value"] = input.RootElement.ToString();
         }

         context?.Diagnostics?.OnToolStart(new ToolInvocationStart
         {
            Tool = Name,
            Version = Version,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = context.SessionId,
            MessageIndex = context.MessageIndex,
            Inputs = (context.Redactor?.RedactInputs(Name, input.RootElement) ?? input.RootElement)
         });

         var result = await _kf.InvokeAsync(kernel: null, args, ct).ConfigureAwait(false);
         sw.Stop();

         var data = JsonSerializer.SerializeToElement(result);
         var cacheKey = $"{Name}:{ComputeStableInputHash(input)}";

         var end = new ToolInvocationEnd
         {
            Tool = Name,
            Version = Version,
            Timestamp = DateTimeOffset.UtcNow,
            Elapsed = sw.Elapsed,
            Ok = true,
            Data = context?.Redactor?.RedactOutputs(Name, data) ?? data,
            CacheKey = cacheKey,
            Flags = Descriptor.Flags.ToString().Split(", ")
         };
         context?.Diagnostics?.OnToolEnd(end);

         return new ToolResult { Ok = true, Data = data, CacheKey = cacheKey, Elapsed = sw.Elapsed };
      }
      catch (Exception ex) when (!ct.IsCancellationRequested)
      {
         sw.Stop();
         var error = new ToolError { Code = KnownErrorCodes.BackendError, Message = ex.Message };
         context?.Diagnostics?.OnToolEnd(new ToolInvocationEnd
         {
            Tool = Name,
            Version = Version,
            Timestamp = DateTimeOffset.UtcNow,
            Elapsed = sw.Elapsed,
            Ok = false,
            Error = error,
            Data = default
         });
         return new ToolResult { Ok = false, Error = error, Elapsed = sw.Elapsed };
      }
   }

   private static string ComputeStableInputHash(JsonDocument doc)
       => System.Convert.ToHexString(
          System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
             doc.RootElement.ToString())));
}
