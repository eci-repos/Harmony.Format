using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

/// <summary>
/// Represents a container for a collection of Harmony messages, providing methods to parse, access, 
/// and extract structured or plain content from the message set.
/// </summary>
/// <remarks>A HarmonyEnvelope typically contains a sequence of messages exchanged between different
/// roles, such as 'system' and 'user'. It supports parsing from JSON and offers methods to retrieve
/// structured scripts and plain prompts for further processing. This type is immutable in structure
/// but allows modification of its Messages collection. Thread safety is not guaranteed; synchronize 
/// access if used concurrently.</remarks>
public sealed class HarmonyEnvelope
{
   [JsonPropertyName("hrfVersion")]
   public string HRFVersion { get; set; } = "1.0.0";

   [JsonPropertyName("messages")]
   public List<HarmonyMessage> Messages { get; set; } = new();

   /// <summary>
   /// Deserialize a JSON string into a HarmonyEnvelope object.
   /// </summary>
   /// <param name="json">given option</param>
   /// <returns>HarmonyEnvelope is returned</returns>
   /// <exception cref="InvalidOperationException"></exception>
   public static HarmonyEnvelope Deserialize(string json)
   {
      var options = new JsonSerializerOptions
      {
         PropertyNameCaseInsensitive = true
      };
      options.Converters.Add(new JsonStringEnumConverter());
      return JsonSerializer.Deserialize<HarmonyEnvelope>(json, options)
             ?? throw new InvalidOperationException("Invalid Harmony JSON.");
   }

   #region -- 4.00 - ("harmony-script") Script Extraction

   /// <summary>
   /// Extracts and deserializes the first HarmonyScript from a list of HarmonyMessage objects that
   /// contains a valid harmony-script payload.
   /// </summary>
   /// <remarks>Only messages with the role "system", a content type of "harmony-script", and an 
   /// object-valued content are considered. The method validates the script before deserialization.
   /// If multiple valid harmony-script messages exist, only the first is returned.</remarks>
   /// <exception cref="InvalidOperationException">Thrown if no valid harmony-script message is 
   /// found in the collection, or if deserialization of the harmony-script fails.</exception>
   public HarmonyScript? GetScript()
   {
      return GetScript(Messages);
   }

   /// <summary>
   /// Extracts and deserializes the first HarmonyScript from a list of HarmonyMessage objects that
   /// contains a valid harmony-script payload.
   /// </summary>
   /// <remarks>Only messages with the role "system", a content type of "harmony-script", and an 
   /// object-valued content are considered. The method validates the script before deserialization.
   /// If multiple valid harmony-script messages exist, only the first is returned.</remarks>
   /// <param name="messages">The collection of messages to search for a system message containing 
   /// a harmony-script. Cannot be null.</param>
   /// <returns>A HarmonyScript object deserialized from the first matching message in the
   /// collection.</returns>
   /// <exception cref="InvalidOperationException">Thrown if no valid harmony-script message is 
   /// found in the collection, or if deserialization of the harmony-script fails.</exception>
   public static HarmonyScript? GetScript(List<HarmonyMessage> messages)
   {
      // Preferred runtime location: assistant + commentary
      var candidates = messages.Where(m =>
          string.Equals(m.ContentType, "harmony-script", StringComparison.OrdinalIgnoreCase) &&
          m.Content.ValueKind == JsonValueKind.Object &&
          (
              // Primary: assistant tool orchestration/prelude
              (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(m.Channel?.ToString(), "commentary", StringComparison.OrdinalIgnoreCase))
              ||
              // Optional: allow system/developer-provided script templates
              string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(m.Role, "developer", StringComparison.OrdinalIgnoreCase)
          )
      );

      foreach (var msg in candidates)
      {
         // Validate the script element (anchored $ref resolution as discussed previously)
         var scriptValidationError = HarmonySchemaValidator.TryValidateScript(msg.Content);
         if (scriptValidationError != null)
         {
            // If you prefer, continue scanning other candidates instead of throwing.
            throw new InvalidOperationException(
                $"harmony-script validation failed: {scriptValidationError.Message}");
         }

         // Deserialize after validation
         var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
         options.Converters.Add(new JsonStringEnumConverter());
         try
         {
            return msg.Content.Deserialize<HarmonyScript>(options);
         }
         catch (JsonException ex)
         {
            throw new InvalidOperationException("Failed to deserialize harmony-script.", ex);
         }
      }

      throw new InvalidOperationException("No harmony-script found in messages.");
   }

