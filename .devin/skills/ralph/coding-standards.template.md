# Coding Standards

This file defines the coding standards for this project. It is used by RALPH's review agent to evaluate code quality after each implementation.

Edit each section for your project — replace the placeholder comments with real rules specific to your codebase.

---

## Language & Framework

<!-- What language and runtime version does this project use? -->
<!-- Example: C# / .NET Framework 4.8, TypeScript / Node 20, Python 3.11 -->

Language:
Runtime:

<!-- Any official style guides or conventions followed? -->
<!-- Example: Microsoft C# coding conventions, Google TypeScript style guide -->

Style guide:

---

## Build & Test Commands

<!-- The exact commands to build and run the full test suite. -->
<!-- The review agent will run these after making any changes. -->

Build:
Test:

<!-- Any additional check commands (type-check, lint, etc.)? -->

Check:

---

## Naming Conventions

<!-- Rules for naming classes, methods, variables, files, and other identifiers. -->
<!-- Be specific — "use PascalCase for classes" is more useful than "use standard conventions". -->

<!-- Example:
- Classes: PascalCase (OrderProcessor, not orderProcessor)
- Methods: PascalCase (GetById, not getById)
- Private fields: _camelCase (_userId, not userId or UserId)
- Local variables: camelCase (orderCount, not OrderCount)
- Test methods: MethodName_Scenario_ExpectedResult
-->

---

## Test Conventions

<!-- What testing framework is used? -->

Framework:

<!-- How are tests structured? (Arrange/Act/Assert, Given/When/Then, etc.) -->

Structure:

<!-- Where do test files live relative to source files? -->

Location:

<!-- What makes a good test in this project? What makes a bad test? -->
<!-- Example:
- Tests verify observable behaviour through public APIs, not implementation details
- One logical assertion per test
- Tests should not depend on each other or on execution order
- No mocking of internal collaborators
-->

---

## Code Quality Rules

<!-- Project-specific rules beyond general good practice. -->
<!-- These are the rules the review agent will actively enforce. -->

<!-- Example:
- No business logic in UI code (ViewModels call services; services own logic)
- No static state; pass dependencies through constructors
- No comments that describe what the code does — only why, if non-obvious
- All public methods must have XML doc comments
-->

---

## What Reviewers Look For

<!-- The top issues that commonly appear in this codebase. -->
<!-- These are the first things the review agent will check. -->

<!-- Example:
- Forgetting to dispose IDisposable resources
- Direct database calls from ViewModel layer
- Test methods that test multiple behaviours in one assertion
- Magic numbers and strings that should be named constants
-->
