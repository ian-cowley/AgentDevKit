# Contributing to AgentDevKit

First off, thank you for considering contributing to AgentDevKit! It's people like you that make AgentDevKit such a great tool for the C# community.

## Where do I go from here?

If you've noticed a bug or have a feature request, please open a new issue. Before opening an issue, please search the existing issues to ensure it hasn't already been reported.

## Pull Requests

### 1. Fork the Repository
Fork the project to your own GitHub account and clone it to your local machine.

### 2. Create a Branch
Create a branch for your changes:
```bash
git checkout -b feature/your-feature-name
# or
git checkout -b bugfix/your-bug-name
```

### 3. Make Your Changes
Implement your feature or fix. Please follow the existing coding style:
- Use **C# Coding Conventions** (e.g., PascalCase for methods/classes, camelCase for local variables).
- Ensure your code is properly documented with XML comments.
- Keep components focused and modular.

### 4. Test Your Changes
Run the existing tests and add new tests if you're introducing new functionality. Use the `AgentDevKit.Adk.Sample` project to verify your changes in a real-world scenario.

### 5. Submit a Pull Request
Push your branch to your fork and open a Pull Request against the `main` branch of the original repository. Please provide a clear description of what your PR does and why it's needed.

## Style Guide

- **Namespaces**: All code should reside under the `AgentDevKit.Adk` root namespace.
- **Tools**: When creating new tools, implement the `ITool` interface and provide clear JSON schemas for arguments.
- **Logging**: Use `Microsoft.Extensions.Logging` for all diagnostic output.
- **Observability**: Integrate with the existing `Telemetry` class where appropriate.

## Code of Conduct

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

---

Happy coding!
