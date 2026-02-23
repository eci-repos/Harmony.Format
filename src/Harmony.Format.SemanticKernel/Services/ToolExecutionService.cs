using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel;


/// <summary>
/// Semantic Kernel-based implementation of IHrfToolRouter.
/// Maps HRF tool calls (recipient + args) to SK plugin functions.
/// </summary>
public sealed class ToolExecutionService : IToolExecutionService
{
   private readonly Kernel _kernel;

   /// <summary>
   /// Creates a new SK-backed tool router.
   /// </summary>
   /// <param name="kernel">The Semantic Kernel instance containing plugins and functions.</param>
   public ToolExecutionService(Kernel kernel)
   {
      _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
   }

   /// <summary>
   /// Invokes a tool based on HRF recipient string and arguments.
   /// Recipient must be of the form "plugin.function".
   /// </summary>
   public async Task<object?> InvokeToolAsync(
       string recipient,
       IReadOnlyDictionary<string, object?> args,
       CancellationToken ct = default)
   {
      if (string.IsNullOrWhiteSpace(recipient))
      {
         throw new ArgumentException("Recipient must be specified.", nameof(recipient));
      }

      // Expect "plugin.function"
      var parts = recipient.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length != 2)
      {
         throw new InvalidOperationException(
             $"Invalid recipient '{recipient}', expected 'plugin.function'.");
      }

      var pluginName = parts[0];
      var functionName = parts[1];

      // Lookup SK function
      var func = _kernel.Plugins.GetFunction(pluginName, functionName);
      if (func is null)
      {
         throw new InvalidOperationException(
             $"Tool '{recipient}' not found in SK kernel plugins.");
      }

      // Normalize HRF args into strings for SK context variables
      var normalizedArgs = NormalizeParameters(args);

      // Build KernelArguments
      var kernelArgs = new KernelArguments();
      foreach (var (key, value) in normalizedArgs)
      {
         if (value is null)
            continue;

         kernelArgs[key] = value;
      }

      // Invoke SK function
      var result = await func.InvokeAsync(_kernel, kernelArgs, ct).ConfigureAwait(false);

      // Try to get a strongly-typed value first
      if (result.GetValue<object?>() is not null)
      {
         return result.GetValue<object?>();
      }

      // Fallback to string representation
      var text = result.ToString();
      return string.IsNullOrWhiteSpace(text) ? null : text;
   }

   /// <summary>
   /// Normalizes HRF argument values into string representations suitable for SK context variables.
   /// </summary>
   private static IDictionary<string, string?> NormalizeParameters(
       IReadOnlyDictionary<string, object?> args)
   {
      var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

      foreach (var (key, value) in args)
      {
         switch (value)
         {
            case null:
               dict[key] = null;
               break;

            case string s:
               dict[key] = s;
               break;

            case bool b:
               dict[key] = b ? "true" : "false";
               break;

            case IFormattable f:
               // Covers numeric types, DateTime, etc.
               dict[key] = f.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
               break;

            default:
               // Fallback: JSON-serialize complex objects
               dict[key] = System.Text.Json.JsonSerializer.Serialize(value);
               break;
         }
      }

      return dict;
   }

}


