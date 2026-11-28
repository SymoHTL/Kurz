# 21. Keywords

A keyword that transforms code is declared in Kurz, with a pattern and a template, and each use of it
stands as the code its template fills (§9; issue 667, the owner's answers of 2026-11-27, comment
7215318330, and the forms of comment 7229158580, each rule marked as it stands). This chapter holds the
declaration of a file, its pattern, a use where a statement stands and what it fills. A use as an
expression, the list that uses add to, `kurz expand`, a package's keywords and those of `std/` are rules
the build answers `not supported` or holds no case of (J11 to J14).

### J1 (assumed, §9) `keyword word <pattern> { template }`, at the top level

A keyword is declared at the top level of a file: the word `keyword` first on its line, the keyword's
word, its pattern (J2), and its template, a block that opens on that line (C1) (§9, rules 1 and 3; the
form of the declaration was said back on issue 667 and not objected to in comment 7215318330). `keyword`
is a word only there, where a name and the rest of the declaration follow it on its line (L18); a
`keyword` and a name inside a declaration or a block are `syntax` at that line (E4). A file's
declarations are read before its statements, so that a use can stand before its declaration and in
another keyword's template (§9, Q7). A declaration is no statement of the program and runs no code where
it stands.

Case: [keywords/nested-and-before.kz](../corpus/keywords/nested-and-before.kz)
```kurz
twice {
    print("a")
}

keyword twice <block body> {
    once {
        body
    }

    body
}

keyword once <block body> {
    body
}
```

Case: [keywords/declared-in-a-block.kz](../corpus/keywords/declared-in-a-block.kz)
```kurz
void Run() {
    keyword twice <block body> {
        body
        body
    }
}

Run()
```

### J2 (assumed, §9) The pattern

A pattern is a row of fixed words, slots and optional parts (§9, rule 1). A fixed word is a name or a
core word, which a use writes as it stands. A slot stands between `<` and `>`: `<int times>`, a type and
a name, takes an expression of that type (J5); `<block body>` takes a block (J6); `<detached block job>`
takes a block the template runs apart (J6; §9, Q10 (a)); and `<name attempt>` takes a name the use gives
(J7; §9, Q6). An optional part stands between `[` and `]` and starts with a fixed word, by which a use
shows that it writes the part. The template names a slot by its name alone, as a function's body names
its parameters (§9, Q9 (a)), and a block slot stands alone as a statement of the template, where it runs.
A slot the template names nowhere, a block slot that stands anywhere else, a slot named twice, two
expression slots with no fixed word between them, an expression slot followed by `else` or `with`, which
go on from an expression, and an optional part that does not start with a fixed word are `syntax` at the
slot's line (E4) *(assumed: readings of the build, said back on issue 667)*. A repeated part (§9, Q18 (a)),
an optional part inside another and an expression slot inside an optional part are `not supported`.

Case: [keywords/slot-named-nowhere.kz](../corpus/keywords/slot-named-nowhere.kz)
```kurz
keyword twice <block body> <int times> {
    body
    body
}
```