   /// <summary>
   /// Call the GetScript with the messages from the envelope.
   /// </summary>
   /// <param name="envelope">envelope</param>
   /// <returns>A HarmonyScript object deserialized from the first matching message in the
   /// collection.</returns>
   public static HarmonyScript GetScript(HarmonyEnvelope envelope)
   {
      return GetScript(envelope.Messages);
   }

   public IEnumerable<(HarmonyChannel? Channel, string Content)> GetPlainSystemPrompts()
   {
      foreach (var msg in Messages)
      {
         if (msg.Role == "system" &&
             !string.Equals(
                msg.ContentType, "harmony-script", StringComparison.OrdinalIgnoreCase) &&
             msg.Content.ValueKind == JsonValueKind.String)
         {
            yield return (msg.Channel, msg.Content.GetString() ?? string.Empty);
         }
      }
   }

   public (HarmonyChannel? Channel, string Content)? GetUserMessage()
   {
      foreach (var msg in Messages)
      {
         if (msg.Role == "user" && msg.Content.ValueKind == JsonValueKind.String)
         {
            return (msg.Channel, msg.Content.GetString() ?? string.Empty);
         }
      }
      return null;
   }

   #endregion
   #region -- 4.00 - High-level HRF validation entry point

   /// <summary>
   /// Performs HRF validation on this envelope.
   /// 1) Validates against the JSON Schema via <see cref="HarmonySchemaValidator"/>.
   /// 2) Applies additional semantic HRF checks (roles, channels, terminations, content types).
   /// 
   /// Returns null if the envelope is HRF-valid; otherwise returns a populated
   /// <see cref="HarmonyError"/>.
   /// </summary>
   public HarmonyError? ValidateForHrf()
   {
      var errors = new List<string>();

      // ---------- 1. Schema-level validation ----------
      HarmonyError? schemaError = null;
      try
      {
         // Serialize the current instance and run it through the schema validator
         var jsonOpts = new JsonSerializerOptions
         {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
         };
         jsonOpts.Converters.Add(new JsonStringEnumConverter());
         var json = JsonSerializer.Serialize(this, jsonOpts);

         schemaError = HarmonySchemaValidator.TryValidateEnvelope(json);
      }
      catch (InvalidOperationException ex)
      {
         // Typically means HarmonySchemaValidator.Initialize(...) was not called
         return new HarmonyError
         {
            Code = "HRF_SCHEMA_NOT_INITIALIZED",
            Message = 
               "HarmonySchemaValidator is not initialized. Call Initialize() before validation.",
            Details = ex.Message
         };
      }

      if (schemaError != null)
      {
         // Bubble up schema problems directly
         return new HarmonyError
         {
            Code = schemaError.Code,
            Message = schemaError.Message,
            Details = schemaError.Details
         };
      }

      // ---------- 2. Semantic HRF validation ----------

      if (string.IsNullOrWhiteSpace(HRFVersion))
      {
         errors.Add("HRFVersion must be specified.");
      }

      if (Messages is null || Messages.Count == 0)
      {
         errors.Add("Envelope must contain at least one message.");
      }

      // track termination tokens...
      var callCount = 0;
      var returnCount = 0;
      var endCount = 0;

      // Track termination markers to avoid inconsistent use
      var terminationMessages = new List<int>();

      for (int i = 0; i < Messages.Count; i++)
      {
         var msg = Messages[i];

         // --- Role checks ---
         if (string.IsNullOrWhiteSpace(msg.Role))
         {
            errors.Add($"Message[{i}]: Role is required.");
         }

         var roleLower = msg.Role?.Trim().ToLowerInvariant() ?? string.Empty;
         bool isAssistant = roleLower == "assistant";
         bool isUser = roleLower == "user";
         bool isSystem = roleLower == "system";

         // --- Termination semantics ---
         if (msg.Termination is not null)
         {
            // Only assistant messages are allowed to carry termination markers
            if (!isAssistant)
            {
               errors.Add(
                  $"Message[{i}]: Only assistant messages may carry a termination token, " +
                  $"but role='{msg.Role}' has termination='{msg.Termination}'.");
            }
            else
            {
               // to track global termination semantics
               switch (msg.Termination)
               {
                  case HarmonyTermination.call: callCount++; break;
                  case HarmonyTermination.@return: returnCount++; break;
                  case HarmonyTermination.end: endCount++; break;
                  default:
                     errors.Add($"Message[{i}]: Unknown termination='{msg.Termination}'. "
                        + "Expected 'call', 'return', or 'end'.");
                     break;
               }

               terminationMessages.Add(i);
            }
         }

         // --- ContentType semantics ---
         if (!string.IsNullOrWhiteSpace(msg.ContentType))
         {
            var ct = msg.ContentType.Trim().ToLowerInvariant();

            // Restrict to a known set for HRF compliance: plain, json, harmony-script
            if (ct != "text" && ct != "json" && ct != "harmony-script")
            {
               errors.Add(
                  $"Message[{i}]: Unsupported contentType='{msg.ContentType}'. " +
                  "Expected 'json' or 'harmony-script' or null/empty for plain text.");
            }

            if (ct == "harmony-script")
            {
               // Must be a JSON object representing a HarmonyScript
               if (msg.Content.ValueKind != JsonValueKind.Object)
               {
                  errors.Add(
                     $"Message[{i}]: contentType='harmony-script' " +
                     "requires object-valued JSON content.");
               }
               else
               {
                  // Validate the script itself against the HarmonyScript schema
                  try
                  {
                     HarmonySchemaValidator.TryValidateScript(msg.Content);
                  }
                  catch (InvalidOperationException ex)
                  {
                     errors.Add(
                        $"Message[{i}]: harmony-script validation failed: {ex.Message}");
                  }
               }
            }
         }
         else
         {
            // No contentType -> treat content as plain text
            // We can optionally assert that Content is a JSON string
            if (msg.Content.ValueKind != JsonValueKind.String)
            {
               errors.Add(
                  $"Message[{i}]: Missing contentType implies plain text, " +
                  $"but content is JSON kind '{msg.Content.ValueKind}'.");
            }
         }

         // --- Assistant channel semantics ---
         if (isAssistant)
         {
            // In HRF, assistant messages must specify a recognized channel
            if (!Enum.IsDefined(typeof(HarmonyChannel), msg.Channel))
            {
               errors.Add(
                  $"Message[{i}]: Assistant message must specify a valid HarmonyChannel, " +
                  $"but got '{msg.Channel}'.");
            }
         }
         else
         {
            // For non-assistant roles, Channel is not semantically required; no strict check here.
            // If desired, you could assert a neutral default in future.
         }
      }

      // --- Global termination semantics ---
      // HRF convention: at most one assistant termination marker of each kind is expected.
      // We only enforce a soft constraint here to avoid over-constraining advanced use:
      if (terminationMessages.Count > 1)
      {
         bool issueFound = false;
         if (callCount > 1)
         {
            errors.Add("Multiple 'call' terminations found. HRF expects at most one assistant "
               + "'call' per envelope.");
            issueFound = true;
         }
         if (returnCount > 1)
         {
            errors.Add("Multiple 'return' terminations found. HRF expects at most one assistant "
               + "'return' per envelope.");
            issueFound = true;
         }
         if (endCount > 1)
         {
            errors.Add("Multiple 'end' terminations found. HRF expects at most one assistant "
               + "'end' per envelope.");
            issueFound = true;
         }

         if (issueFound)
            errors.Add(
               "Multiple messages carry termination markers. " +
               "HRF workflows typically expect at most one termination-bearing assistant message " +
               "per envelope.");
      }

      // ---------- Result aggregation ----------
      if (errors.Count == 0)
      {
         return null; // HRF-valid
      }

      return new HarmonyError
      {
         Code = "HRF_SEMANTIC_VALIDATION_FAILED",
         Message = "Harmony envelope failed semantic HRF validation.",
         Details = errors
      };
   }

   /// <summary>
   /// Convenience static wrapper to validate an envelope instance for HRF compliance.
   /// Returns null if valid; otherwise returns a populated <see cref="HarmonyError"/>.
   /// </summary>
   public static HarmonyError? ValidateForHrf(HarmonyEnvelope envelope)
      => envelope?.ValidateForHrf();

   #endregion

}
