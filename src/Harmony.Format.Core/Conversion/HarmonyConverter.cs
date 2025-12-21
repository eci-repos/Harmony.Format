using Json.Schema;
using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

#region -- 4.00 - FormatToJsonConverter --

/// <summary>
/// Converts native HRF text into JSON HRF envelopes (and strongly-typed HarmonyEnvelope).
/// </summary>
public static class FormatToJsonConverter
{
   private static readonly JsonSerializerOptions JsonOpts = new()
   {
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true,
      Converters =
         {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
         }
   };

   /// <summary>
   /// Parses native HRF text (with &lt;|start|&gt;, &lt;|message|&gt;, etc.) into a JSON HRF 
   /// envelope string.</summary>
   /// <param name="hrfText">The raw HRF text.</param>
   /// <param name="hrfVersion">HRF version to stamp into the envelope (default: "1.0.0").</param>
   /// <returns>A JSON string representing a <see cref="HarmonyEnvelope"/>.</returns>
   public static string ConvertHarmonyTextToEnvelopeJson(string hrfText, string hrfVersion = "1.0.0")
   {
      if (string.IsNullOrWhiteSpace(hrfText))
         throw new ArgumentException("HRF text is empty.", nameof(hrfText));

      var parser = new HarmonyParser();
      var conversation = parser.ParseConversation(hrfText);

      var envelope = new HarmonyEnvelope
      {
         HRFVersion = hrfVersion,
         Messages = conversation.Messages
      };

      var json = JsonSerializer.Serialize(envelope, JsonOpts);
      return json;
   }

   /// <summary>
   /// Parses native HRF text into a strongly-typed <see cref="HarmonyEnvelope"/>.
   /// </summary>
   /// <param name="hrfText">The raw HRF text.</param>
   /// <param name="hrfVersion">HRF version to stamp into the envelope (default: "1.0.0").</param>
   /// <returns>A populated <see cref="HarmonyEnvelope"/> instance.</returns>
   public static HarmonyEnvelope ConvertHrfTextToEnvelope(string hrfText, string hrfVersion = "1.0.0")
   {
      var json = ConvertHarmonyTextToEnvelopeJson(hrfText, hrfVersion);
      return JsonSerializer.Deserialize<HarmonyEnvelope>(json, JsonOpts)
         ?? throw new InvalidOperationException("Failed to deserialize HRF envelope from JSON.");
   }
}

#endregion
#region -- 4.00 - JsonToFormatConverter --

/// <summary>
/// Converts JSON HRF envelopes (and HarmonyEnvelope instances) back into native HRF text.
/// </summary>
public static class JsonToFormatConverter
{
   private static readonly JsonSerializerOptions JsonOpts = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true
   };

   /// <summary>
   /// Converts a JSON HRF envelope string back into native HRF text representation.
   /// </summary>
   /// <param name="envelopeJson">The JSON string representing a <see cref="HarmonyEnvelope"/>.
   /// </param>
   /// <returns>Native HRF text using &lt;|start|&gt; / &lt;|message|&gt; / &lt;|end|&gt; etc.
   /// </returns>
   public static string ConvertEnvelopeJsonToHarmonyText(string envelopeJson)
   {
      if (string.IsNullOrWhiteSpace(envelopeJson))
         throw new ArgumentException("Envelope JSON is empty.", nameof(envelopeJson));

      var envelope = JsonSerializer.Deserialize<HarmonyEnvelope>(envelopeJson, JsonOpts)
         ?? throw new InvalidOperationException("Failed to deserialize HarmonyEnvelope from JSON.");

      return ConvertEnvelopeToHarmonyText(envelope);
   }

   /// <summary>
   /// Converts a <see cref="HarmonyEnvelope"/> instance back into native HRF text.
   /// </summary>
   /// <param name="envelope">The envelope to serialize.</param>
   /// <returns>Native HRF text.</returns>
   public static string ConvertEnvelopeToHarmonyText(HarmonyEnvelope envelope)
   {
      if (envelope is null)
         throw new ArgumentNullException(nameof(envelope));

      var sb = new StringBuilder();

      foreach (var msg in envelope.Messages)
      {
         AppendMessageAsNativeHrf(sb, msg);
         sb.AppendLine(); // blank line between messages for readability
      }

      return sb.ToString();
   }

   /// <summary>
   /// Renders a single <see cref="HarmonyMessage"/> as native HRF text into the given
   /// StringBuilder. Format is the inverse of what <see cref="HarmonyParser"/> expects.
   /// </summary>
   private static void AppendMessageAsNativeHrf(StringBuilder sb, HarmonyMessage msg)
   {
      if (msg is null) throw new ArgumentNullException(nameof(msg));

      // <|start|>
      sb.AppendLine(HarmonyTokens.Start);

      // ROLE line (raw string)
      sb.AppendLine(msg.Role ?? string.Empty);

      // Optional <|channel|> header
      if (msg.Channel != null)
      {
         var channelName =
            msg.Channel?.ToString().ToLowerInvariant(); // analysis | commentary | final
         sb.AppendLine(HarmonyTokens.Channel);
         sb.Append(channelName);

         if (!string.IsNullOrWhiteSpace(msg.Recipient))
         {
            sb.Append(" to=");
            sb.Append(msg.Recipient);
         }

         sb.AppendLine();
      }

      // Optional <|constrain|> contentType
      if (!string.IsNullOrWhiteSpace(msg.ContentType))
      {
         sb.AppendLine(HarmonyTokens.Constrain);
         sb.AppendLine(msg.ContentType);
      }

      // <|message|>
      sb.AppendLine(HarmonyTokens.Message);

      // Content: depends on contentType
      string contentText = RenderContentForNative(msg);
      sb.AppendLine(contentText);

      // Termination token
      var termToken = msg.Termination switch
      {
         HarmonyTermination.end => HarmonyTokens.End,
         HarmonyTermination.call => HarmonyTokens.Call,
         HarmonyTermination.@return => HarmonyTokens.Return,
         null => null, // default to null if not specified
         _ => null
      };

      sb.AppendLine(termToken);
   }

   /// <summary>
   /// Renders the content field of a message as plain text for HRF.
   /// If contentType is json or harmony-script, we emit JSON; otherwise, we emit the raw string.
   /// </summary>
   private static string RenderContentForNative(HarmonyMessage msg)
   {
      var contentType = msg.ContentType?.Trim().ToLowerInvariant();

      if (!string.IsNullOrWhiteSpace(contentType) &&
          (contentType == HarmonyConstants.ContentTypeJson ||
          contentType == HarmonyConstants.ContentTypeScript))
      {
         // Serialize the JsonElement back to JSON text
         return JsonSerializer.Serialize(msg.Content, JsonOpts);
      }

      // Plain-text: content is expected to be a JSON string
      if (msg.Content.ValueKind == JsonValueKind.String)
      {
         return msg.Content.GetString() ?? string.Empty;
      }

      // Fallback: serialize as JSON for unexpected kinds
      return JsonSerializer.Serialize(msg.Content, JsonOpts);
   }

}

