# **Harmony.Format**
### *A .NET implementation of the Harmony Format (HRF-style) message and scripting system*

---

## **Overview**

**Harmony.Format** is an open-source, framework-agnostic .NET library that implements a structured message and scripting format inspired by the **Harmony Response Format (HRF)** conventions used by modern LLMs. It provides:

- A formal **envelope** and **message** model  
- A **native HRF text parser** and **JSON conversion layer**  
- Full **JSON Schema** + **semantic validation**  
- A deterministic **workflow engine** for executing HarmonyScripts  
- Optional **Semantic Kernel integration** via a clean adapter layer  

It is designed to give enterprises and developers a **predictable**, **safe**, and **extensible** orchestration layer for LLM-powered workflows.

> **Important:** This project does *not* claim full adherence to a finalized OpenAI HRF specification, as no such formal standard has been published.  
> Instead, Harmony.Format follows **high-fidelity interpretations of observed HRF patterns** and models exhibited by LLMs.

### **HRF and Semantic Kernel better together**

Use Harmony Response Format (HRF) in a Semantic Kernel project when you want a more reliable, model-native envelope for what the model is doing—especially around tool calling and structured multi-part outputs. SK is great at orchestration, plugins, and prompt templating (Handlebars), but those don’t guarantee the model’s output will be consistently parseable. HRF gives you a predictable structure for “this is the user-visible answer” vs “this is a tool call” vs “this is tool output,” which reduces brittle regex/heuristics and makes error handling, auditing, and retries much cleaner in production.

It’s particularly useful if you’re running or integrating gpt-oss models (which are trained to speak HRF) or you need portability across inference backends: you can keep SK’s planners/filters/tool pipeline while standardizing the I/O contract at the model boundary. Then you can still use SK Handlebars for what it’s best at—rendering the final response (including tables) from structured tool results—while HRF ensures those results and calls are represented consistently end-to-end.

---

## **Project Goals**

- Provide an **independent, open, clean-room** implementation of Harmony-style messaging  
- Be compatible with:
  - Semantic Kernel  
  - OpenAI/Azure OpenAI clients  
  - Local models and custom backends  
- Deliver a **strict** executor that prevents malformed workflows  
- Allow enterprises to reliably:
  - Validate agent outputs  
  - Execute tool-oriented workflows  
  - Maintain secure and auditable orchestration flows  
- Serve as an **extensible foundation** for future Harmony-related features

---

# **Packages**

| Project | Description |
|--------|-------------|
| **Harmony.Format.Core** | Core envelope model, parser, validators, execution engine, converters |
| **Harmony.Format.SemanticKernel** | Semantic Kernel integration (`ChatCompletionService`, `ToolExecutionService`) |
| **Harmony.Format.Cli** *(future)* | CLI utilities for converting, validating, and executing HRF scripts |
| **Harmony.Format.Extensions** *(future)* | Experimental/advanced script features |

---

# **Features**

### Harmony Envelope Model  
- Messages with roles, channels, content types  
- Termination markers (`end`, `call`, `return`)  
- HRF-style native tokenization (`<|start|>`, `<|message|>`, etc.)

### Native HRF Text → JSON Conversion  
- Parse HRF text into envelopes  
- Convert envelope back into HRF  
- JSON-preserving round-trip conversion  
- Supports `contentType=json` and `contentType=harmony-script`

### HarmonyScript Workflow Engine  
- `vars` initialization  
- Step types:
  - `extract-input`
  - `tool-call`
  - `assistant-message`
  - `if` / `then` / `else`
  - `halt`
- Expression resolution (`$input.x`, `$vars.y`)
- Final-answer orchestration (`analysis` vs `final` channels)

### Validation  
- JSON Schema validation  
- Deep semantic validation:
  - Roles, channels, termination rules  
  - Script step correctness  
  - Duplicate keys  
  - Expression validity  
  - Tool-call structure

### Backend-Agnostic Execution  
The executor depends only on two interfaces:

```csharp
ILanguageModelChatService   // provide LLM chat responses
IToolExecutionService       // execute tool calls
```

