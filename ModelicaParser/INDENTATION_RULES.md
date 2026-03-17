# Modelica Code Formatter - Indentation Rules

This document describes the indentation rules implemented in `ModelicaSyntaxVisitor.cs` for formatting Modelica source code.

## Core Concepts

### Base Indentation Unit
- **INDENT_SPACES = 2**: All indentation increments are 2 spaces

### Indentation Mechanisms
1. **`Indent()` / `Dedent()`**: Modify the `_indentLevel` counter, affecting all subsequent lines
2. **`EmitLine()`**: Prepends `_indentLevel * INDENT_SPACES` to the current line before adding to output
3. **`AddIndentToCurrentLine()`**: Adds `INDENT_SPACES` (2 spaces) directly to the current line buffer (for continuation indents)

### State Tracking
- **`_indentLevel`**: Current nesting level (incremented/decremented by `Indent()`/`Dedent()`)
- **`_parentUsingMultiLine`**: Indicates if parent visitor has set up multi-line formatting with `Indent()`
- **`_equationContinuationIndent`**: Additional indentation for wrapped equations/statements

## Indentation Rules by Context

### 1. Class and Model Definitions

#### Long Class Specifier
```modelica
model MyModel "Description"
  // Body content at indent level 1 (2 spaces)
  Real x;
end MyModel;
```

**Rules:**
- Class body: `Indent()` after class declaration, `Dedent()` before `end`
- Result: Body content at 2 spaces

#### Composition Elements
- **Imports, extends, components**: Indented within class body (level 1 = 2 spaces)
- **Equation sections**: `equation` keyword at class level, content indented
- **Algorithm sections**: `algorithm` keyword at class level, content indented

### 2. Extends Clauses and Inheritance

#### Simple Extends (No Arguments)
```modelica
model MyModel
  extends BaseModel;
end MyModel;
```

**Rules:**
- No additional indentation for extends without arguments

#### Extends with Arguments - Multi-Line Format

```modelica
model MyModel
  extends BaseModel(
    param1=value1,
    param2=value2,
    param3=value3
  );
end MyModel;
```

**Rules (`VisitClass_or_inheritence_modification`):**
- **Multi-line mode triggered when**:
  - More than 5 arguments, OR
  - Nesting depth ≥ 2 AND ≥ 2 arguments, OR
  - In annotation context with ≥ 2 arguments, OR
  - In class annotation with ≥ 1 argument, OR
  - Parent is multi-line AND ≥ 2 arguments, OR
  - More than 2 arguments (non-graphics, non-declaration context)

- **When multi-line**:
  1. Opening `(` followed by `EmitLine()`
  2. `Indent()` called (increases level)
  3. Arguments visited
  4. `EmitLine()` then `Dedent()` before closing `)`
  5. `_parentUsingMultiLine = useMultiLineParens` (child knows parent called `Indent()`)

- **Argument indentation**: 4 spaces (level 2) because parent called `Indent()` once

#### Argument Wrapping Logic (`VisitArgument_or_inheritence_list`)

**Wrapping conditions:**
- Parent is using multi-line mode (`_parentUsingMultiLine`), OR
- More than 2 arguments (`argCount > 2`), OR
- Line starts with `)`, OR
- Line would be too long

**When wrapping:**
- If wrapping due to line length AND not in multi-line parent:
  - Call `Indent()` before wrapping
  - Call `AddIndentToCurrentLine()` after `EmitLine()`
  - Call `Dedent()` after visiting argument
  - **Result**: Continuation indent (extra 2 spaces)

- If wrapping due to `argCount > 2` OR `_parentUsingMultiLine`:
  - Parent already called `Indent()`
  - Just `EmitLine()` and visit argument
  - **Result**: Normal indent level (no extra continuation indent)

#### Constraining Clause
```modelica
model MyModel
  extends Base(
    redeclare replaceable package Medium = Water
      constrainedby PartialMedium
  );
end MyModel;
```

**Rules (`VisitConstraining_clause`):**
- `EmitLine()` to move to new line
- `AddIndentToCurrentLine()` adds 2 spaces for continuation
- **Expected**: 6 spaces (4 base + 2 continuation)
- **Current issue**: Only produces 4 spaces (continuation indent not working correctly)

### 3. Equation and Algorithm Sections

#### Equation Section
```modelica
equation
  x = y + z;
  a = b * c;
```

