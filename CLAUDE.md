# MCPProject

Two separate C#/.NET codebases live in this repo:

- `SV/MicroservicesServer` — ASP.NET microservices backend. Solution: `SV/MicroservicesServer/MicroservicesServer.sln`
- `ClientTest` — Unity client. Solution: `ClientTest/ClientTest.sln`

## C# codebase exploration: prioritize vs-mcp-server

When reading, navigating, or modifying C# code in either codebase, prefer the Visual Studio MCP server (`mcp__vs-mcp-server__*` tools) over Grep/Glob/full-file Read:

1. Call `LoadSolution` on the relevant `.sln` above if no solution is loaded yet (or the wrong one is loaded).
2. Use `FindSymbols` / `FindSymbolDefinition` / `FindSymbolUsages` / `GetMethodCallers` / `GetMethodCalls` / `GetInheritance` / `GetSolutionTree` / `GetDocumentOutline` to navigate semantically instead of grepping for class/method names.
3. Use `GetDiagnostics` / `ErrorListGet` to check compiler errors instead of guessing from build output.
4. Use `RenameSymbol` for renames so all references update correctly.
5. Fall back to Grep/Read only for non-code files (JSON/config/docs) or when vs-mcp-server is unavailable.

The `vs-mcp-efficient-usage` skill has the full tool reference and workflow patterns for this — consult it for any C#/.NET task involving symbol lookup, call graphs, type hierarchies, renaming, diagnostics, or debugging.