#endregion

/// <summary>
/// High-level conversion utilities for Harmony Response Format (HRF):
/// - HRF native text (<|start|> ... <|end|>) -> JSON envelope (schema-valid)
/// - JSON envelope -> HRF native text
///
/// Notes:
/// The provided harmony_envelope_schema.json only allows a root object with a "messages" property
/// and additionalProperties=false. Therefore, schema-valid JSON MUST NOT include extra root
/// properties (e.g., HRFVersion).
/// </summary>
public static class HarmonyConverter
{
   private static readonly JsonSerializerOptions JsonOut = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
   };

   /// <summary>
   /// Reads an HRF text file and converts it into a schema-valid JSON envelope string.
   /// </summary>
   public static string ConvertHrfFileToValidatedEnvelopeJson(
      string hrfFilePath,
      string envelopeSchemaFilePath)
   {
      if (string.IsNullOrWhiteSpace(hrfFilePath))
         throw new ArgumentException("HRF file path is required.", nameof(hrfFilePath));
      if (!File.Exists(hrfFilePath))
         throw new FileNotFoundException($"HRF file not found at '{hrfFilePath}'.", hrfFilePath);

      if (string.IsNullOrWhiteSpace(envelopeSchemaFilePath))
         throw new ArgumentException("Schema file path is required.", nameof(envelopeSchemaFilePath));
      if (!File.Exists(envelopeSchemaFilePath))
         throw new FileNotFoundException(
            $"Schema file not found at '{envelopeSchemaFilePath}'.", envelopeSchemaFilePath);

      var hrfText = File.ReadAllText(hrfFilePath);
      var schemaText = File.ReadAllText(envelopeSchemaFilePath);

      return ConvertHrfTextToValidatedEnvelopeJson(hrfText, schemaText);
   }

   /// <summary>
   /// Converts HRF native text into a schema-valid JSON envelope string.
   /// </summary>
   /// <remarks>
   /// The emitted JSON is validated against the provided schema text. If invalid, an exception is
   /// thrown with schema evaluation details.
   /// </remarks>
   public static string ConvertHrfTextToValidatedEnvelopeJson(
      string hrfText,
      string envelopeSchemaJson)
   {
      if (string.IsNullOrWhiteSpace(hrfText))
         throw new ArgumentException("HRF text is empty.", nameof(hrfText));
      if (string.IsNullOrWhiteSpace(envelopeSchemaJson))
         throw new ArgumentException("Envelope schema JSON is empty.", nameof(envelopeSchemaJson));

      // 1) Parse HRF text to messages
      var parser = new HarmonyParser();
      var convo = parser.ParseConversation(hrfText);

      // 2) Produce a schema-valid root object: ONLY { "messages": [...] }
      var schemaValidEnvelope = new
      {
         messages = convo.Messages
      };

      // 3) Serialize
      var json = JsonSerializer.Serialize(schemaValidEnvelope, JsonOut);

      // 4) Validate
      ValidateEnvelopeJsonOrThrow(json, envelopeSchemaJson);

      return json;
   }

   /// <summary>
   /// Converts HRF native text into a strongly-typed <see cref="HarmonyEnvelope"/> and validates
   /// the *JSON rendering* against the provided schema.
   /// </summary>
   /// <remarks>
   /// Since the schema only permits "messages" at the root, the JSON used for schema validation
   /// is produced from an anonymous shape. The returned HarmonyEnvelope can still carry HRFVersion
   /// for runtime/semantic checks.
   /// </remarks>
   public static HarmonyEnvelope ConvertHrfTextToEnvelopeValidatedBySchema(
      string hrfText,
      string envelopeSchemaJson,
      string hrfVersion = "1.0")
   {
      _ = ConvertHrfTextToValidatedEnvelopeJson(hrfText, envelopeSchemaJson);

      // We validated the schema-valid shape, now return the strongly-typed envelope.
      var parser = new HarmonyParser();
      var convo = parser.ParseConversation(hrfText);

      return new HarmonyEnvelope
      {
         HRFVersion = string.IsNullOrWhiteSpace(hrfVersion) ? "1.0" : hrfVersion,
         Messages = convo.Messages
      };
   }

   /// <summary>
   /// Validates a JSON envelope string against a Draft 2020-12 JSON Schema string.
   /// Throws <see cref="InvalidOperationException"/> on failure, with a structured error payload.
   /// </summary>
   public static void ValidateEnvelopeJsonOrThrow(string envelopeJson, string envelopeSchemaJson)
   {
      try
      {
         var schema = JsonSchema.FromText(envelopeSchemaJson);
         using var doc = JsonDocument.Parse(envelopeJson);

         var result = schema.Evaluate(doc.RootElement, new EvaluationOptions
         {
            OutputFormat = OutputFormat.Hierarchical
         });

         if (!result.IsValid)
         {
            var details = result.Errors; // hierarchical error object
            var detailsJson = JsonSerializer.Serialize(details, JsonOut);

            throw new InvalidOperationException(
               $"Envelope validation failed against the provided JSON Schema.{Environment.NewLine}{detailsJson}");
         }
      }
      catch (JsonException ex)
      {
         throw new InvalidOperationException("Envelope JSON is not valid JSON.", ex);
      }
   }

   /// <summary>
   /// Converts a JSON HRF envelope string back into native HRF text representation.
   /// </summary>
   /// <remarks>
   /// This method accepts either:
   /// - The schema-valid envelope shape: { "messages": [...] }
   /// - A serialized HarmonyEnvelope (which includes HRFVersion), as long as it can be deserialized.
   /// </remarks>
   public static HarmonyEnvelope ConvertEnvelopeJsonToEnvelope(string envelopeJson)
   {
      if (string.IsNullOrWhiteSpace(envelopeJson))
         throw new ArgumentException("Envelope JSON is empty.", nameof(envelopeJson));

      var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
      options.Converters.Add(new JsonStringEnumConverter());

      // Try minimal wrapper first (schema-valid shape)
      try
      {
         var wrapper = JsonSerializer.Deserialize<EnvelopeWrapper>(envelopeJson, options);
         if (wrapper?.Messages is { Count: > 0 })
         {
            return new HarmonyEnvelope { Messages = wrapper.Messages };
         }
      }
      catch
      {
         // fall through to full envelope
      }

      var envelope = JsonSerializer.Deserialize<HarmonyEnvelope>(envelopeJson, options)
         ?? throw new InvalidOperationException("Failed to deserialize envelope JSON.");
      return envelope;
   }

   /// <summary>
   /// Converts a JSON HRF envelope string back into native HRF text representation.
   /// </summary>
   /// <remarks>
   /// This method accepts either:
   /// - The schema-valid envelope shape: { "messages": [...] }
   /// - A serialized HarmonyEnvelope (which includes HRFVersion), as long as it can be deserialized.
   /// </remarks>
   public static string ConvertEnvelopeJsonToHrfText(string envelopeJson)
   {
      if (string.IsNullOrWhiteSpace(envelopeJson))
         throw new ArgumentException("Envelope JSON is empty.", nameof(envelopeJson));

      var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
      options.Converters.Add(new JsonStringEnumConverter());

      // Try minimal wrapper first (schema-valid shape)
      try
      {
         var wrapper = JsonSerializer.Deserialize<EnvelopeWrapper>(envelopeJson, options);
         if (wrapper?.Messages is { Count: > 0 })
         {
            return JsonToFormatConverter.ConvertEnvelopeToHarmonyText(
               new HarmonyEnvelope { Messages = wrapper.Messages });
         }
      }
      catch
      {
         // fall through to full envelope
      }

      var envelope = JsonSerializer.Deserialize<HarmonyEnvelope>(envelopeJson, options)
         ?? throw new InvalidOperationException("Failed to deserialize envelope JSON.");

      return JsonToFormatConverter.ConvertEnvelopeToHarmonyText(envelope);
   }

   private sealed class EnvelopeWrapper
   {
      public System.Collections.Generic.List<HarmonyMessage> Messages { get; set; } = new();
   }

}