**Rules (`VisitEquation_section`):**
- `equation` keyword at class level
- `Indent()` after `equation`, `Dedent()` at end of section
- Each equation at level 1 (2 spaces)

#### Algorithm Section
```modelica
algorithm
  x := y + z;
  a := b * c;
```

**Rules (`VisitAlgorithm_section`):**
- `algorithm` keyword at class level
- `Indent()` after `algorithm`, `Dedent()` at end of section
- Each statement at level 1 (2 spaces)

#### Equation Wrapping
```modelica
equation
  veryLongVariableName
    = someFunction(arg1, arg2, arg3);
```

**Rules (`VisitEquation`):**
- If LHS > 20 chars AND total line > max length:
  - Add space after LHS
  - `EmitLine()` to move to new line
  - `Indent()` for continuation
  - `AddIndentToCurrentLine()` for extra 2 spaces
  - Write `=` operator
  - Visit RHS expression
  - `Dedent()` when done
- **Result**: RHS at 4 spaces (2 base + 2 continuation)

### 4. Control Flow Structures

#### If-Then-Else (Equations)
```modelica
if condition then
  x = y;
elseif condition2 then
  x = z;
else
  x = 0;
end if;
```

**Rules (`VisitIf_equation`):**
- `if`/`elseif`/`else` at current level
- `Indent()` after `then`/`else`, `Dedent()` before next clause
- Body content indented by 2 spaces relative to `if`

#### For Loop (Equations)
```modelica
for i in 1:10 loop
  x[i] = i * 2;
end for;
```

**Rules (`VisitFor_equation`):**
- `for` keyword at current level
- `Indent()` after `loop`, `Dedent()` before `end for`
- Loop body indented by 2 spaces

#### While Loop (Statements)
```modelica
while condition loop
  x := x + 1;
end while;
```

**Rules (`VisitWhile_statement`):**
- `while` keyword at current level
- `Indent()` after `loop`, `Dedent()` before `end while`
- Loop body indented by 2 spaces

#### When Clause (Equations)
```modelica
when condition then
  x = y;
elsewhen condition2 then
  x = z;
end when;
```

**Rules (`VisitWhen_equation`):**
- `when`/`elsewhen` at current level
- `Indent()` after `then`, `Dedent()` before next clause
- Body content indented by 2 spaces

### 5. Component Declarations

#### Simple Declaration
```modelica
model MyModel
  Real x;
  parameter Real y = 5;
end MyModel;
```

**Rules:**
- Component declarations at class body level (2 spaces)

#### Declaration with Modification
```modelica
model MyModel
  Component comp(
    param1=value1,
    param2=value2
  );
end MyModel;
```

**Rules (`VisitClass_modification`):**
- Similar to extends clause rules
- Multi-line format for complex modifications
- Arguments indented when multi-line mode active

### 6. Annotations

#### Class Annotation
```modelica
model MyModel
  Real x;
  annotation(
    Icon(
      graphics={
        Rectangle(extent={{-100, 100}, {100, -100}})
      }
    )
  );
end MyModel;
```

**Rules:**
- Class annotations use multi-line format with ≥ 1 argument
- `Icon` and `Graphics` nested structures get additional indentation
- Each level of nesting adds 2 spaces

#### Graphics Elements
```modelica
annotation(
  Icon(
    graphics={
      Line(points={{0, 0}, {100, 100}})
    }
  )
)
```

**Rules:**
- Simple 2-argument graphics elements (like `Line` with 2 points) stay on one line
- Complex graphics use multi-line format
- Graphics annotation level tracking via `_inGraphicsAnnotationLevel`

### 7. Function Arguments and Modifications

#### Argument Lists
```modelica
result = myFunction(
  arg1,
  arg2,
  arg3
);
```

**Rules (`VisitArgument_list`):**
- Similar logic to `VisitArgument_or_inheritence_list`
- Wrap if: parent multi-line, >2 args, line starts with `)`, or line too long
- **When NOT in parent multi-line mode**: Add `AddIndentToCurrentLine()` for continuation

#### Named Arguments
```modelica
result = myFunction(
  name1=value1,
  name2=value2
);
```

**Rules (`VisitNamed_arguments`):**
- Same wrapping logic as regular arguments
- Continuation indent when wrapping for line length

### 8. Array Expressions

#### Array Constructor
```modelica
matrix = {
  {1, 2, 3},
  {4, 5, 6},
  {7, 8, 9}
};
```

