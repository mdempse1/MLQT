/*
Copyright (c) 2025-2026 M Dempsey Ltd.
Licensed under the MIT License. See LICENSE file in the project root.

Modified the original ANTLR Modelica grammar to better support use in a syntax highlighter.
Updated to Modelica 3.6 specification with extensions to accept the same variations as Dymola.

Original grammar by Tom Everett, licensed under the BSD License (below).
*/
/*
[The "BSD licence"]
Copyright (c) 2012 Tom Everett
All rights reserved.
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
1. Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright
notice, this list of conditions and the following disclaimer in the
documentation and/or other materials provided with the distribution.
3. The name of the author may not be used to endorse or promote products
derived from this software without specific prior written permission.
THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

grammar modelica;

//Added support for C-style comments before within statement - used in ExternData
// A file has at most ONE within clause (Modelica spec 13.2.2.2). Accepting a repeated clause here
// let a file that had been written with a duplicated 'within' parse clean, so nothing downstream
// ever reported it and reformatting preserved the damage instead of flagging it.
// Comments may also follow the clause and the file's last class (B430): `within P; // note` and
// `end M; // trailer` were syntax errors. None of this text is in any class's source span, so a
// writer that rebuilds the file from stored source keeps it only because FileLevelText carries it
// (B445) - which it does for these two positions and the header, and not for a comment BETWEEN two
// top-level classes, which Format All's one-file-per-class restructure would silently drop. That
// position stays a syntax error, visible, rather than becoming a quiet deletion.
// Each run can belong to one loop only: the trailing comments are inside the group that starts with
// a class, so a run at the top or after the clause cannot be claimed by them. That keeps every choice
// one token ahead - a comment continues its run, anything else ends it - so a long run is never
// rescanned per comment (B235). A trailing loop outside the group would let a comment after the
// clause belong to either, and deciding that means scanning to the end of the run for each one.
stored_definition
    : c_comment* ('within' (name)? ';' c_comment*)?
      (('final')? class_definition ';' (('final')? class_definition ';')* c_comment*)? EOF
    ;

class_definition
    : ('encapsulated')? class_prefixes class_specifier
    ;

class_specifier
    : long_class_specifier
    | short_class_specifier
    | der_class_specifier
    ;

class_prefixes
    : ('partial')? (
        'class'
        | 'model'
        | ('operator')? 'record'
        | 'block'
        | ('expandable')? 'connector'
        | 'type'
        | 'package'
        | (('pure' | 'impure'))? ('operator')? 'function'
        | 'operator'
    )
    ;

long_class_specifier
    : IDENT string_comment composition 'end' IDENT
    | 'extends' IDENT (class_modification)? string_comment composition 'end' IDENT
    ;

//In Modelica 3.6, Changed name to type_specifier
short_class_specifier
    : IDENT '=' base_prefix type_specifier (array_subscripts)? (class_modification)? comment
    | IDENT '=' 'enumeration' '(' (c_comment* enum_list c_comment* | ':')? ')' comment
    ;

//In Modelica 3.6, Changed name to type_specifier
der_class_specifier
    : IDENT '=' 'der' '(' type_specifier ',' IDENT (',' IDENT)* ')' comment
    ;

//Modelica 3.6 should only allow input/output here but keeping broader support for now
base_prefix
    : type_prefix
    ;

// Comments may follow a ',' (B432) or come before one (B431): one literal a line with a note after
// each is the natural way to write a long enumeration. Comments after the '(' and before the ')'
// are the enclosing short_class_specifier's. This is the one list that takes a comment before its
// separator - see argument_list for why the others do not, and why an enumeration can: a run
// before a ',' belongs to the loop only when the ',' follows it, so it is decided once, but it makes
// every turn of the loop a prediction rather than a one-token switch, and enumerations are few.
enum_list
    : enumeration_literal (c_comment* ',' c_comment* enumeration_literal)*
    ;

enumeration_literal
    : IDENT comment
    ;

//Added support for a more c-style comment locations
// Comments may come before the leading class annotation (B432), on B409's terms: they belong to it
// only when the annotation follows, so the choice is made once and the loop ends on 'annotation',
// one token ahead. Otherwise they fall to element_list exactly as before. A class with nothing but
// comments and an annotation was already ambiguous between the leading and the trailing annotation;
// ANTLR takes the leading one, and the renderer writes both in the same place.
composition
    : (c_comment* annotation ';')?
      element_list (
        'public' element_list
        | 'protected' element_list
        | equation_section
        | algorithm_section
      )* 
      ('external' (language_specification)? (external_function_call)? (annotation)? ';')? 
      c_comment*
      ( annotation ';' )?
      final_comment
    ;

final_comment
    : c_comment*
    ;

language_specification
    : STRING
    ;

external_function_call
    : (component_reference '=')? IDENT '(' (expression_list)? ')'
    ;

//Added support for tracking c-style comments among element definitions
element_list
    : (c_comment+ | element ';')*
    ;

// Comments before 'constrainedby' (B432) belong to the element only when 'constrainedby' follows
// them; the ';' that ends an element cannot start with a comment, so the choice is made once.
element
    : import_clause
    | extends_clause
    | ('redeclare')? ('final')? ('inner')? ('outer')? 
      (
        ( class_definition 
        | component_clause)
        | 'replaceable' (class_definition | component_clause) (c_comment* constraining_clause comment)?
      )
    ;

import_clause
    : 'import' (IDENT '=' name | name '.*' | name '.{' import_list '}' | name) comment
    ;

import_list
    : IDENT (',' IDENT)*
    ;

//Changed in Modelica 3.6 with class_or_inheritence_modification instead of class_modification
//In Modelica 3.6, Changed name to type_specifier
// Comments may come before the annotation (B432), only when the annotation follows - as in comment.
extends_clause
    : 'extends' type_specifier (class_or_inheritence_modification)? (c_comment* annotation)?
    ;

//In Modelica 3.6, Changed name to type_specifier
constraining_clause
    : 'constrainedby' type_specifier (class_modification)?
    ;

component_clause
    : type_prefix type_specifier (array_subscripts)? component_list
    ;

type_prefix
    : ('flow' | 'stream')? ('discrete' | 'parameter' | 'constant')? ('input' | 'output')?
    ;

//In Modelica 3.6 added support for leading "."
type_specifier
    : ('.')? name
    ;

component_list
    : component_declaration (',' component_declaration)*
    ;

component_declaration
    : declaration (condition_attribute)? comment
    ;

condition_attribute
    : 'if' expression
    ;

declaration
    : IDENT (array_subscripts)? (modification)?
    ;

//Modified in Modelica 3.6 to use modification-expression instead of expression
modification
    : class_modification ('=' modification_expression)?
    | '=' modification_expression
    | ':=' modification_expression
    ;

//New in Modelica 3.6
modification_expression
    : expression
    | 'break'
    ;

//New in Modelica 3.6
// Comments after the '(' and before the ')' (B431), as in class_modification.
class_or_inheritence_modification
    : '(' c_comment* (argument_or_inheritence_list c_comment*)? ')'
    ;

//New in Modelica 3.6
// Comments after a ',' (B431), as in argument_list.
argument_or_inheritence_list
    : (argument | inheritence_modification) (',' c_comment* (argument | inheritence_modification))*
    ;

//New in Modelica 3.6
inheritence_modification
    : 'break' (connect_clause | IDENT)
    ;

// Comments may come after the '(' and before the ')' (B431): `Real x(start=1, // why` then
// `fixed=true);`, or a note on the last argument. The closing run is taken only after a list, so a
// modification holding nothing but comments is the opening run's alone: with a run allowed both
// before and after an optional list, an empty list leaves two loops competing for the same
// comments, and the parser rescans the run at every comment to tell them apart (B235).
class_modification
    : '(' c_comment* (argument_list c_comment*)? ')'
    ;

// Comments after a ',' (B431). Every bracketed list - modifications, function arguments, arrays,
// matrix rows, enumerations - takes this one shape: a run after a separator ends on the next item,
// and a run after the last item is the enclosing rule's closing run, which ends on the bracket.
// Every one of those loops ends one token ahead, so no run is rescanned per comment (B235), and
// whose a run after an item is - a description's, an annotation's, a constrainedby's or the closing
// run - is decided once, by the token after it.
//
// A comment BEFORE a ',' is refused, except in an enumeration. It could be taken on the same terms,
// but then a COMMENT could either continue a list or end it, and no loop over a list could be
// decided by one token any more: every ',' and every closing bracket in every file becomes a
// prediction. Measured over MSL and Buildings, that was 2.15M more predictions (+61%) and parse
// time 5-8% slower, for a position (`a=1 // why` then `, b=2` on the next line) that the usual
// `a=1, // why` makes unnecessary. Without it the count rose by 189.
argument_list
    : argument (',' c_comment* argument)*
    ;

argument
    : element_modification_or_replaceable
    | element_redeclaration
    ;

element_modification_or_replaceable
    : ('each')? ('final')? (element_modification | element_replaceable)
    ;

element_modification
    : name (modification)? string_comment
    ;

element_redeclaration
    : 'redeclare' ('each')? ('final')? (
        (short_class_definition | component_clause1)
        | element_replaceable
    )
    ;

// Comments before 'constrainedby' (B431), on the terms element has them (B432): only when
// 'constrainedby' follows, so the run is otherwise the enclosing list's.
element_replaceable
    : 'replaceable' (short_class_definition | component_clause1) (c_comment* constraining_clause)?
    ;

component_clause1
    : type_prefix type_specifier component_declaration1
    ;

component_declaration1
    : declaration comment
    ;

short_class_definition
    : class_prefixes short_class_specifier
    ;

equation_section
    : ('initial')? 'equation' equation_or_comment*
    ;

algorithm_section
    : ('initial')? 'algorithm' statement_or_comment*
    ;

//In Modelica 3.6 changed name to component_reference
equation
    : (
        simple_expression '=' expression
        | if_equation
        | for_equation
        | connect_clause
        | when_equation
        | component_reference function_call_args
    ) comment
    ;

//Added support for der(x) := expr in statements
//In Modelica 3.6, removed c_comment to be consistent with equation
statement
    : (
        component_reference (':=' expression | function_call_args)
        | 'der' function_call_args (':=' expression | function_call_args)
        | '(' output_expression_list ')' ':=' component_reference function_call_args
        | 'break'
        | 'return'
        | if_statement
        | for_statement
        | while_statement
        | when_statement
    ) comment
    ;

//Separated out elseif_equation and else_equation for clarity in syntax highlighting
if_equation
    : 'if' expression 'then' equation_or_comment* elseif_equation* else_equation? 'end' 'if'
    ;

elseif_equation
    : 'elseif' expression 'then' equation_or_comment*
    ;

else_equation
    : 'else' equation_or_comment*
    ;

//Separated out elseif_statement and else_statement for clarity in syntax highlighting
if_statement
    : 'if' expression 'then' statement_or_comment* elseif_statement* else_statement? 'end' 'if'
    ;

elseif_statement
    : 'elseif' expression 'then' statement_or_comment*
    ;

else_statement
    : 'else' statement_or_comment*
    ;

for_equation
    : 'for' for_indices 'loop' equation_or_comment* 'end' 'for'
    ;

for_statement
    : 'for' for_indices 'loop' statement_or_comment* 'end' 'for'
    ;

for_indices
    : for_index (',' for_index)*
    ;

for_index
    : IDENT ('in' expression)?
    ;

while_statement
    : 'while' expression 'loop' statement_or_comment* 'end' 'while'
    ;

//Separated out elsewhen_equation for clarity in syntax highlighting
when_equation
    : 'when' expression 'then' equation_or_comment* elsewhen_equation* 'end' 'when'
    ;

elsewhen_equation
    : 'elsewhen' expression 'then' equation_or_comment*
    ;

//Separated out elsewhen_statement for clarity in syntax highlighting
when_statement
    : 'when' expression 'then' statement_or_comment* elsewhen_statement* 'end' 'when'
    ;

elsewhen_statement
    : 'elsewhen' expression 'then' statement_or_comment*
    ;

//connect_equation in Modelica 3.6
connect_clause
    : 'connect' '(' component_reference ',' component_reference ')'
    ;

//Added to support tracking comments within equations and statements
// Comments may come between an equation or a statement and its ';' (B432). The equation's own
// comment rule takes them only when an annotation follows, so these end on the ';', one token
// ahead. An equation_or_comment is a comment-only node when it has no equation, not when it has
// comments - read equation(), never c_comment(), to tell which.
equation_or_comment
    : (c_comment+ | (equation c_comment* ';'))
    ;

//Added to support tracking comments within equations and statements
statement_or_comment
    : (c_comment+ | (statement c_comment* ';'))
    ;
    
//Separated out elseif_expression for clarity in syntax highlighting
expression
    : simple_expression
    | 'if' expression 'then' expression elseif_expression* 'else' expression
    ;

elseif_expression
    : 'elseif' expression 'then' expression
    ;

simple_expression
    : logical_expression (':' logical_expression (':' logical_expression)?)?
    ;

logical_expression
    : logical_term ('or' logical_term)*
    ;

logical_term
    : logical_factor ('and' logical_factor)*
    ;

logical_factor
    : ('not')? relation
    ;

relation
    : arithmetic_expression (rel_op arithmetic_expression)?
    ;

rel_op
    : '<'
    | '<='
    | '>'
    | '>='
    | '=='
    | '<>'
    ;

arithmetic_expression
    : (add_op)? term (add_op term)*
    ;

add_op
    : '+'
    | '-'
    | '.+'
    | '.-'
    ;

term
    : factor (mul_op factor)*
    ;

mul_op
    : '*'
    | '/'
    | '.*'
    | './'
    ;

factor
    : primary (('^' | '.^') primary)?
    ;

//In Modelica 3.6 replaced name with component_reference
//In Modelica 3.6 added pure as a function_call_args prefix option
//Added optional array_arguments after output_expression_list to align with Dymola
primary
    : UNSIGNED_NUMBER
    | STRING
    | 'false'
    | 'true'
    | (component_reference | 'der' | 'initial' | 'pure') function_call_args
    | component_reference
    | '(' output_expression_list ')' ( '[' array_arguments ']' )?
    // Comments after the opening bracket, after a row's ';' and before the closing bracket (B431) -
    // a data table with a note on each row is the real case. The shape is argument_list's; the
    // rows' own ',' are expression_list's.
    | '[' c_comment* expression_list (';' c_comment* expression_list)* c_comment* ']'
    | '{' c_comment* array_arguments c_comment* '}'
    | 'end'
    ;

//In Modelica 3.6, removed the leading "." option
name
    : IDENT ('.' IDENT)*
    ;

component_reference
    : ('.')? IDENT (array_subscripts)? ('.' IDENT (array_subscripts)?)*
    ;

// Comments after the '(' and before the ')' (B431), on class_modification's terms.
function_call_args
    : '(' c_comment* (function_arguments c_comment*)? ')'
    ;

//Changed in Modelica 3.6
//Changed from right-recursive to iterative to avoid stack overflow on large argument lists
// Comments after each ',' (B431), in argument_list's shape. Whether a ',' starts a positional or a
// named argument is decided by what follows it, as it always was; a run after the ',' is scanned in
// that decision once, and the loop over it ends one token ahead.
function_arguments
    : expression (',' c_comment* function_argument)* (',' c_comment* named_arguments)? ('for' for_indices)?
    | function_partial_application (',' c_comment* function_argument)* (',' c_comment* named_arguments)?
    | named_arguments
    ;

//New in Modelica 3.6
//Changed from right-recursive to iterative to avoid stack overflow on large arrays
// Comments after each ',' (B431); those after the '{' and before the '}' are primary's.
array_arguments
    : expression (',' c_comment* expression)* ('for' for_indices)?
    ;

//Changed from right-recursive to iterative to avoid stack overflow
// Comments after each ',' (B431).
named_arguments
    : named_argument (',' c_comment* named_argument)*
    ;

named_argument
    : IDENT '=' function_argument
    ;

//In Modelica 3.6, introduces function_partial_application
function_argument
    : function_partial_application
    | expression
    ;

//New in Modelica 3.6
function_partial_application
    : 'function' type_specifier '(' (named_arguments)?')'
    ;

output_expression_list
    : (expression)? (',' (expression)?)*
    ;

// Comments after each ',' (B431) - within a matrix row, or between an external call's arguments.
expression_list
    : expression (',' c_comment* expression)*
    ;

array_subscripts
    : '[' subscript_ (',' subscript_)* ']'
    ;

subscript_
    : ':'
    | expression
    ;

//description in Modelica 3.6
// Comments may also come before the annotation (B409), on the same terms as before a description:
// they belong here only when an annotation follows, so the choice is taken once and the loop ends on
// the 'annotation' keyword, one token ahead. Nothing that can follow this rule starts with a comment.
comment
    : string_comment (c_comment* annotation)?
    ;

// Comments may come before a description string (B409): `function f // note` then the string on the
// next line. Modelica allows a comment anywhere, and without this one here the description was a
// syntax error and error recovery detached every later class in the file from its package.
// The comments belong to this rule only when a STRING follows them - otherwise the rule is empty and
// they fall to whatever comes next (a class body's element_list) exactly as before. That keeps the
// choice unambiguous: entering is decided once, by scanning the run to the token after it, and the
// loop inside ends on the STRING, one token ahead, so a long run is not rescanned per comment (B235).
// A string_comment therefore still has text if and only if it has a STRING.
string_comment
    : (c_comment* STRING ('+' STRING)*)?
    ;

annotation
    : 'annotation' class_modification
    ;

c_comment
    : COMMENT
    | LINE_COMMENT
    ;

IDENT
    : NONDIGIT (DIGIT | NONDIGIT)*
    | Q_IDENT
    ;

fragment Q_IDENT
    : '\'' (Q_CHAR | S_ESCAPE) (Q_CHAR | S_ESCAPE | WS)* '\''
    ;

fragment S_CHAR
    : ~ ["\\]
    ;

fragment NONDIGIT
    : '_'
    | 'a' .. 'z'
    | 'A' .. 'Z'
    ;

STRING
    : '"' (S_CHAR | S_ESCAPE)* '"'
    ;

fragment Q_CHAR
    : NONDIGIT
    | DIGIT
    | '!'
    | '#'
    | '$'
    | '%'
    | '&'
    | '('
    | ')'
    | '*'
    | '+'
    | ','
    | '-'
    | '.'
    | '/'
    | ':'
    | ';'
    | '<'
    | '>'
    | '='
    | '?'
    | '@'
    | '['
    | ']'
    | '^'
    | '{'
    | '}'
    | '|'
    | '~'
    | '"'
    | ' '
    ;

// The Modelica spec only defines a specific set of escape sequences, but Dymola
// and every other tool in the wild treat any `\<char>` inside a string as literal
// text. Enforcing the spec here causes the lexer to reject real-world files that
// contain HTML with typos (e.g. `<\p>`), Windows file paths (`C:\Users\...`), or
// JavaScript snippets inside annotation strings — files that Dymola loads without
// complaint. Accept any character after `\` so these files parse.
fragment S_ESCAPE
    : '\\' .
    ;

fragment DIGIT
    : '0' .. '9'
    ;

fragment UNSIGNED_INTEGER
    : DIGIT (DIGIT)*
    ;

//UNSIGNED_REAL in Modelica 3.6
UNSIGNED_NUMBER
    : UNSIGNED_INTEGER ('.' (UNSIGNED_INTEGER)?)? (('e' | 'E') ('+' | '-')? UNSIGNED_INTEGER)?
    | '.' UNSIGNED_INTEGER (('e' | 'E') ('+' | '-')? UNSIGNED_INTEGER)?
    ;

WS
    : [ \r\n\t]+ -> channel (HIDDEN)
    ;

COMMENT
    : '/*' .*? '*/'
    ;

LINE_COMMENT
    : '//' ~[\r\n]*
    ;