Case: [keywords/repeat.kz](../corpus/keywords/repeat.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

mut k = 0
repeat 3 {
    print(k)
    k = k + 1
}
```

### J3 (assumed, §9) A use

A use stands where a statement does: the keyword's word first, then what its pattern asks, in its order,
to the end of the line or the closing brace of the pattern's last block (§9, rule 1). A fixed word is
written as it stands. An expression slot takes an expression, read as the condition of an `if` is, so
that a `{` after it opens the pattern's block. A block slot takes a block that opens on the line (C1), a
name slot takes a name, and an optional part is written where the use writes its first word. A use that
its pattern does not match is `keyword-no-match` at the line where it leaves the pattern (§9, Q16 (a)).

Case: [keywords/no-match.kz](../corpus/keywords/no-match.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

repeat {
    print(1)
}
```

### J4 (decided, §9) A template for each use

At each use the template is filled with what the use wrote and checked as code written by hand (§9, Q1,
"Template per use"). The filled template stands in the use's place as a block, so that the variables it
declares live in that block. An error inside it points at the use's line, and names each name of the template
as the template wrote it, not as the expansion took it (§9, rule 4); the line of the
expansion that rule 4 adds to the error is `not supported` by this build, which names the use's line
alone.

Case: [keywords/repeat.kz](../corpus/keywords/repeat.kz)

Case: [keywords/type-error-at-the-use.kz](../corpus/keywords/type-error-at-the-use.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

repeat "two" {
    print(1)
}
```

### J5 (decided, §9) An expression slot, once

An expression slot is evaluated once, where the keyword stands, before the template's code runs, into a
variable of the slot's type, as a function's argument is, the slots in the pattern's order (§9, Q2, "Once,
at the use"). A value of another type gets the error a variable of the slot's type gets (J4), and the
template cannot assign the variable (V7) *(assumed: the build's reading, said back on issue 667, comment 7230457821)*.

Case: [keywords/slot-once.kz](../corpus/keywords/slot-once.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

int Count() {
    print("counted")
    return 3
}

mut total = 0
repeat Count() {
    total = total + 1
}
print(total)
```

Case: [keywords/type-error-at-the-use.kz](../corpus/keywords/type-error-at-the-use.kz)

### J6 (decided, §9) Jumps out of a block slot

A `return`, `break` or `continue` in a block slot acts on the function and the loop around the use, as in
C#'s `lock` (§9, Q3, "The enclosing function"). In a block the template runs apart, written `<detached
block>` (§9, Q10 (a) *(assumed: asked on issue 667, not answered)*), a `return`, `break` or `continue`
that would leave the block is `keyword-jump` at its line (§9, Q3). The build refuses two jumps it
cannot make as Q3 says *(assumed: the build's reading of Q3, said back on issue 667)*. In a block slot
that the template puts inside a loop of its own, a `break` or `continue` that would leave the block is
`keyword-jump` at its line, since it would act on the template's loop. A plain block slot that the
template puts inside a lambda is `keyword-jump` at the slot's line, since a `return` in it would leave
the lambda.

Case: [keywords/return-leaves-the-function.kz](../corpus/keywords/return-leaves-the-function.kz)
```kurz
keyword unless <bool ok> <block body> {
    if !ok {
        body
    }
}

int Positive(int x) {
    unless x > 0 {
        return -1
    }

    return x
}

print(Positive(5))
print(Positive(-2))
```

Case: [keywords/break-in-a-template-loop.kz](../corpus/keywords/break-in-a-template-loop.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

for x in [1, 2] {
    print(x)
    repeat 2 {
        break
    }
}
```

Case: [keywords/return-from-a-detached-block.kz](../corpus/keywords/return-from-a-detached-block.kz)
```kurz
void Run(() => void f) {
    f()
}

keyword apart <detached block job> {
    Run(() => {
        job
    })
}

int F() {
    apart {
        return 1
    }

    return 0
}

print(F())
```

### J7 (decided, §9) Names

A name the template declares is its own: no name of the use reaches it, and the use's block reads the
use's variables, as the hygiene of `macro_rules!` gives it (§9, rule 6 *(assumed: said back on issue 667,
not objected to in comment 7215318330)*).
A name slot gives the template's variable of its name the name the use writes, so that the use's block
reads that variable (§9, Q6, "The use names it"); where the optional part that holds the slot is not
written, the variable is the template's own. A name the template uses and does not declare, a function's
or a type's, is read where the keyword is declared. A member after `.` or `?.`, a parameter's name before
`:` in a call and a field's name in the braces of `with` are no variables of the template *(assumed: the build's reading, said back on issue 667, comment 7230457821)*.

Case: [keywords/names-kept-apart.kz](../corpus/keywords/names-kept-apart.kz)
```kurz
keyword repeat <int times> <block body> {
    mut i = 0
    while i < times {
        body
        i = i + 1
    }
}

data P(int size)

keyword show <P p> {
    size = p.size + 1
    q = p with { size = size }
    print("size {size} {q.size}")
}

mut i = 100
repeat 2 {
    i = i + 1
}
print(i)

size = 7
show P(1)
print(size)
```