**Rules (`VisitArray_arguments`):**
- Multi-line format for complex arrays
- Each row indented consistently
- Nested arrays get additional indentation

### 9. Multi-Line Strings

#### Documentation Strings
```modelica
parameter Real x "This is a very long
description that spans
multiple lines";
```

**Rules (`WriteMultiLineString`):**
- First line: Keep on same line as declaration
- Second line: Emit first line WITH indentation, second line WITHOUT indentation
- Middle lines: Emit WITHOUT indentation
- Last line: Emit previous line WITHOUT indentation, suppress indentation for current line

## Special Cases and Edge Conditions

### 1. Line Length Management
- **Maximum line length**: Configurable via `_maxLineLength` (default 100)
- **Wrapping threshold**: Line length check uses plain text (excluding markup)
- **Line length exclusions**: Documentation annotations don't trigger wrapping

### 2. Graphics Annotations
- **2-argument graphics elements**: Stay on one line (e.g., `Line(points={{...}}, color={...})`)
- **Icon with single-line graphics**: Entire Icon stays on one line
- **Graphics annotation level**: Tracked to apply different rules at different nesting depths

### 3. Documentation Annotations
- **No wrapping**: Lines in Documentation annotations are not wrapped
- **Preserved formatting**: Multi-line documentation strings preserve original formatting

### 4. Declaration Context
- **`_inDeclaration` flag**: Affects wrapping decisions for modifications
- **Reset on multi-line**: Set to false when entering multi-line parentheses mode

## Parent-Child Communication

### `_parentUsingMultiLine` Flag
**Purpose:** Tells child visitors whether parent called `Indent()` for multi-line formatting

**Set by:**
- `VisitClass_or_inheritence_modification`: Set to `useMultiLineParens`
- `VisitClass_modification`: Set to `oneArgumentPerLine`

**Used by:**
- `VisitArgument_or_inheritence_list`: Don't add continuation indent if parent is multi-line
- `VisitArgument_list`: Don't add continuation indent if parent is multi-line
- `VisitNamed_arguments`: Don't add continuation indent if parent is multi-line

**Rationale:** When parent calls `Indent()`, child arguments automatically get correct indentation from `EmitLine()`. Adding `AddIndentToCurrentLine()` would double-indent.

## Known Issues and Limitations

### 1. Constraining Clause Indentation
**Issue:** `constrainedby` clause only gets 4 spaces instead of expected 6 spaces (4 base + 2 continuation)

**Expected:**
```modelica
extends Base(
  redeclare package Medium = Water
    constrainedby PartialMedium  // Should be 6 spaces
);
```

**Actual:**
```modelica
extends Base(
  redeclare package Medium = Water
  constrainedby PartialMedium  // Only 4 spaces
);
```

**Root cause:** `AddIndentToCurrentLine()` in `VisitConstraining_clause` not producing expected output. The continuation indent is being lost when the line is eventually emitted, possibly due to indent level changes between when the line is buffered and when it's emitted.

### 2. Complexity of Multi-Line Logic
**Issue:** The decision logic for when to use multi-line formatting is complex and distributed across multiple conditions.

**Example from `VisitClass_or_inheritence_modification`:**
```csharp
bool useMultiLineParens = !isIconWithSingleLineGraphics && !isAnnotationWithSingleLineIcon && (
    numArguments > 5 ||
    (maxNestingDepth >= 2 && numArguments >= 2 && ...) ||
    (_inAnnotation && numArguments >= 2 && ...) ||
    (isInClassAnnotation && numArguments >= 1) ||
    (_inGraphicsAnnotationLevel <= 1 && !_inDeclaration && numArguments > 2) ||
    (_parentUsingMultiLine && numArguments >= 2 && ...));
```

**Potential simplification:** Consider extracting multi-line decision logic into separate methods with clear names.

## Recommendations for Simplification

### 1. Consolidate Wrapping Logic
**Current:** `VisitArgument_or_inheritence_list`, `VisitArgument_list`, and `VisitNamed_arguments` have similar but slightly different wrapping logic.

**Suggestion:** Extract common wrapping logic into a shared helper method:
```csharp
private void WrapArgumentIfNeeded(
    bool shouldWrap,
    bool needsWrapForLength,
    Action visitArgument)
{
    if (shouldWrap)
    {
        bool needsExtraIndent = needsWrapForLength && !_parentUsingMultiLine;
        if (needsExtraIndent)
            Indent();

        EmitLine();
        if (needsExtraIndent)
            AddIndentToCurrentLine();

        visitArgument();

        if (needsExtraIndent)
            Dedent();
    }
    else
    {
        Space();
        visitArgument();
    }
}
```

