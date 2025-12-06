using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Json.Schema;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

/// <summary>
/// Validates Harmony envelopes & embedded harmony-script objects against 
/// Draft 2020-12 JSON Schemas.
/// </summary>
public static class HarmonySchemaValidator
{
   private static JsonSchema? _envelopeSchema;
   private static JsonSchema? _scriptSchema;

   /// <summary>
   /// Initialize the validator by loading the envelope schema from disk and extracting
   /// the HarmonyScript sub-schema. Call once during app startup.
   /// </summary>
   /// <param name="schemaFolderPath">
   /// Path to the folder containing <c>harmony_envelope_schema.json</c>.
   /// </param>
   public static void Initialize(string schemaFolderPath)
   {
      var path = Path.Combine(schemaFolderPath, "harmony_envelope_schema.json");
      if (!File.Exists(path))
      {
         throw new FileNotFoundException(
            $"Harmony envelope schema file not found at '{path}'. " +
            "Expected 'harmony_envelope_schema.json' in the schema folder.");
      }

      var schemaText = File.ReadAllText(path);

      // Load the full envelope schema
      _envelopeSchema = JsonSchema.FromText(schemaText);

      // Extract the HarmonyScript sub-schema from $defs.HarmonyScript, if present.
      using var doc = JsonDocument.Parse(schemaText);
      if (doc.RootElement.TryGetProperty("$defs", out var defs) &&
          defs.TryGetProperty("HarmonyScript", out var scriptDef))
      {
         _scriptSchema = JsonSchema.FromText(scriptDef.GetRawText());
      }
      else
      {
         // We treat absence of the sub-schema as a configuration error.
         throw new InvalidOperationException(
            "HarmonyScript sub-schema not found in harmony_envelope_schema.json " +
            "at $defs.HarmonyScript.");
      }
   }

   /// <summary>
   /// Validates the raw JSON (string) against the HarmonyEnvelope schema.
   /// Returns a <see cref="HarmonyError"/> on failure or null on success.
   /// </summary>
   /// <param name="json">The JSON string representing the Harmony envelope.</param>
   /// <returns>
   /// A <see cref="HarmonyError"/> describing envelope validation errors, or null if valid.
   /// </returns>
   public static HarmonyError? TryValidateEnvelope(string json)
   {
      if (_envelopeSchema is null)
         throw new InvalidOperationException(
            "HarmonySchemaValidator not initialized. Call Initialize() before validation.");

      using var doc = JsonDocument.Parse(json);
      var result = _envelopeSchema.Evaluate(doc.RootElement, new EvaluationOptions
      {
         OutputFormat = OutputFormat.Hierarchical
      });

      if (!result.IsValid)
      {
         return new HarmonyError
         {
            Code = "HRF_SCHEMA_ENVELOPE_FAILED",
            Message = "Envelope validation failed against the HarmonyEnvelope JSON Schema.",
            Details = result.Errors
         };
      }

      return null;
   }

   /// <summary>
   /// Attempts to validate the extracted harmony-script JsonElement against the HarmonyScript
   /// sub-schema obtained from the loaded envelope schema.
   /// Returns a <see cref="HarmonyError"/> on failure or null on success.
   /// </summary>
   /// <param name="scriptElement">
   /// The JsonElement representing the harmony-script object 
   /// (typically the content of a system message).
   /// </param>
   /// <returns>
   /// A <see cref="HarmonyError"/> describing script validation errors, or null if valid.
   /// </returns>
   public static HarmonyError? TryValidateScript(JsonElement scriptElement)
   {
      if (_scriptSchema is null)
         throw new InvalidOperationException(
            "HarmonySchemaValidator not initialized. HarmonyScript sub-schema is not available.");

      var result = _scriptSchema.Evaluate(scriptElement, new EvaluationOptions
      {
         OutputFormat = OutputFormat.Hierarchical
      });

      if (!result.IsValid)
      {
         return new HarmonyError
         {
            Code = "HRF_SCHEMA_SCRIPT_FAILED",
            Message = "harmony-script validation failed against the HarmonyScript JSON Schema.",
            Details = result.Errors
         };
      }

      return null;
   }

   /// <summary>
   /// Validates the extracted harmony-script JsonElement against the HarmonyScript sub-schema.
   /// This is a throwing convenience wrapper around <see cref="TryValidateScript"/>.
   /// </summary>
   /// <param name="scriptElement">The JsonElement representing the harmony-script object.</param>
   /// <exception cref="InvalidOperationException">
   /// Thrown if the HarmonySchemaValidator is not initialized or
   /// if the script fails schema validation.
   /// </exception>
   public static void ValidateScript(JsonElement scriptElement)
   {
      var error = TryValidateScript(scriptElement);
      if (error != null)
      {
         // Make the failure visible to callers who rely on exceptions
         var detailsJson = JsonSerializer.Serialize(error.Details,
            new JsonSerializerOptions { WriteIndented = true });

         throw new InvalidOperationException(
            $"{error.Message}{Environment.NewLine}{detailsJson}");
      }
   }

   /// <summary>
   /// Validates a JSON envelope file against the expected schema using the specified schema file 
   /// path.
   /// </summary>
   /// <remarks>
   /// This helper is preserved for backwards compatibility with existing code that validates
   /// JSON script/envelope files on disk by name.
   /// It initializes the schema validator (if not already initialized) and reads the JSON envelope
   /// from the <c>Scripts</c> folder before performing validation.
   /// 
   /// Make sure the schema file name is "harmony_envelope_schema.json" and the file is in the
   /// "Schemas/" folder, and also make sure the given script file is in the "Scripts/" folder.
   /// </remarks>
   /// <param name="jsonScriptFileName">
   /// The relative path to the envelope JSON file within the 'Scripts' directory. 
   /// Cannot be null or empty.
   /// </param>
   public static void Validate(string jsonScriptFileName)
   {
      if (string.IsNullOrWhiteSpace(jsonScriptFileName))
         throw new ArgumentException(
            "jsonScriptFileName must be specified.", nameof(jsonScriptFileName));

      // Ensure the schemas are initialized; callers may have already done this, but this
      // keeps the helper self-contained for test utilities.
      if (_envelopeSchema is null || _scriptSchema is null)
      {
         Initialize("Schemas");
      }

      // Read the envelope JSON from disk
      string path = Path.Combine("Scripts", jsonScriptFileName);
      if (!File.Exists(path))
      {
         throw new FileNotFoundException(
            $"Envelope JSON file not found at '{path}'. " +
            "Ensure the file exists in the 'Scripts' folder.");
      }

      string json = File.ReadAllText(path);

      var error = TryValidateEnvelope(json);
      if (error != null)
      {
         var detailsJson = JsonSerializer.Serialize(error.Details,
            new JsonSerializerOptions { WriteIndented = true });

         throw new InvalidOperationException(
            $"{error.Message}{Environment.NewLine}{detailsJson}");
      }
   }

}