Case: [keywords/name-slot.kz](../corpus/keywords/name-slot.kz)
```kurz
keyword each <int count> [as <name index>] <block body> {
    mut index = 0
    while index < count {
        body
        index = index + 1
    }
}

each 2 as n {
    print(n)
}

each 2 {
    print("x")
}
```

### J8 (decided, §9) Keywords inside templates

A template may use keywords, its own among them (§9, Q7, "Yes, depth 64"). A use whose keywords used
inside expansions reach a depth past 64 is `keyword-depth` at its line. A keyword whose template uses it
again, itself or through another keyword's template, is `keyword-depth` at that use in the template,
since its expansions have no end *(assumed: the build's reading of Q7, said back on issue 667, comment 7230457821)*.

Case: [keywords/own-template.kz](../corpus/keywords/own-template.kz)
```kurz
keyword forever <block body> {
    forever {
        body
    }
}
```

Case: [keywords/nested-and-before.kz](../corpus/keywords/nested-and-before.kz)

### J9 (decided, §9) No redefinition

A keyword cannot be declared as a built-in keyword, nor as another keyword (§9, rule 8). A built-in
keyword is a core word of L18, a type's name of L19 or a word of a form that stands where a statement
starts: `keyword`, `within`, `test`, `check`, `advance`, `mock`, `const`, `attempt`, `retry`, `restart`,
`stop` and `escalate`; a keyword is declared once in a file. Each of these is `keyword-redefined` at the
word. A keyword's word is no name in its file: before `=`, and alone where an expression stands, it is
`reserved-word` at its line (L12) *(assumed: the build's readings of rule 8, said back on issue 667)*.

Case: [keywords/core-word.kz](../corpus/keywords/core-word.kz)
```kurz
keyword while <block body> {
    body
}
```

Case: [keywords/word-is-no-name.kz](../corpus/keywords/word-is-no-name.kz)
```kurz
keyword twice <block body> {
    body
}

twice = 2
```

### J10 (assumed, §9) The grant

A keyword is declared by a package that has the grant `keywords`, and a package that uses its keywords
needs nothing (§9, the owner, 2026-10-30, "Definer, `allow keywords`"). While the build reads no
`project.kz`, only `std/` and a case whose header grants `keywords`, `// allow: keywords` as `raw`'s is
(R7), may declare a keyword (§9, Q15 (a)). A declaration without the grant is `keyword-grant` at its line.

Case: [keywords/no-grant.kz](../corpus/keywords/no-grant.kz)
```kurz
keyword twice <block body> {
    body
    body
}

twice {
    print(1)
}
```

### J11 (decided, §9) A keyword as an expression

A keyword can stand as an expression, whose value is its expansion's (§9, Q4, "Yes"), and where it may
stand is read from its template's shape (§9, Q11 (a) *(assumed: asked on issue 667, not answered)*). This
build answers a use as an expression `not
supported`.

No case: the build answers such a use `not supported`, which no case can expect.

### J12 (decided, §9) The list that uses add to

Each use of a keyword whose pattern names a list adds an entry to it, and the program reads the list at its
start (§9, Q5, "A list read at start"). The list is named after the pattern, `into routes`; a use that
adds an entry stands at the top level of a file, and the entries are read in the order of the files'
paths, then of the uses in each file (§9, Q12 (a), Q13 (a), Q14 (a) *(assumed: asked on issue 667, not
answered)*). This build reads no list.

No case: the build reads no list.

### J13 (decided, §9) `kurz expand`

A command shows what each use expands to (§9, rule 4, "kurz expand + editor"): `kurz expand FILE` prints
the file with each use replaced by its expansion (§9, Q17 (a) *(assumed: asked on issue 667, not
answered)*). The command `kurz` has no `expand` in this build.

No case: no command of the build prints an expansion.

### J14 (decided, §9) The keywords of `std/` and of a package

The keywords of `std/` come with the prelude, and a package's keywords are named in the file's `use`
line (§9, Q8, "In the prelude"); `pub keyword` makes a keyword visible outside its package (§9
*(assumed: said back on issue 667, not objected to in comment 7215318330)*). `std/` declares no keyword, and the build reads no package.

No case: `std/` declares no keyword, and the build reads no package.
