using Harmony.Format.Core;
using static System.Net.Mime.MediaTypeNames;


// -------------------------------------------------------------------------------------------------
// Simple stub chat + tool services so HarmonyExecutor can run

public sealed class StubLanguageModelChatService : ILanguageModelChatService
{
   public Task<string> GetAssistantReplyAsync(
      ChatConversation history, CancellationToken ct = default)
   {
      // Simple: return the last user message reversed or echoed
      var last = history.Messages.LastOrDefault();
      var reply = last.Content is null ? "Hello!" : $"[Echo] {last.Content}";
      return Task.FromResult(reply);
   }
}

public sealed class StubToolExecutionService : IToolExecutionService
{
   public Task<object?> InvokeToolAsync(
      string name, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
   {
      // Just return a summary for demonstration
      var summary = new
      {
         tool = name,
         arguments = args,
         message = "This is a stub tool response."
      };
      return Task.FromResult<object?>(summary);
   }
}

public sealed class HarmonyHelper
{

   public static string GetFileExtension(string fileName)
   {
      if (string.IsNullOrWhiteSpace(fileName))
      {
         throw new ArgumentException("Filename cannot be null or empty.", nameof(fileName));
      }

      // Path.GetExtension returns the extension including the leading dot (e.g., ".txt")
      string extension = Path.GetExtension(fileName);

      // If you prefer without the dot, trim it
      return string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.');
   }

   public static bool IsJsonFile(string fileName)
   {
      var extension = GetFileExtension(fileName);
      return extension.ToLower() == "json";
   }

   public static string GetFileText(string scriptFileName)
   {
      var path = "Scripts/" + scriptFileName;
      if (!File.Exists(path))
      {
         throw new FileNotFoundException("File not found.", path);
      }
      return File.ReadAllText(path);
   }

   public static bool ValidateEnvelope(HarmonyEnvelope envelope)
   {
      // Schema Validator Initialization (if needed)
      // Load schemas from the Core project’s Schemas folder
      var schemaFolder = Path.Combine(AppContext.BaseDirectory, "Schemas");
      if (Directory.Exists(schemaFolder))
      {
         Console.WriteLine("\n--- Initializing Schema Validator ---");
         HarmonySchemaValidator.Initialize(schemaFolder);
      }

      // Validate Envelope (schema + semantic rules)
      Console.WriteLine("\n--- Validating Envelope ---");
      var error = envelope.ValidateForHrf();
      if (error != null)
      {
         Console.WriteLine($"ERROR: {error.Code} — {error.Message}");
         return false;
      }
      Console.WriteLine("Envelope is valid!");
      return true;
   }

   public static HarmonyEnvelope ConvertHrfTextToEnvelope(string hrfText)
   {
      // Load HRF text
      Console.WriteLine("\n--- Loaded HRF Text ---");
      Console.WriteLine(hrfText);

      // 1. Convert HRF → JSON
      Console.WriteLine("\n--- Converting HRF to JSON ---");
      string json = FormatToJsonConverter.ConvertHarmonyTextToEnvelopeJson(hrfText);
      Console.WriteLine(json);

      // 2. Convert JSON → Envelope object
      var envelope = FormatToJsonConverter.ConvertHrfTextToEnvelope(hrfText);
      Console.WriteLine("\n--- Envelope Loaded ---");
      Console.WriteLine($"Messages: {envelope.Messages.Count}");

      return envelope;
   }

   public static HarmonyEnvelope ConvertJsonToEnvelope(string jsonText)
   {
      // Load JSON text
      Console.WriteLine("\n--- Loaded JSON Text ---");
      Console.WriteLine(jsonText);

      // Convert JSON → Envelope object
      var envelope = HarmonyConverter.ConvertEnvelopeJsonToEnvelope(jsonText);
      Console.WriteLine("\n--- Envelope Loaded ---");
      Console.WriteLine($"Messages: {envelope.Messages.Count}");

      return envelope;
   }

   public static async Task ExecuteEnvelope(HarmonyEnvelope envelope)
   {
      // Execute Envelope
      Console.WriteLine("\n--- Executing Envelope ---");

      var chatService = new StubLanguageModelChatService();
      var toolService = new StubToolExecutionService();
      var executor = new HarmonyExecutor(chatService, toolService);

      var input = new Dictionary<string, object?>(); // provide runtime input here

      HarmonyExecutionResult result;
      try
      {
         result = await executor.ExecuteAsync(envelope, input);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"\n=== Execution Exception ===\n{ex}");
         return;
      }

      // 6. Show Results
      Console.WriteLine("\n=== Execution Result ===");

      if (result.IsError)
      {
         Console.WriteLine($"ERROR: {result.Error.Code} — {result.Error.Message}");
      }
      else
      {
         Console.WriteLine(result.FinalText);
      }
   }

}

// -------------------------------------------------------------------------------------------------
// Basic Envelope Sample App

class Program
{
   public static async Task ProcessScriptAsync(string? scriptFileName)
   {
      Console.WriteLine("=== Harmony.Format.Core — Basic Envelope Sample ===");

      if (String.IsNullOrWhiteSpace(scriptFileName))
      {
         Console.WriteLine("Usage: dotnet run <path-to.hrf|.json>");
         return;
      }

      var isJson = HarmonyHelper.IsJsonFile(scriptFileName ?? "");

      string text = HarmonyHelper.GetFileText(scriptFileName);

      HarmonyEnvelope? envelope = null;
      if (isJson)
      {
         // If JSON, convert to Envelope
         envelope = HarmonyHelper.ConvertJsonToEnvelope(text);
      }
      else
      {
         // If HRF, convert to Envelope
         envelope = HarmonyHelper.ConvertHrfTextToEnvelope(text);
      }

      // Validate Envelope
      if (!HarmonyHelper.ValidateEnvelope(envelope))
      {
         return;
      }

      // Execute Envelope
      await HarmonyHelper.ExecuteEnvelope(envelope);
   }

   static async Task Main(string[] args)
   {
      foreach (var arg in args)
      {
         Console.WriteLine($"Argument: {arg}");
         await ProcessScriptAsync(arg);
      }
   }

}