This allows you to plug in:

- Semantic Kernel  
- OpenAI client  
- Custom LLMs  
- Local models  
- Your own tool plugin system

---

# **Architecture**

```
Harmony.Format.Core
 │
 ├── Envelope Model (HarmonyEnvelope, HarmonyMessage)
 ├── HarmonyScript (steps, vars, converters)
 ├── Parsing (native HRF → JSON → native HRF)
 ├── Validation (schema + semantic rules)
 ├── Execution (HarmonyExecutor)
 │      ├── ILanguageModelChatService
 │      └── IToolExecutionService
 │
 └── Conversion (HrfToJsonConverter, JsonToHrfConverter)

Harmony.Format.SemanticKernel
 ├── SkChatCompletionService (ILanguageModelChatService)
 └── SkToolExecutionService  (IToolExecutionService)
```

---

# **Installation**

### When published to NuGet (planned):

```sh
dotnet add package Harmony.Format.Core
dotnet add package Harmony.Format.SemanticKernel
```

For now, clone the repo:

```sh
git clone https://github.com/<you>/harmony-format-core.git
```

---

# **Usage Examples**

---

## **1. Parse and Validate an HRF Envelope**

```csharp
string hrfText = File.ReadAllText("sample.hrf");

var envelope = HrfToJsonConverter.ConvertHrfTextToEnvelope(hrfText);

// Optional: initialize schemas
HarmonySchemaValidator.Initialize("Schemas/");

var error = envelope.ValidateForHrf();
if (error != null)
{
    Console.WriteLine($"Invalid: {error.Code}: {error.Message}");
}
```

---

## **2. Execute with Stub Services**

```csharp
var chat = new StubLanguageModelChatService();
var tools = new StubToolExecutionService();

var executor = new HarmonyExecutor(chat, tools);
var input = new Dictionary<string, object?>();

var result = await executor.ExecuteAsync(envelope, input);

Console.WriteLine(result.Message);
```

---

## **3. Integrate with Semantic Kernel**

```csharp
var chat = new SkChatCompletionService(kernel, chatService);
var tools = new SkToolExecutionService(kernel);

var executor = new HarmonyExecutor(chat, tools);
var result = await executor.ExecuteAsync(envelope, new Dictionary<string, object?>());
```

---

# **Sample Native HRF Script**

```text
<|start|>
system
<|constrain|>
harmony-script
<|message|>
{
  "vars": {
    "location": "$input.location",
    "forecast": null
  },
  "steps": [
    {
      "type": "tool-call",
      "recipient": "weather.get_forecast",
      "channel": "commentary",
      "args": {
        "location": "$vars.location"
      },
      "save_as": "forecast"
    },
    {
      "type": "assistant-message",
      "channel": "final",
      "content_template": "Forecast for {{location}}: {{forecast}}"
    }
  ]
}
<|end|>
```

---

# **Roadmap**

### **Near-term**
- [ ] Publish NuGet packages  
- [ ] CLI tooling (`harmony` command)  
- [ ] More sample workflows  
- [ ] Full TypeScript port (`Harmony-format-js`)

### **Mid-term**
- [ ] Extended HarmonyScript features: parallel steps, retry steps  
- [ ] Built-in tool registry  
- [ ] Visual designer for HarmonyScript

### **Long-term**
- [ ] HRF standardization proposals  
- [ ] First-class multi-agent orchestration  
- [ ] Monitoring and observability (OpenTelemetry integration)  

---

# **Contribution**

Contributions are welcome!

- Open issues for bugs, enhancements, or questions  
- Submit PRs (feature branches preferred)  
- Follow the code style defined in `.editorconfig`  
- Add/update unit tests for all new features  

---

# **License**

This project is open-source and available under the **MIT License**.

---

# **Acknowledgments**

This project is inspired by the evolving Harmony Response Format concepts introduced by modern LLM systems.  
While not an official implementation of any vendor’s standard, it strives for **high fidelity with observed HRF behavior**, delivering a safe and extensible foundation for enterprise LLM workflows.
