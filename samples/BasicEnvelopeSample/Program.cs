using Harmony.Format.Core;


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

// -------------------------------------------------------------------------------------------------
// Basic Envelope Sample App

class Program
{

   public static async Task ProcessScriptAsync(string? scriptFileName)
   {
      Console.WriteLine("=== Harmony.Format.Core — Basic Envelope Sample ===");

      if (String.IsNullOrWhiteSpace(scriptFileName))
      {
         Console.WriteLine("Usage: dotnet run <path-to.hrf>");
         return;
      }

      var path = "Scripts/" + scriptFileName;
      if (!File.Exists(path))
      {
         Console.WriteLine($"File not found: {path}");
         return;
      }

      // Load HRF text
      string hrfText = File.ReadAllText(path);
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

      // 3. Schema Validator Initialization (if needed)
      // Load schemas from the Core project’s Schemas folder
      var schemaFolder = Path.Combine(AppContext.BaseDirectory, "Schemas");
      if (Directory.Exists(schemaFolder))
      {
         Console.WriteLine("\n--- Initializing Schema Validator ---");
         HarmonySchemaValidator.Initialize(schemaFolder);
      }

      // 4. Validate Envelope (schema + semantic rules)
      Console.WriteLine("\n--- Validating Envelope ---");
      var error = envelope.ValidateForHrf();
      if (error != null)
      {
         Console.WriteLine($"ERROR: {error.Code} — {error.Message}");
         return;
      }
      Console.WriteLine("Envelope is valid!");

      // 5. Execute Envelope
      Console.WriteLine("\n--- Executing Envelope ---");

      var chatService = new StubLanguageModelChatService();
      var toolService = new StubToolExecutionService();
      var executor = new HarmonyExecutor(chatService, toolService);

      var input = new Dictionary<string, object?>(); // provide runtime input here
      var result = await executor.ExecuteAsync(envelope, input);

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

   static async Task Main(string[] args)
   {
      foreach (var arg in args)
      {
         Console.WriteLine($"Argument: {arg}");
         await ProcessScriptAsync(arg);
      }
   }

}
