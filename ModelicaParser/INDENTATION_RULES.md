# Modelica Code Formatter - Indentation Rules

This document describes the indentation rules implemented in `ModelicaRenderer.cs` for formatting Modelica source code.

## Core Concepts

### Base Indentation Unit
- **IndentSpaces = 2**: All indentation increments are 2 spaces

### Indentation Mechanisms
1. **`Indent()` / `Dedent()`**: Modify the `_indentLevel` counter, affecting all subsequent lines
2. **`EmitLine()`**: Prepends `_indentLevel * IndentSpaces` to the current line before adding to output
3. **`AddIndentToCurrentLine()`**: Adds `IndentSpaces` (2 spaces) directly to the current line buffer (for continuation indents)
4. **`AddIndentAtLineStart(lineNumber)`**: Adds `IndentSpaces` (2 spaces) to an already-emitted line by index (for public/protected post-processing)

### State Tracking
- **`_indentLevel`**: Current nesting level (incremented/decremented by `Indent()`/`Dedent()`)
- **`_parentUsingMultiLine`**: Indicates if parent visitor has set up multi-line formatting with `Indent()`
- **`_equationContinuationIndent`**: Additional indentation for wrapped equations/statements
- **`_bracketDepth`**: Depth of nested brackets/parentheses/braces (prevents equation wrapping inside nested structures)
- **`_suppressNextIndentation`**: Flag to suppress indentation on the next emitted line (used after multi-line strings)
- **`_noPostIndentLines`**: HashSet of line indices exempt from public/protected post-processing indent
- **`_inAnnotation`**: Flag for annotation context (affects multi-line decision)
- **`_inGraphicsAnnotationLevel`**: Nesting depth within graphics annotations (0 = not in graphics, 1 = Icon/Diagram, 2+ = nested elements)
- **`_inClassAnnotationIcon`**: Flag for Icon at class annotation level
- **`_inDocumentationAnnotation`**: Flag that disables line wrapping inside Documentation annotations
- **`_classAnnotation`**: Flag for class-level annotation context
- **`_inDeclaration`**: Flag affecting wrapping decisions for modifications

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
- **Imports, extends, components**: Indented within class body
- **Public/Protected sections**: Use a post-processing pattern — content is rendered first, then `AddIndentAtLineStart()` is applied to each emitted line. Lines marked in `_noPostIndentLines` (e.g. multi-line string content) are exempt
- **Equation sections**: `equation` keyword at class level, content indented
- **Algorithm sections**: `algorithm` keyword at class level, content indented

### 2. Enumeration Types

```modelica
type Color = enumeration(
  red,
  green,
  blue
);
```

**Rules:**
- When the enumeration has literals, multi-line format is always used
- Opening `(`: followed by `EmitLine()`
- `_indentLevel++` before visiting the enum list
- Each literal separated by `,` followed by `EmitLine()`
- `_indentLevel--` after visiting the list, then `EmitLine()` before closing `)`
- Single-element enumerations also use multi-line format

### 3. Extends Clauses and Inheritance

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
  - Nesting depth ≥ 2 AND ≥ 2 arguments (unless exactly 2 args at graphics level 2), OR
  - In annotation context with ≥ 2 arguments (unless at graphics level 2), OR
  - In class annotation with ≥ 1 argument, OR
  - Parent is multi-line AND ≥ 2 arguments (unless exactly 2 args at graphics level 2), OR
  - At graphics level ≤ 1 AND not in declaration AND > 2 arguments, OR
  - Would exceed max line length
  - **Overridden to false** if Icon has single-line graphics or annotation has single-line Icon

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

### 4. Equation and Algorithm Sections

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
- If LHS > 20 chars AND estimated total line length > max line length:
  - Estimated total = LHS length + 3 (for ` = `) + RHS text length
  - Add space after LHS
  - `EmitLine()` to move to new line
  - `Indent()` for continuation
  - `AddIndentToCurrentLine()` for extra 2 spaces
  - Write `=` operator
  - Visit RHS expression
  - `Dedent()` when done
- **Result**: RHS at 4 spaces (2 base + 2 continuation)

#### Arithmetic Expression Wrapping
```modelica
equation
  long_variable_name = expression1 +
    expression2 +
    expression3;
```

**Rules:**
- Triggered when estimated line length > `_maxLineLength - 3` AND `_bracketDepth == 0` AND in equation/statement context
- `_equationContinuationIndent` is set to 1 when processing equations or simple statements
- On wrap: `EmitLine()`, then `Indent()` × `_equationContinuationIndent`, `AddIndentToCurrentLine()`, write operator, visit next term, `Dedent()` × `_equationContinuationIndent`

### 5. Control Flow Structures

#### If-Then-Else (Equations and Statements)
```modelica
if condition then
  x = y;
elseif condition2 then
  x = z;
else
  x = 0;
end if;
```

**Rules (`VisitIf_equation` / `VisitIf_statement`):**
- `if`/`elseif`/`else` at current level
- `Indent()` after `then`/`else`, `Dedent()` before next clause
- Body content indented by 2 spaces relative to `if`

