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
stored_definition
    : c_comment* ('within' (name)? ';')* (('final')? class_definition ';')* EOF
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
    | IDENT '=' 'enumeration' '(' ((enum_list)? | ':') ')' comment
    ;

//In Modelica 3.6, Changed name to type_specifier
der_class_specifier
    : IDENT '=' 'der' '(' type_specifier ',' IDENT (',' IDENT)* ')' comment
    ;

//Modelica 3.6 should only allow input/output here but keeping broader support for now
base_prefix
    : type_prefix
    ;

enum_list
    : enumeration_literal (',' enumeration_literal)*
    ;

enumeration_literal
    : IDENT comment
    ;

//Added support for a more c-style comment locations
composition
    : element_list (
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
    : (c_comment | element ';')*
    ;

element
    : import_clause
    | extends_clause
    | ('redeclare')? ('final')? ('inner')? ('outer')? 
      (
        ( class_definition 
        | component_clause)
        | 'replaceable' (class_definition | component_clause) (constraining_clause comment)?
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
extends_clause
    : 'extends' type_specifier (class_or_inheritence_modification)? (annotation)?
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
class_or_inheritence_modification
    : '(' (argument_or_inheritence_list)? ')'
    ;

//New in Modelica 3.6
argument_or_inheritence_list
    : (argument | inheritence_modification) (',' (argument | inheritence_modification))*
    ;

//New in Modelica 3.6
inheritence_modification
    : 'break' (connect_clause | IDENT)
    ;

class_modification
    : '(' (argument_list)? ')'
    ;

argument_list
    : argument (',' argument)*
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

element_replaceable
    : 'replaceable' (short_class_definition | component_clause1) (constraining_clause)?
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
equation_or_comment
    : (c_comment | (equation ';'))
    ;

//Added to support tracking comments within equations and statements
statement_or_comment
    : (c_comment | (statement ';'))
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
primary
    : UNSIGNED_NUMBER
    | STRING
    | 'false'
    | 'true'
    | (component_reference | 'der' | 'initial' | 'pure') function_call_args
    | component_reference
    | '(' output_expression_list ')'
    | '[' expression_list (';' expression_list)* ']'
    | '{' array_arguments '}'
    | 'end'
    ;

//In Modelica 3.6, removed the leading "." option
name
    : IDENT ('.' IDENT)*
    ;

component_reference
    : ('.')? IDENT (array_subscripts)? ('.' IDENT (array_subscripts)?)*
    ;

function_call_args
    : '(' (function_arguments)? ')'
    ;

//Changed in Modelica 3.6
//Changed from right-recursive to iterative to avoid stack overflow on large argument lists
function_arguments
    : expression (',' function_argument)* (',' named_arguments)? ('for' for_indices)?
    | function_partial_application (',' function_argument)* (',' named_arguments)?
    | named_arguments
    ;

//New in Modelica 3.6
//Changed from right-recursive to iterative to avoid stack overflow on large arrays
array_arguments
    : expression (',' expression)* ('for' for_indices)?
    ;

//Changed from right-recursive to iterative to avoid stack overflow
named_arguments
    : named_argument (',' named_argument)*
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

expression_list
    : expression (',' expression)*
    ;

array_subscripts
    : '[' subscript_ (',' subscript_)* ']'
    ;

subscript_
    : ':'
    | expression
    ;

//description in Modelica 3.6
comment
    : string_comment (annotation)?
    ;

string_comment
    : (STRING ('+' STRING)*)?
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

//Might need to add " to this list of characters.  Space is also missing but exists in the concrete syntax.
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
    ;

fragment S_ESCAPE
    : '\\' ('’' | '\'' | '"' | '?' | '\\' | 'a' | 'b' | 'f' | 'n' | 'r' | 't' | 'v')
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