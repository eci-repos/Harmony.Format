using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;


// Polymorphic steps
[JsonConverter(typeof(HarmonyStepJsonConverter))]
public abstract class HarmonyStep
{
   [JsonPropertyName("type")]
   public string Type { get; init; } = string.Empty;

   /// <summary>
   /// Performs semantic HRF validation on the step instance.
   /// Implementations should throw <see cref="JsonException"/> if the step
   /// is malformed or violates HarmonyScript HRF conventions.
   /// </summary>
   public virtual void Validate()
   {
      if (string.IsNullOrWhiteSpace(Type))
      {
         throw new JsonException("Step is missing required 'type' property.");
      }
   }

   /// <summary>
   /// Helper to check if a string has material content (non-empty and not just the '.' sentinel).
   /// </summary>
   protected static bool HasMaterial(string? s)
      => !string.IsNullOrWhiteSpace(s) && s.Trim() != ".";
}

public sealed class ExtractInputStep : HarmonyStep
{
   // Maps varName -> expression, e.g. "cuisineRegion" : "$input.cuisineRegion"
   [JsonPropertyName("output")]
   public Dictionary<string, string> Output { get; set; } = new();

   public override void Validate()
   {
      base.Validate();

      if (!Type.Equals("extract-input", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException(
            $"ExtractInputStep expects type 'extract-input', got '{Type}'.");
      }

      if (Output is null || Output.Count == 0)
      {
         throw new JsonException(
            "extract-input step requires a non-empty 'output' mapping.");
      }

      foreach (var kvp in Output)
      {
         if (string.IsNullOrWhiteSpace(kvp.Key))
         {
            throw new JsonException(
               "extract-input step has an empty variable name in 'output' map.");
         }

         if (string.IsNullOrWhiteSpace(kvp.Value))
         {
            throw new JsonException(
               $"extract-input step output for '{kvp.Key}' has an empty expression.");
         }
      }
   }
}

public sealed class ToolCallStep : HarmonyStep
{
   [JsonPropertyName("recipient")]
   public string Recipient { get; set; } = string.Empty; // e.g. "functions.search_recipes"

   [JsonPropertyName("channel")]
   public string Channel { get; set; } = "commentary"; // informational

   [JsonPropertyName("args")]
   public Dictionary<string, JsonElement> Args { get; set; } = new();

   [JsonPropertyName("save_as")]
   public string SaveAs { get; set; } = string.Empty;

   public override void Validate()
   {
      base.Validate();

      if (!Type.Equals("tool-call", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException(
            $"ToolCallStep expects type 'tool-call', got '{Type}'.");
      }

      if (string.IsNullOrWhiteSpace(Recipient))
      {
         throw new JsonException("tool-call step is missing 'recipient'.");
      }

      // Basic HRF expectation: plugin.functionName format
      if (!Recipient.Contains('.', StringComparison.Ordinal))
      {
         throw new JsonException(
            $"tool-call recipient '{Recipient}' must be of form 'plugin.functionName'.");
      }

      if (string.IsNullOrWhiteSpace(Channel))
      {
         throw new JsonException(
            "tool-call step is missing 'channel'. Expected 'commentary'.");
      }

      if (!Channel.Equals("commentary", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException(
            $"tool-call step must use channel='commentary', got '{Channel}'.");
      }

      if (string.IsNullOrWhiteSpace(SaveAs))
      {
         throw new JsonException(
            "tool-call step requires 'save_as' to name the variable where results are stored.");
      }

      if (Args is null)
      {
         throw new JsonException("tool-call step 'args' must not be null.");
      }
   }
}

public sealed class IfStep : HarmonyStep
{
   [JsonPropertyName("condition")]
   public string Condition { get; set; } = string.Empty;

   [JsonPropertyName("then")]
   public List<HarmonyStep> Then { get; set; } = new();

   [JsonPropertyName("else")]
   public List<HarmonyStep> Else { get; set; } = new();

   public override void Validate()
   {
      base.Validate();

      if (!Type.Equals("if", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException($"IfStep expects type 'if', got '{Type}'.");
      }

      if (string.IsNullOrWhiteSpace(Condition))
      {
         throw new JsonException("if step is missing 'condition'.");
      }

      // Ensure lists are not null to avoid surprises at execution time
      Then ??= new List<HarmonyStep>();
      Else ??= new List<HarmonyStep>();

      // Optionally: validate nested steps as well (deep validation)
      foreach (var step in Then)
      {
         step?.Validate();
      }

      foreach (var step in Else)
      {
         step?.Validate();
      }
   }
}

public sealed class AssistantMessageStep : HarmonyStep
{
   [JsonPropertyName("channel")]
   public string Channel { get; set; } = "final"; // analysis | final

   // Either content or content_template may appear (your example uses ".")
   [JsonPropertyName("content")]
   public string? Content { get; set; }

   [JsonPropertyName("content_template")]
   public string? ContentTemplate { get; set; }

   public override void Validate()
   {
      base.Validate();

      if (!Type.Equals("assistant-message", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException(
            $"AssistantMessageStep expects type 'assistant-message', got '{Type}'.");
      }

      if (string.IsNullOrWhiteSpace(Channel))
      {
         throw new JsonException(
            "assistant-message step is missing 'channel'. Expected 'analysis' or 'final'.");
      }

      var ch = Channel.Trim().ToLowerInvariant();
      if (ch != "analysis" && ch != "final")
      {
         throw new JsonException(
            $"assistant-message step must use channel='analysis' or 'final', got '{Channel}'.");
      }

      // HRF semantics: you may use '.' or empty to signal "let the LLM decide"
      // We only forbid both Content and ContentTemplate from having material text simultaneously.
      bool hasContent = HasMaterial(Content);
      bool hasTemplate = HasMaterial(ContentTemplate);

      if (hasContent && hasTemplate)
      {
         throw new JsonException(
            "assistant-message step should not have both 'content' and 'content_template' " +
            "with non-trivial text. Use one or the other.");
      }
   }
}

public sealed class HaltStep : HarmonyStep 
{
   public override void Validate()
   {
      base.Validate();

      if (!Type.Equals("halt", StringComparison.OrdinalIgnoreCase))
      {
         throw new JsonException($"HaltStep expects type 'halt', got '{Type}'.");
      }
   }
}

// Polymorphic converter
public sealed class HarmonyStepJsonConverter : JsonConverter<HarmonyStep>
{
   public override HarmonyStep? Read(
      ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
   {
      using var doc = JsonDocument.ParseValue(ref reader);
      if (!doc.RootElement.TryGetProperty("type", out var typeProp))
         throw new JsonException("Step missing 'type'.");

      var type = typeProp.GetString() ?? string.Empty;
      HarmonyStep step = type switch
      {
         "extract-input" => doc.RootElement.Deserialize<ExtractInputStep>(options)!,
         "tool-call" => doc.RootElement.Deserialize<ToolCallStep>(options)!,
         "if" => doc.RootElement.Deserialize<IfStep>(options)!,
         "assistant-message" => doc.RootElement.Deserialize<AssistantMessageStep>(options)!,
         "halt" => doc.RootElement.Deserialize<HaltStep>(options)!,
         _ => throw new JsonException($"Unknown step type '{type}'.")
      };

      // Perform semantic HRF validation at parse time
      step.Validate();

      return step;
   }

   public override void Write(
      Utf8JsonWriter writer, HarmonyStep value, JsonSerializerOptions options)
   {
      JsonSerializer.Serialize(writer, (object)value, value.GetType(), options);
   }

}

