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
/// KernelToolRegistry implements IToolRegistry by first checking an internal dictionary of 
/// registered tools, and if not found, attempting to resolve tools dynamically from the 
/// Semantic Kernel's plugins based on the recipient string format "plugin.function". This allows 
/// seamless integration of SK functions as tools without manual registration, while still 
/// supporting explicitly registered tools.
/// </summary>
public sealed class KernelToolRegistry : IToolRegistry
{
   private readonly Kernel _kernel;
   private readonly Dictionary<string, ITool> _byName = new(StringComparer.OrdinalIgnoreCase);

   public KernelToolRegistry(Kernel kernel) => 
      _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));

   public void Register(ITool tool) => _byName[tool.Name] = tool;

   public ITool? Resolve(string name) => 
      _byName.TryGetValue(name, out var t) ? t : TryResolveFromKernel(name);

   public IEnumerable<ToolDescriptor> List() => _byName.Values.Select(t => t.Descriptor);

   private ITool? TryResolveFromKernel(string recipient)
   {
      // recipient: "plugin.function"
      var dot = recipient.LastIndexOf('.');
      if (dot <= 0 || dot == recipient.Length - 1) return null;

      var plugin = recipient[..dot];
      var fn = recipient[(dot + 1)..];

      if (!_kernel.Plugins.TryGetFunction(plugin, fn, out var kf)) return null;
      var tool = new FunctionTool(recipient, kf);
      _byName[recipient] = tool;
      return tool;
   }
}
