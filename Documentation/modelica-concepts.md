# Modelica Concepts for MLQT Users

This guide provides a brief introduction to the Modelica concepts that are relevant when using MLQT. It is not a Modelica tutorial — it covers just enough to understand what MLQT shows you and why.

If you are already familiar with Modelica, you can skip this guide.

## What Is Modelica?

Modelica is an open-standard language for modeling physical systems — mechanical, electrical, thermal, hydraulic, control systems, and more. Models written in Modelica describe the equations governing a system's behavior, and simulation tools (like Dymola and OpenModelica) solve those equations to predict how the system behaves over time.

MLQT helps you manage and maintain Modelica libraries stored in version control, applying quality checks and formatting rules to keep the code consistent.

## Class Types

Modelica has several kinds of classes, each with a specific purpose. MLQT shows these in the library tree and uses them in dependency analysis and style checking.

| Class Type | Purpose | MLQT Context |
|-----------|---------|--------------|
| **model** | The primary building block. Describes a physical component with equations. | Most common node in the library tree. |
| **block** | A model with defined input/output causality (like a control block). | Appears in the tree same as models. |
| **package** | A container for organizing other classes. Like a folder/namespace. | Shown as expandable tree nodes. |
| **function** | A computational function (no equations, uses algorithms). | Leaf nodes in the tree. |
| **record** | A data container with named fields but no equations. | Leaf nodes in the tree. |
| **connector** | Defines an interface for connecting components (e.g., electrical pin, fluid port). | Leaf nodes. Important for connection analysis. |
| **type** | A type alias or constrained type (e.g., `type Voltage = Real(unit="V")`). | Leaf nodes. |
| **operator record** | A record with operator overloading. | Less common. Leaf nodes. |

## Package Structure

Modelica libraries are organized as a hierarchy of packages. MLQT's library tree directly mirrors this structure.

### The Root Package

Every Modelica library has a root package — the top-level container that holds everything else. For example, the Modelica Standard Library's root package is simply called `Modelica`.

### Nested Packages

Packages can contain other packages, creating a hierarchy:

```
Modelica                          (root package)
  Mechanics                       (sub-package)
    Rotational                    (sub-sub-package)
      Components                  (sub-sub-sub-package)
        Inertia                   (model)
        Spring                    (model)
```

The fully qualified name of a class includes the entire package path, separated by dots:
`Modelica.Mechanics.Rotational.Components.Inertia`

### File Organization

Modelica code can be stored in files in two ways:

**Single-file packages (`package.mo`):**
A package and all its contents in a single file. The file must be named `package.mo` and placed in a directory matching the package name.

**Directory packages:**
Each class in a separate `.mo` file within a directory. The directory contains:
- `package.mo` — The package definition itself
- `package.order` — A text file listing the child elements and their order
- Individual `.mo` files — One per class

Most large libraries use the directory structure. MLQT handles both transparently.

### `package.order` Files

The `package.order` file lists the children of a package in the order they should appear. This is important because:
- It defines the display order in tools like Dymola and MLQT
- It determines which classes belong to the package
- MLQT monitors these files for changes (see [File Monitoring](file-monitoring.md))

Example `package.order`:
```
Components
Sources
Sensors
Interfaces
Types
```

## Sections Within a Class

Modelica classes are organized into sections. Several of MLQT's [formatting rules](settings-reference.md#formatting-rules) control the ordering of these sections.

### Public and Protected Sections

- **public** — Declarations visible to users of the class (the default)
- **protected** — Declarations hidden from users, only accessible within the class

The formatting rule **"One of each section"** merges multiple public sections into one and multiple protected sections into one.

### Composition Elements

Within public and protected sections, you find:

- **import statements** — Bring other packages into scope (e.g., `import Modelica.SIunits.*`)
- **extends clauses** — Inherit from another class (e.g., `extends Modelica.Icons.Package`)
- **component declarations** — Variables and parameters (e.g., `parameter Real mass = 1.0`)
- **nested class definitions** — Classes defined inside another class

The formatting rules **"Imports first"** and **"Components before classes"** control the ordering of these elements.

### Equation and Algorithm Sections

- **equation** — Declares equations that the solver must satisfy
- **algorithm** — Declares sequential computation steps (like a programming language)
- **initial equation** / **initial algorithm** — Equations or algorithms that apply only at the start of simulation

The formatting rules **"Initial equation first/last"** control where the initial sections appear relative to the main equation/algorithm section.

The style rule **"Don't mix equation and algorithm"** flags classes that have both types of sections.

## Annotations

Annotations are metadata attached to Modelica classes and elements. They don't affect the model's behavior but provide information for tools. MLQT's style checking rules reference several annotation types.

### Common Annotations

| Annotation | Purpose | MLQT Style Rule |
|-----------|---------|----------------|
| `Documentation(info="...")` | HTML documentation for the class | "Every class must have documentation info" |
| `Documentation(revisions="...")` | Change history for the class | "Every class must have documentation revisions" |
| `Icon(...)` | Graphical representation for diagram editors | "Every class must have an icon" |
| `Dialog(loadSelector(...))` | File picker configuration for parameters | Tracked in External Resources |
| `Include`, `Library`, `IncludeDirectory`, etc. | External C/Fortran code references | Tracked in External Resources |

### Show/Hide Annotations

In MLQT's Code Review tab, the **Annotations toggle** (bookmark icon) lets you hide annotations from the code view. Since annotations can be very verbose (especially Documentation and Icon annotations), hiding them helps you focus on the functional model code.

## Description Strings

Modelica classes, parameters, and variables can have **description strings** — short text that describes their purpose:

```modelica
model HeatTransfer "Simple heat transfer model"
  parameter Real k = 1.0 "Thermal conductivity";
  Real T "Temperature";
equation
  ...
end HeatTransfer;
```

The strings `"Simple heat transfer model"`, `"Thermal conductivity"`, and `"Temperature"` are descriptions. They appear in:
- Tool browsers and documentation
- Parameter dialogs in simulation tools
- MLQT's library tree tooltips

MLQT's style rules **"Every class must have a description"**, **"Every public parameter must have a description"**, and **"Every public constant must have a description"** check for the presence of these strings.

## Naming Conventions

Modelica has established naming conventions:
- **Classes** (models, packages, etc.): Start with **uppercase** (`HeatTransfer`, `FluidPort`)
- **Variables and parameters**: Start with **lowercase** (`temperature`, `flowRate`, `nPorts`)
- **Constants**: Start with **lowercase** (`pi`, `g_n`)

MLQT's **"Check that the naming convention is followed"** style rule validates these conventions.

## Dependencies

When one Modelica class references another, it creates a dependency. MLQT's [Dependency Analysis](dependency-analysis.md) tab visualizes these relationships. Dependencies arise from:

- **extends** — Class inheritance (`extends BaseModel`)
- **Component types** — Declaring a variable of another class's type (`Resistor R1`)
- **Function calls** — Using functions from other packages
- **Type references** — Using types from other packages in equations or algorithms

Understanding dependencies is crucial for assessing the impact of changes — if you modify a base class, all classes that extend it may be affected.

## Connections

In Modelica, components are connected through their connectors:

```modelica
connect(resistor.p, ground.p);
```

The style rule **"Do not mix connections and equations"** checks that `connect()` statements are kept separate from mathematical equations, improving readability.
