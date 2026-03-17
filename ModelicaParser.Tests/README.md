# ModelicaParser.Tests

This project contains comprehensive unit tests for the ModelicaParser library, with a focus on testing the `ModelicaSyntaxVisitor` class.

## Test Structure

### ModelicaSyntaxVisitorTests

The main test class contains tests organized into the following categories:

#### Variable Declaration Tests
- `SimpleRealVariable_FormatsCorrectly` - Tests basic Real variable declaration
- `RealVariableWithInitialValue_FormatsCorrectly` - Tests variable with initial value
- `RealVariableWithModification_FormatsCorrectly` - Tests variable with modification (e.g., `start=1.0`)
- `ParameterVariable_FormatsCorrectly` - Tests parameter variables
- `MultipleVariables_FormatsCorrectly` - Tests comma-separated variable lists

#### Equation Tests
- `SimpleEquation_FormatsCorrectly` - Tests basic equations
- `DerEquation_FormatsCorrectly` - Tests derivative equations using `der()`
- `ArithmeticEquation_FormatsCorrectly` - Tests equations with arithmetic operations
- `ConnectClause_FormatsCorrectly` - Tests connector connections

#### Expression Tests
- `AdditionExpression_FormatsCorrectly` - Tests addition operations
- `MultiplicationExpression_FormatsCorrectly` - Tests multiplication operations
- `PowerExpression_FormatsCorrectly` - Tests exponentiation (^)
- `UnaryMinusExpression_FormatsCorrectly` - Tests unary minus operator

#### Markup Mode Tests
- `SimpleModel_WithMarkup_FormatsCorrectly` - Tests markup tags for simple models
- `EquationWithOperators_WithMarkup_FormatsCorrectly` - Tests markup for equations with operators
- `VariableWithModification_WithMarkup_FormatsCorrectly` - Tests markup for variable modifications

#### Class Type Tests
- `FunctionDeclaration_FormatsCorrectly` - Tests function declarations
- `BlockDeclaration_FormatsCorrectly` - Tests block declarations

#### Import and Extends Tests
- `ImportStatement_FormatsCorrectly` - Tests import statements
- `ExtendsClause_FormatsCorrectly` - Tests inheritance/extends clauses

#### Algorithm Tests
- `SimpleAlgorithm_FormatsCorrectly` - Tests algorithm sections
- `IfStatement_FormatsCorrectly` - Tests if statements in algorithms

## Running Tests

Run all tests:
```bash
dotnet test
```

Run specific test category:
```bash
dotnet test --filter "FullyQualifiedName~ModelicaSyntaxVisitorTests"
```

Run with detailed output:
```bash
dotnet test --logger "console;verbosity=detailed"
```

## Test Helper Methods

### AssertFormattedLine

The main helper method that:
1. Parses the input Modelica code (wrapped in a model/function/block for valid syntax)
2. Visits the parse tree with ModelicaSyntaxVisitor
3. Removes trailing empty lines from the output
4. Checks a specific line index against the expected formatted output

This approach allows tests to focus on individual Modelica statements while maintaining valid syntax for the parser. The tests wrap statements in minimal model structures (e.g., `model Test <statement> end Test;`) but only verify the formatted output of the specific line being tested.

## Current Test Results

As of the latest run:
- **Total Tests**: 25
- **Passed**: 25 (100%)
- **Failed**: 0 (0%)

## Test Design

Tests are simplified to focus on single lines of Modelica code:
- Input includes minimal wrapping (e.g., `model Test Real x; end Test;`)
- Only the specific line being tested is verified (e.g., `  Real x;`)
- Uses line index parameter to check the correct output line
- Variable declarations check line index 1
- Equations and algorithms check line index 2 (after `equation` or `algorithm` keyword)

## Future Improvements

1. Add more complex test cases:
   - Nested models
   - For loops and while loops
   - When equations
   - Array expressions
   - Function calls with multiple arguments
   - Annotations

2. Add tests for edge cases:
   - Empty models
   - Comments
   - String literals
   - Qualified names (e.g., `Modelica.Blocks.Sources`)

3. Add performance tests for large Modelica files

4. Add tests for error handling and malformed input