### 2. Clarify Multi-Line Conditions
**Current:** Boolean logic for `useMultiLineParens` is complex and hard to understand.

**Suggestion:** Break into named intermediate variables:
```csharp
bool hasManyArguments = numArguments > 5;
bool hasDeeplyNestedStructure = maxNestingDepth >= 2 && numArguments >= 2;
bool isComplexAnnotation = _inAnnotation && numArguments >= 2 && _inGraphicsAnnotationLevel != 2;
bool isClassAnnotation = isInClassAnnotation && numArguments >= 1;
bool hasModerateComplexity = !_inDeclaration && numArguments > 2;
bool parentRequiresMultiLine = _parentUsingMultiLine && numArguments >= 2;

bool useMultiLineParens = !isIconWithSingleLineGraphics &&
                         !isAnnotationWithSingleLineIcon &&
                         (hasManyArguments || hasDeeplyNestedStructure ||
                          isComplexAnnotation || isClassAnnotation ||
                          hasModerateComplexity || parentRequiresMultiLine);
```

### 3. Document Indent Level Invariants
**Suggestion:** Add assertions or comments documenting expected indent levels at key points:
```csharp
// At this point, indent level should be N because parent called Indent() M times
Debug.Assert(_indentLevel == expectedLevel, $"Indent level mismatch: expected {expectedLevel}, got {_indentLevel}");
```

### 4. Unify Continuation Indent Strategy
**Current:** Sometimes use `Indent()/Dedent()` pairs, sometimes use `AddIndentToCurrentLine()`.

**Consideration:** Should continuation indent always use one mechanism?
- **`Indent()/Dedent()`**: Affects all lines until `Dedent()` is called
- **`AddIndentToCurrentLine()`**: Affects only the current line buffer

**Recommendation:** Use `Indent()/Dedent()` for multi-line blocks, `AddIndentToCurrentLine()` only for single-line continuations. This makes it clearer when indentation affects multiple lines vs. one line.

### 5. Separate Graphics Annotation Rules
**Current:** Graphics annotation rules are mixed with general argument formatting rules.

**Suggestion:** Extract graphics-specific formatting into separate methods:
```csharp
private bool ShouldUseMultiLineForGraphics(int numArguments, int nestingDepth)
{
    // Graphics-specific multi-line logic
}

private bool ShouldUseMultiLineForGeneral(int numArguments, int nestingDepth)
{
    // General multi-line logic
}
```

## Test Coverage

### Passing Tests (349/356 = 98.0%)
- Class definitions and long class specifiers
- Extends clauses with arguments (fixed in recent changes)
- Equation and algorithm sections
- If/for/while/when control structures
- Component declarations and modifications
- Most annotation formatting
- Array expressions
- Function arguments and named arguments

### Failing Tests (7/356)
- **DebugConstrainedBy**: `constrainedby` clause indentation
- **EquilibriumDrumBoiler_FormatsCorrectly**: Complex extends with `constrainedby`
- **ReplaceableModelConstrainingClause_FormatsCorrectly**: Replaceable with `constrainedby`
- **ReplaceableModelConstrainingClauseModifier_FormatsCorrectly**: Replaceable with modifier and `constrainedby`
- **ReplaceableModelConstrainingClauseDescription_FormatsCorrectly**: Replaceable with description and `constrainedby`
- **ReplaceableModelConstrainingClauseDescriptionAnnotation_FormatsCorrectly**: Replaceable with description, annotation, and `constrainedby`
- **ReplaceableModelAll_FormatsCorrectly**: Replaceable with all features including `constrainedby`

All failing tests relate to the same underlying issue: `constrainedby` clause indentation.

## Summary

The indentation system is generally well-structured and handles most Modelica constructs correctly. The main areas for improvement are:

1. **Fix `constrainedby` indentation**: This is the only failing category
2. **Simplify multi-line decision logic**: Make it easier to understand when multi-line format is used
3. **Consolidate wrapping logic**: Reduce duplication between similar visitors
4. **Document invariants**: Make expected indent levels explicit at key points
5. **Consider separating concerns**: Graphics annotations vs. general formatting

The system uses a clear parent-child communication pattern via `_parentUsingMultiLine`, which prevents double-indentation when parent has already called `Indent()`. This is a good design that should be preserved.
