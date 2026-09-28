"""Group a Stryker report's surviving mutants by the method they sit in.

B227 asked for a way to sample before reading 361 survivors, because reading that many end to
end is how a pass like this turns into tests that assert the renderer's current output instead of
its contract. This is that way: it answers "which decisions are unguarded?" rather than listing
mutants, so a group can be judged as a whole - is this behaviour specified anywhere? - and the
groups nobody can specify can be left alone deliberately.

    python build/survivor-map.py <report.json> [--file <substring>] [--method <name>] [--all]

With --method it prints every survivor in that method, with the source line and the replacement,
which is the second pass once a group has been chosen. Without it, one line per method.

The method a line belongs to is found by scanning the C# source for declarations and tracking
brace depth, which is enough for this repository's style (one class per file, no nested types in
the files this is pointed at) and wrong in ways that would be obvious rather than silent.
"""
import argparse
import io
import json
import re
import sys
from collections import Counter, defaultdict

# A method or local-function declaration: optional modifiers, a return type, a name, and an open
# paren. Deliberately loose - a false positive names a group oddly, it does not lose a survivor.
DECLARATION = re.compile(
    r'^\s*(?:(?:public|private|protected|internal|static|override|virtual|sealed|async|partial|'
    r'new|extern|unsafe)\s+)*'
    # `else` has to be here as well as the statement keywords: without it `else if (x)` reads as
    # the type `else` followed by a method called `if`, and every arm of a chained conditional in
    # the file lands in one group called "if".
    r'(?!(?:if|else|do|try|finally|for|foreach|while|switch|catch|lock|using|return|fixed)\b)'
    r'[\w<>\[\],\.\?\(\)]+\s+'
    r'(?P<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\('
)


# A block-bodied member whose return type the strict pattern cannot span (a tuple, say). Only
# applied when the line cannot be an expression body or an assignment, so a call in one of those
# cannot be read as a declaration.
LOOSE_DECLARATION = re.compile(
    r'^\s*(?:public|private|protected|internal|static|override|virtual|sealed|async|partial)\b'
    r'(?!.*(?:=>|;|\s=\s))'
)

NAME_BEFORE_PAREN = re.compile(r'([A-Za-z_]\w*)\s*\(')


def method_at_each_line(source):
    """line number (1-based) -> the name of the method containing it."""
    lines = source.split('\n')
    owner = [None] * (len(lines) + 2)

    depth = 0
    stack = []          # (name, depth at which the method's body opened)
    pending = None      # a declaration seen, body not yet opened

    for number, line in enumerate(lines, start=1):
        stripped = line.strip()

        if stack:
            owner[number] = stack[-1][0]

        if pending is None and not stripped.startswith('//'):
            match = DECLARATION.match(line)
            if match and not stripped.endswith(';'):
                pending = match.group('name')
            elif LOOSE_DECLARATION.match(line):
                # A return type the strict pattern cannot span, such as the tuple in
                # `Task<(bool Success, string? Error)> Foo(...)`: take the identifier before the
                # last `(` that an identifier opens. Only for block-bodied members, so a call in
                # an expression body cannot be mistaken for a declaration.
                names = NAME_BEFORE_PAREN.findall(line)
                if names:
                    pending = names[-1]

        for char in line:
            if char == '{':
                depth += 1
                if pending is not None:
                    stack.append((pending, depth))
                    pending = None
            elif char == '}':
                if stack and stack[-1][1] == depth:
                    stack.pop()
                depth -= 1

        # An expression-bodied member ends on its own line, with no brace to pop.
        if pending is not None and stripped.endswith(';'):
            owner[number] = pending
            pending = None
        elif stack and owner[number] is None:
            owner[number] = stack[-1][0]

    return owner


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('report')
    parser.add_argument('--file', default=None, help='only files whose path contains this')
    parser.add_argument('--method', default=None, help='list every survivor in this method')
    parser.add_argument('--all', action='store_true', help='print every group, not the top 30')
    parser.add_argument('--status', default='Survived', help='Survived (default) or NoCoverage')
    args = parser.parse_args()

    report = json.load(io.open(args.report, encoding='utf-8'))

    total = Counter()
    for path, entry in sorted(report['files'].items()):
        if args.file and args.file.lower() not in path.lower():
            continue

        mutants = [m for m in entry['mutants'] if m['status'] == args.status]
        if not mutants:
            continue

        source = entry['source']
        lines = source.split('\n')
        owner = method_at_each_line(source)

        groups = defaultdict(list)
        for mutant in mutants:
            line = mutant['location']['start']['line']
            name = owner[line] if line < len(owner) else None
            groups[name or '<fields and initialisers>'].append(mutant)

        print(f'\n{path}  -  {len(mutants)} {args.status.lower()}')
        print('-' * 100)

        if args.method:
            chosen = groups.get(args.method)
            if not chosen:
                print(f'  no {args.status.lower()} mutants in {args.method}; '
                      f'groups here: {", ".join(sorted(k or "?" for k in groups))}')
                continue
            for mutant in sorted(chosen, key=lambda m: m['location']['start']['line']):
                line = mutant['location']['start']['line']
                print(f'  :{line:<5} {mutant["mutatorName"]:<28} -> {mutant["replacement"][:60]!r}')
                print(f'         {lines[line - 1].strip()[:110]}')
            continue

        ranked = sorted(groups.items(), key=lambda kv: -len(kv[1]))
        shown = ranked if args.all else ranked[:30]
        for name, mutants_here in shown:
            kinds = Counter(m['mutatorName'] for m in mutants_here)
            summary = ', '.join(f'{count}x {kind}' for kind, count in kinds.most_common(3))
            print(f'  {len(mutants_here):>4}  {name or "?":<42} {summary}')
            total[name or '?'] += len(mutants_here)

        if not args.all and len(ranked) > 30:
            print(f'  ... and {len(ranked) - 30} more methods with '
                  f'{sum(len(v) for _, v in ranked[30:])} between them  (--all)')

    return 0


if __name__ == '__main__':
    sys.exit(main())
