# Contributing to Harmonia.Format

Thank you for your interest in contributing to **Harmonia.Format**!  
This project aims to provide a clean, open, and extensible implementation of the Harmony-style message format, scripting model, and execution engine. Contributions from the community are essential to achieving this goal.

We welcome issues, bug fixes, new features, documentation improvements, and discussions.

---

# Code of Conduct

By participating in this project, you agree to uphold a respectful, constructive, and inclusive environment.

If you experience or witness unacceptable behavior, please open an issue or contact the project maintainers.

---

# Repository Structure

```
/src
   Harmonia.Format.Core/            # Core envelope, parser, validation, executor
   Harmonia.Format.SemanticKernel/  # Semantic Kernel integration layer

/tests
   Harmonia.Format.Core.Tests/      # Core tests
   Harmonia.Format.SemanticKernel.Tests/

/samples
   BasicEnvelopeSample/
   SemanticKernelSample/

/docs
   HRF_Implementation_Profile.md
   ...other documentation files
```

The `Core` project contains the main format, parser, validation, conversion, and execution logic.  
**It must not depend on Semantic Kernel, OpenAI SDKs, or any external framework.**

The `SemanticKernel` project provides SK adapters that implement:

- `ILanguageModelChatService`
- `IToolExecutionService`

---

# How to Contribute

## 1. Open an Issue First (Recommended)

Before submitting a PR, please open an issue if your contribution:

- Adds a major feature  
- Changes public APIs  
- Modifies execution semantics  
- Alters the HarmonyScript model  
- Impacts repository structure  

This ensures design alignment before coding begins.

---

## 2. Fork & Clone

```sh
git clone https://github.com/<your-fork>/harmony-format-core.git
cd harmony-format-core
```

---

## 3. Create a Feature Branch

```sh
git checkout -b feature/my-new-feature
```

Examples:

- `feature/native-hrf-enhance-parser`
- `bugfix/tool-call-arg-null-handling`
- `docs/update-readme-samples`

---

## 4. Follow Project Code Style

### General Rules

- Use .NET 8 features where appropriate  
- Use file-scoped namespaces  
- Use `readonly` fields when possible  
- Use `async/await` and cancellation tokens  
- Avoid abbreviations in public APIs  
- Prefer composition over inheritance  
- Avoid "magic strings"  

### Namespaces

- Use: `Harmonia.Format.Core` and `Harmonia.Format.SemanticKernel`
- Do **not** introduce nested namespaces unless necessary

### Naming Philosophy

- No acronyms in public-facing type names  
- Prefer descriptive names:  
  - `ChatConversation`  
  - `ILanguageModelChatService`  
  - `IToolExecutionService`  
- Avoid previous HRF-coupled names:
  - `IHrfToolRouter`  
  - `SkHrfChatService`

---

# Testing Guidelines

All new features **must include tests**.

### Run tests:

```sh
dotnet test
```

### Coverage areas:

- HRF → JSON and JSON → HRF conversion  
- HRF text parsing  
- Script validation & step conversion  
- Workflow execution  
- Semantic validation  
- SK integration adapters (where applicable)

---

# Submitting a Pull Request

1. Ensure the solution builds:

```sh
dotnet build
```

2. Ensure tests pass:

```sh
dotnet test
```

3. Commit & push:

```sh
git commit -am "Add <feature> or Fix <issue>"
git push origin feature/my-new-feature
```

4. Open a Pull Request (PR) on GitHub.

### PR Requirements

- Clear title and description  
- Link to an issue (when applicable)  
- Document API changes  
- Include tests  
- Avoid breaking changes unless approved  

Reviewers may request improvements in:

- Coverage  
- Naming  
- Structure  
- Semantics  

---

# Documentation Contributions

We welcome PRs that improve:

- README  
- HRF_Implementation_Profile  
- Samples  
- API docs  

Documentation is a first-class contribution.

---

# Building the Project

From the repository root:

```sh
dotnet restore
dotnet build
dotnet test
```

Run samples:

```sh
cd samples/BasicEnvelopeSample
dotnet run sample.hrf
```

---

# Roadmap & Vision

See the project README for the latest roadmap.  
If you'd like to contribute to a feature, open an issue or PR proposal!

---

# Thank You

Your time and contributions help move Harmonia.Format forward as a stable, extensible, vendor-agnostic foundation for structured LLM orchestration.  
Every contribution—code, docs, ideas—helps the ecosystem grow.