#### For Loop (Equations and Statements)
```modelica
for i in 1:10 loop
  x[i] = i * 2;
end for;
```

**Rules (`VisitFor_equation` / `VisitFor_statement`):**
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

#### When Clause (Equations and Statements)
```modelica
when condition then
  x = y;
elsewhen condition2 then
  x = z;
end when;
```

**Rules (`VisitWhen_equation` / `VisitWhen_statement`):**
- `when`/`elsewhen` at current level
- `Indent()` after `then`, `Dedent()` before next clause
- Body content indented by 2 spaces

### 6. Component Declarations

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
- Similar multi-line decision logic to `VisitClass_or_inheritence_modification` but without the `wouldExceedLineLength` condition
- Multi-line format for complex modifications
- Arguments indented when multi-line mode active

### 7. Annotations

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
- An empty line (`EmitEmptyLine()`) is inserted before the class annotation
- `_classAnnotation = true` is set when entering, reset when exiting
- Class annotations use multi-line format with ≥ 1 argument
- `Indent()` before visiting the annotation, `Dedent()` after
- `Icon` and `Graphics` nested structures get additional indentation
- Each level of nesting adds 2 spaces

#### External Clause Annotation
```modelica
  external "C"
    annotation(Library="mylib");
```

**Rules:**
- When an `external` clause has an annotation: `EmitLine()`, then `Indent()` before visiting the annotation
- `Dedent()` after the semicolon is written
- `_withAnnotation` flag tracks whether the external annotation was processed

#### Extends Annotation
```modelica
  extends BaseModel(param1=value)
    annotation(Inline=true);
```

**Rules:**
- When extends has an annotation: `EmitLine()`, then `Indent()` before visiting
- `Dedent()` after
- If current line has content, `AddIndentToCurrentLine()` is applied

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
- Simple 2-argument graphics elements at level 2 stay on one line
- Complex graphics use multi-line format
- Graphics annotation level tracking via `_inGraphicsAnnotationLevel`:
  - Level 0: Not in graphics
  - Level 1: Inside Icon or Diagram (array level)
  - Level 2+: Inside graphic element function calls and nested structures
  - Level incremented when entering function arguments or array arguments, decremented when exiting

### 8. Function Arguments and Modifications

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

### 9. Connect Statements

```modelica
equation
  connect(port_a, port_b);
```

**Rules (`VisitConnect_clause`):**
- Written inline with comma and space between the two component references
- No special wrapping logic — always on one line

### 10. Array Expressions

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
- `_bracketDepth` tracking prevents equation-level wrapping inside array literals

### 11. Multi-Line Strings

#### Documentation Strings
```modelica
parameter Real x "This is a very long
description that spans
multiple lines";
```

**Rules (`WriteMultiLineString`):**
- First line: Written to current line (inherits normal indentation)
- Line 0→1 transition: `EmitLine()` with normal indentation (line 0 contains code structure)
- Line 1+ transitions: `EmitLine(ignoreIndentation: true)` — string content lines are NOT indented
- Last line: `_suppressNextIndentation = true` so whatever follows the string (closing quote, paren, etc.) is not indented
- Lines emitted with `ignoreIndentation: true` are added to `_noPostIndentLines` to prevent public/protected post-processing from adding extra indentation
- Code editor rendering wraps each line in `<STRING>` tags; non-editor rendering preserves content as-is

## Special Cases and Edge Conditions

### 1. Line Length Management
- **Maximum line length**: Configurable via `_maxLineLength` (default 100)
- **Wrapping threshold**: Line length check uses plain text (excluding markup)
- **Line length exclusions**: Documentation annotations don't trigger wrapping (`_inDocumentationAnnotation` flag)
- **Empty lines**: `EmitLine()` does not add indentation to empty/whitespace-only lines

### 2. Graphics Annotations
- **2-argument graphics elements**: Stay on one line at level 2 (e.g., `Line(points={{...}}, color={...})`)
- **Icon with single-line graphics**: Entire Icon stays on one line
- **Graphics annotation level**: Tracked to apply different rules at different nesting depths

### 3. Documentation Annotations
- **No wrapping**: Lines in Documentation annotations are not wrapped
- **Preserved formatting**: Multi-line documentation strings preserve original formatting
- **`_inDocumentationAnnotation`** flag is set when visiting a `Documentation` element modification and restored when exiting
- Checked in `IsLineTooLong()` and in multiple wrapping decision points throughout the renderer

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

## Test Coverage

### Current Status
- **485 tests, all passing (100%)**

## Summary

The indentation system handles all Modelica constructs using a combination of:

1. **`Indent()`/`Dedent()` pairs** for standard block indentation (class bodies, sections, control flow)
2. **`AddIndentAtLineStart()` post-processing** for public/protected section content
3. **`AddIndentToCurrentLine()` continuation indents** for single-line wrapping
4. **`_noPostIndentLines` exemptions** to prevent double-indenting multi-line string content
5. **`_suppressNextIndentation`** for edge cases where content follows a multi-line string on the same logical line

The system uses a clear parent-child communication pattern via `_parentUsingMultiLine`, which prevents double-indentation when parent has already called `Indent()`.
