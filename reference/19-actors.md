# 19. Actors

An actor is an object with private state and an inbox, which handles one message at a time; a call
of its method through its reference is a message (§6, "Actors"). This chapter holds the actors of
one machine and what a program writes of them: an actor's declaration, `spawn`, the call that waits
for its result, `send`, and what crosses into a message, the crash of an actor, its restart by
the default policy and its log line (U19 to U24), the words of a policy (U25 to U27), the block of a policy (U28
to U34), the threads that run the actors (U35), the bounds of the calls that wait (U36 to U45), the block of an `attempt` (U46 to U50) and the actors declared `per node` (U51 to U55). The scheduler but its threads, the stacks, deadlocks but the
calls a waiting actor lets in (U17), the loops of calls that wait between actor types (U56 to U59), the wait line
that each wait of an actor follows (U60 to U66), the wait of the top-level code that nothing can end (U18) and the deadline of a call that waits in a message, which is nothing
left to run (U42), the clauses after an actor's name but
the stack's, the policy's and `per node` (U1, U27, U51) and the actors of other machines (§6, §7) have no rule here, and `compiler/README.md` says what the compiler builds of the rules here.

### U1 (decided, §6) `actor`, and its words

`actor Name(Type Field, ...) { ... }` declares an actor, with a primary constructor and a body as a
class has them (K1, K7), whose fields nothing outside the actor reaches (U8) (§6, "An actor is an
object with private state"). `actor`, `spawn` and `send` are core words, so that none of them names
a variable, a function or a type (L18), and the clauses after an actor's name, `per`, `yield`,
`inbox`, the stack's size and `on crash`, stand before its body in any order, their words words
only there (§6, the owner, 2026-11-13, #519, comment 6847932993, Q10, "Any order + contextual
(Recommended)"), after the head a class has, its name, its type parameters, its primary
constructor and its `:` list (T19, K1, K7), so that a clause before the `:` is `syntax` (E4)
*(assumed: a class's head read for an actor's, which the answer did not give)*. An actor may
declare type parameters and interfaces after its name, as a Rust
struct that a task runs takes type parameters and implements traits, and its methods take a
class's modifiers (K7); it has no base, so that a type after its name that is no interface is
`syntax`, and so is an actor after a class's `:` *(assumed: that id, which the answer did not give,
as E4 gives one to a form no rule gives a meaning)*, and it holds no `static` state (§6, the owner,
2026-11-14, #519, comment 6882624606, Q26, "All six leans (Recommended)"): a `static mut` field of
an actor, or a `static` field whose type is or holds a class, an interface's type, a function type
or a type parameter, is `static-state`, as K18 makes a class's, by its written type (§4, the owner,
2026-11-15, issue 519, comment 6891937666, item 3, option (a), "Written type, Rust"), since a class
instance, mutable or not, lives in one actor's heap (K18), and an immutable
`static` field of another type is no `static` state in K18's sense and stands as a class's does
*(assumed: K18's reading of `static` state, which the answer did not name; a class's immutable
`static` field of such a type is reached from every actor, so that one of an actor shares nothing
more, as a Rust `static` that is `Sync` stands)*. How a generic
actor's messages are built, a value of a type parameter's type among a message's arguments and its
result included, and a call through an interface as a message stand open (§14).

The stack's clause is `stack` and a literal of bytes, `kb`, `mb` or `gb` (L14), `actor Parser stack 8mb
{ ... }`: it sets the size of the stack each message of the actor runs on, which is 2 MiB without it,
as a thread that Rust's standard library spawns has (§6, the owner, 2026-11-13, #519, comment
6847932993, Q4, "2 MiB + clause", and the owner, 2026-11-15, #519, comment 6892812780, Q3, "Build it now"). The size
is read as a `long`, so that a literal that is no whole number of bytes is `inexact-literal` (L14)
and one past the largest `long` is `constant-overflow`, as a literal that does not fit the type it
takes (L10); the runtime rounds it up to a whole page and raises it to its floor, 64 KiB, with no
message, so that the clause sets at least what it says (§6, the owner, 2026-11-15, #519, comment 6892835500, Q7,
"Rust/C#: round up silently") *(assumed: the floor, as §6 marks it)*, and a stack the system refuses
ends the process with `allocation-failed`, naming the size so rounded, 2 MiB where no clause sets
one, without the guard page Linux maps below it (O11), and a size past 2^62 bytes, which no system
maps, ends it so before the rounding, naming the size as written *(assumed: the size and that limit,
as §6 marks them)*. `stack` with no literal of bytes after it, a number with no unit or with another unit
among them, and `stack` written twice are `syntax` (E4) *(assumed: as §6 marks it)*. On a target where
no actor has a stack of its own the clause builds and sets nothing (§6, the owner, 2026-11-13, #519,
comment 6851105334, "Accept, set nothing (Recommended)").

`on crash` after an actor's name takes the words of a policy as they stand after `spawn`'s arguments
(U2), `actor Worker on crash stop { ... }`, and gives the type's default (U27) (§6, "Policy at
`spawn`"), and a second `on crash` there is `syntax`, as a second `stack` is (E4) *(assumed: as §6
marks it)*; `on crash` with a block after it, `on crash (c) { ... }`,
is U28's.

Case: [actors/counter.kz](../corpus/actors/counter.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

c = spawn Counter()
send c.Add(1)
send c.Add(2)
print(c.Get())
```

Case: [actors/spawn-reserved-word.kz](../corpus/actors/spawn-reserved-word.kz)
```kurz
spawn = 1
print(spawn)
```

Case: [actors/static-state.kz](../corpus/actors/static-state.kz)
```kurz
actor Counter {
    static mut int total = 0
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

c = spawn Counter()
send c.Add(1)
print(c.Get())
```

Case: [actors/stack-clause.kz](../corpus/actors/stack-clause.kz)
```kurz
actor Parser stack 8mb {
    pub long Down(long n) {
        if n == 0 {
            return 0
        }

        return Down(n - 1) + 1
    }
}

p = spawn Parser()
print(p.Down(60000))
```

Case: [actors/stack-floor.kz](../corpus/actors/stack-floor.kz)
```kurz
actor Small stack 1kb {
    pub long Down(long n) {
        if n == 0 {
            return 0
        }

        return Down(n - 1) + 1
    }
}

s = spawn Small()
print(s.Down(200))
```

Case: [actors/stack-twice.kz](../corpus/actors/stack-twice.kz)
```kurz
actor Parser stack 8mb stack 4mb {
    pub int Get() => 1
}

p = spawn Parser()
print(p.Get())
```

Case: [actors/stack-without-bytes.kz](../corpus/actors/stack-without-bytes.kz)
```kurz
actor Parser stack 8 {
    pub int Get() => 1
}

p = spawn Parser()
print(p.Get())
```

Case: [actors/stack-inexact.kz](../corpus/actors/stack-inexact.kz)
```kurz
actor Parser stack 0.1kb {
    pub int Get() => 1
}

p = spawn Parser()
print(p.Get())
```

Case: [actors/base-after-name.kz](../corpus/actors/base-after-name.kz)
```kurz
class Box(int Size)

actor Store : Box {
    pub int Get() => 1
}

s = spawn Store()
print(s.Get())
```

### U2 (decided, §6) `spawn`

`spawn Name(args)` makes an actor, its arguments bound to its primary constructor as a class's
construction binds them (K1, D11), and gives its reference, a value of the type `Name`; each `spawn`
makes an actor with a state of its own. An actor's constructor is its primary one, and a call of an
actor's name without `spawn` is the compile error `spawn-required` (§6, the owner, 2026-11-13, #519,
comment 6847932993, Q13, "Primary ctor, kept args (Recommended)"). An actor's reference compares by
the actor it names: `==` is true of two references to one actor, `!=` of two to different actors,
its hash is its actor's number's, so that a reference can be a key of a map, and its text is its
actor's type and number, `Counter#1`, the number counted from 1 in the order in
which the actors of one machine are spawned (§6, the owner, 2026-11-14, #519, comment 6880404917, Q10, "All eight leans (Recommended)") *(assumed: that shape and that
count, which the answer did not give)*. `==` and `!=` take two references to actors of one type, and
on references to actors of two types they are `type-mismatch`, since no actor's type converts to
another's, an actor having no base (U1), as T18 makes an operator on two values it does not take,
as C#'s `==` takes no two classes neither of which converts to the other, and as Q10
(comment 6880404917) compares a reference as C# compares a class's instances, by reference. An
actor's reference beside a value of an interface it implements stands with K13's open item (§14).
`spawn` takes the name and its argument list, and what follows applies to the reference, so that
`spawn Counter().Get()` calls `Get` on the new actor, as C#'s `new Counter().Get()` binds (§6, the
owner, 2026-11-14, #519, comment 6882624606, Q25, "All six leans (Recommended)"). Between the name
and its argument list stand the actor's type arguments where it has type parameters (U1, Q26),
written or inferred as a generic class's construction takes them (T19, T27) *(assumed: Q26's type
parameters read with T19's use of a generic type and U2's binding, which no answer named)*. `equal by` on an
actor and a constructor in its body are `syntax`, since this rule gives the actor's `==` and its
constructor (§6, comment 6882624606, Q26).

A reference holds the index of its actor's slot in a table of the process and the actor's number,
which is the slot's generation: an actor that ends clears its number from its slot, a later `spawn`
takes the slot again and writes its own number there, larger than every number before it, and a call
or a `send` through a reference whose number its slot no longer holds raises `actor-gone` (U20, U6)
(§6, the owner, 2026-11-16, #519, comment 6919351399, "Erlang: id + generation") *(assumed: the
generation is the number this rule counts, so that the end raises the slot's generation by the larger
number of the next actor that takes it, as §6 marks it)*. `==`, `!=`, a reference's hash as a map's key
and its text read its number alone, so that a reference whose actor ended compares, hashes and prints
as it did before the end *(assumed: §6, since no other actor of the process has that number)*. A
`spawn` past the largest `long` ends the process with `allocation-failed`, naming the bytes of the
actor's record and its state, as a string past the largest `int` does (T15) *(assumed: §6, so that no
two actors of a process have one number)*.

`spawn` stands wherever an expression does, in a function, a class's method, a lambda and a field's
default among them, as a call that waits does (U4).

After the argument list stand the words of a policy, which U25 and U26 give a meaning:
`on crash restart`, `on crash stop` or `on crash escalate`, and after `restart` its knobs,
`after <duration>` and `max N in <duration>`, `spawn Worker(queue) on crash restart after 1s max 10 in
1min` (§6, "Policy at `spawn`"). Each knob stands at most once, in either order, and its value is a
number literal, with its sign where it has one, which the checker reads (U26); the policy ends the
expression. Another word after `on crash`, a knob after `stop` or `escalate`, a knob written twice,
`max N` without its `in` and its duration, a value that is no number literal and anything after the
policy but the end of the expression are `syntax` (E4) *(assumed: as §6 marks it, as `stack` takes its
literal once (U1))*. `on crash` with a block after it, `on crash (c) { ... }`, is U28's.

Case: [actors/spawn-in-function.kz](../corpus/actors/spawn-in-function.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

Counter Make(int start) {
    made = spawn Counter()
    send made.Add(start)
    return made
}

actor Factory {
    pub int Build() {
        built = Make(5)
        send built.Add(1)
        return built.Get()
    }
}

c = Make(2)
print(c.Get())
f = spawn Factory()
print(f.Build())
```

Case: [actors/two-instances.kz](../corpus/actors/two-instances.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

a = spawn Counter()
b = spawn Counter()
send a.Add(1)
send b.Add(2)
print(a.Get())
print(b.Get())
```

Case: [actors/constructor-arguments.kz](../corpus/actors/constructor-arguments.kz)
```kurz
int Made(int n) {
    print("made {n}")
    return n
}

actor Account(int Start) {
    mut int balance = Made(Start)

    pub void Deposit(int n) {
        balance = balance + n
    }

    pub int Balance() => balance
}

a = spawn Account(10)
print("spawned")
send a.Deposit(5)
print(a.Balance())
```

Case: [actors/spawn-required.kz](../corpus/actors/spawn-required.kz)
```kurz
actor Counter {
    int count = 0

    pub int Get() => count
}

c = Counter()
print(c.Get())
```

Case: [actors/counter.kz](../corpus/actors/counter.kz)

Case: [actors/identity.kz](../corpus/actors/identity.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }
}

a = spawn Counter()
b = spawn Counter()
c = a
print("{a} {b}")
print(a == c)
print(a == b)
```

Case: [actors/references-map-key.kz](../corpus/actors/references-map-key.kz)
```kurz
actor Counter {
}

actor Hub {
    pub string Find(Map<Counter, string> known, Counter c) => known[c] ?? "none"
}

a = spawn Counter()
b = spawn Counter()
mut names = Map<Counter, string>()
names[a] = "a"
names[b] = "b"
h = spawn Hub()
print(h.Find(names, a))
print(h.Find(names, b))
```

Case: [actors/generic-reference.kz](../corpus/actors/generic-reference.kz)
```kurz
actor Counter {
    mut int count = 0

    pub int Bump() {
        count = count + 1
        return count
    }
}

T Same<T>(T x) => x

c = spawn Counter()
print(Same(c).Bump())
```

Case: [actors/first-error-spawn-required.kz](../corpus/actors/first-error-spawn-required.kz)
```kurz
actor Counter(int Start) {
    pub int Get() => Start
}

c = Counter("x")
print(c.Get())
```

Case: [actors/equal-by.kz](../corpus/actors/equal-by.kz)
```kurz
actor Point(int X) equal by X {
    pub int Get() => X
}

p = spawn Point(1)
print(p.Get())
```

### U3 (decided, §6) The constructor runs while the spawner waits

An actor's constructor, its fields' defaults with it, runs in the new actor while the code that
spawned it waits, as Elixir's `GenServer.start_link` waits for `init/1`, so that on one machine what
the constructor prints comes before what the spawner prints after its `spawn`, and a throw in it at
the `spawn` is the spawner's crash (§6, the owner, 2026-11-14, #519, comment 6880404917, Q2, "All nine leans (Recommended)"),
while a throw in its run at a restart is the actor's own crash under its policy (§6, the owner,
2026-11-14, #519, comment 6882624606, Q28, "All six leans (Recommended)").

A call that reaches the actor while this constructor runs at its `spawn` waits in its inbox, as at a
restart (U22), one rule for every start of an actor (§6, the owner, 2026-11-21, #599, comment
7060156109, Q3 1), and no program can make one: no code holds the reference before `spawn` returns,
since a field's default reads no `this` (K7) and a constructor in an actor's body is `syntax` (U2).

Case: [actors/constructor-arguments.kz](../corpus/actors/constructor-arguments.kz)

Case: [actors/constructor-waits.kz](../corpus/actors/constructor-waits.kz)
```kurz
actor Config {
    pub int Size() {
        print("size asked")
        return 3
    }
}

actor Pool(Config Settings) {
    int size = Settings.Size()

    pub int Count() => size
}

s = spawn Config()
p = spawn Pool(s)
print("spawned")
print(p.Count())
```

Case: [actors/constructor-this-refused.kz](../corpus/actors/constructor-this-refused.kz)
```kurz
actor B {
    pub int Ask(A x) => 0
}

actor A(B Other) {
    int first = Other.Ask(this)
    List<int> items = [1, 2, 3]

    pub int Peek() => items.Count
}

print(spawn A(spawn B()).Peek())
```

### U4 (decided, §5, §6) A call through a reference waits

A call of an actor's `pub` method through its reference, `c.Get()`, is a message, and its caller
waits for its result (§6, "A call waits for its result by default"). The result is read as any
call's: its success is the value, and its other cases leave the calling code or are handled, as O2
to O5 say (§5).

A call through a reference stands wherever a call does: in the top-level code, an actor's method, a
class's method, a function, a lambda and a field's default, an actor's among them, which runs inside
`spawn` (U3), since every call looks synchronous and the runtime parks the actor while it waits, on a
stack of its own, so that no function is rewritten (§6, "There is no `async`/`await`", and the stack
of an actor parked in the middle of a call). A call that waits inside a message parks that message:
its actor takes no other message until the reply is set but the calls of its chain (U17).

Case: [actors/wait-in-method.kz](../corpus/actors/wait-in-method.kz)
```kurz
actor Price {
    pub int Of(int n) => n * 5
}

actor Cart(Price Prices) {
    pub int Total(int n) {
        return Prices.Of(n) + 1
    }
}

p = spawn Price()
c = spawn Cart(p)
print(c.Total(3))
```

Case: [actors/wait-in-function.kz](../corpus/actors/wait-in-function.kz)
```kurz
actor Price {
    pub int Of(int n) => n * 5
}

int Twice(Price p, int n) => p.Of(n) * 2

actor Cart(Price Prices) {
    pub int Total(int n) => Twice(Prices, n) + 1
}

prices = spawn Price()
cart = spawn Cart(prices)
print(cart.Total(3))
print(Twice(prices, 1))
```

Case: [actors/wait-in-lambda.kz](../corpus/actors/wait-in-lambda.kz)
```kurz
actor Price {
    pub int Of(int n) => n * 5
}

actor Cart(Price Prices) {
    pub int Total(List<int> counts) {
        priced = (int n) => Prices.Of(n)
        mut total = 0
        for n in counts {
            total = total + priced(n)
        }

        return total
    }
}

p = spawn Price()
c = spawn Cart(p)
print(c.Total([1, 2, 3]))
of = (int n) => p.Of(n)
print(of(4))
```

Case: [actors/constructor-waits.kz](../corpus/actors/constructor-waits.kz)

Case: [actors/union-result.kz](../corpus/actors/union-result.kz)
```kurz
data NotFound

actor Store {
    pub int | NotFound Find(int id) {
        if id == 1 {
            return 10
        }
        return NotFound
    }
}

s = spawn Store()
value = s.Find(1) else {
    NotFound => 0
}
print(value)
match s.Find(2) {
    int n => print(n)
    NotFound => print("none")
}
```

Case: [actors/counter.kz](../corpus/actors/counter.kz)

Case: [actors/two-instances.kz](../corpus/actors/two-instances.kz)

### U5 (decided, §6) The order of one sender's messages

Messages from one actor to another arrive in the order they were sent, the top-level code among the
actors, as the root actor (§6, the owner, 2026-11-14, #519, comment 6880404917, Q18, "All five leans
(Recommended)").

Case: [actors/send-order.kz](../corpus/actors/send-order.kz)
```kurz
actor Printer {
    pub void Show(int n) {
        print(n)
    }

    pub string Done() => "done"
}

p = spawn Printer()
send p.Show(1)
send p.Show(2)
send p.Show(3)
print(p.Done())
```

Case: [actors/counter.kz](../corpus/actors/counter.kz)

Case: [actors/two-instances.kz](../corpus/actors/two-instances.kz)

Case: [actors/own-method-direct.kz](../corpus/actors/own-method-direct.kz)
```kurz
actor Doubler {
    mut int total = 0

    pub void Add(int n) {
        total = total + Twice(n)
    }

    int Twice(int n) => n * 2

    pub int Total() => this.total
}

d = spawn Doubler()
send d.Add(1)
send d.Add(2)
print(d.Total())
```

Case: [actors/constructor-arguments.kz](../corpus/actors/constructor-arguments.kz)

Case: [actors/send-gone.kz](../corpus/actors/send-gone.kz)
```kurz
actor Child {
    pub void Note(int n) {
        print(n)
    }
}

actor Parent {
    mut Child? kid = null

    pub void Make() {
        kid = spawn Child()
    }

    pub Child? Kid() => kid

    pub void Boom() {
        throw "parent fails"
    }
}

p = spawn Parent()
p.Make()
kid = p.Kid()
send p.Boom()
print(p.Kid() == null)
if kid != null {
    send kid.Note(1)
}
```

Case: [actors/crash-child-gone.kz](../corpus/actors/crash-child-gone.kz)
```kurz
actor Child {
    pub int Ping() => 1
}

actor Parent {
    mut Child? kid = null

    pub void Make() {
        kid = spawn Child()
    }

    pub Child? Kid() => kid

    pub void Boom() {
        throw "parent fails"
    }
}

p = spawn Parent()
p.Make()
kid = p.Kid()
send p.Boom()
print(p.Kid() == null)
if kid != null {
    print(kid.Ping())
}
```

### U6 (decided, §6) `send`

`send` before a call of an actor's method, `send c.Add(1)`, makes the call a message that nobody
waits for (§6, "`send` makes a call fire-and-forget"). `send <call>` stands as a statement and gives
no value, so that a `send` where a value stands, `print(send c.Add(1))`, is `syntax`, as is a call
whose success is `void` used as a value (O10). A `send` of a method that gives a result is
the compile error `send-result`, since its result and its cases would reach nobody (§6, the owner,
2026-11-13, #519, comment 6847932993, Q12, "send-result error (Recommended)"). `send` stands
as a statement wherever a call does, in a function, a class's method and a lambda among them, as a call that
waits does (U4).

A `send` through a reference whose actor ended, a child whose supervisor crashed (U21) or one whose
policy stopped it (U25), raises `actor-gone` at the `send`'s line, as a call does (U20), the sending
actor's own crash in its turn and at the top-level code the end of the process (§6, the owner,
2026-11-16, #519, comment 6923513534, "Raise actor-gone").

Case: [actors/spawn-in-function.kz](../corpus/actors/spawn-in-function.kz)

Case: [actors/send-result.kz](../corpus/actors/send-result.kz)
```kurz
actor Counter {
    int count = 0

    pub int Get() => count
}

c = spawn Counter()
send c.Get()
```

Case: [actors/counter.kz](../corpus/actors/counter.kz)

Case: [actors/send-order.kz](../corpus/actors/send-order.kz)

Case: [actors/relay.kz](../corpus/actors/relay.kz)
```kurz
actor Printer {
    mut int last = 0

    pub void Show(int n) {
        print("relayed {n}")
        last = n
    }

    pub int Last() => last
}

actor Relay(Printer Target) {
    pub void Pass(int n) {
        send Target.Show(n)
    }
}

p = spawn Printer()
r = spawn Relay(p)
r.Pass(7)
print(p.Last())
```

Case: [actors/first-error-send-as-value.kz](../corpus/actors/first-error-send-as-value.kz)
```kurz
actor Counter {
    int count = 0

    pub int Get() => count
}

c = spawn Counter()
x = send c.Get()
print(x)
```

Case: [actors/send-gone.kz](../corpus/actors/send-gone.kz)

### U7 (assumed, §6) A `void` with cases gives a result

A method whose success is `void` with cases beside it, `void | Full`, gives a result, so that its
`send` is `send-result`, since its cases would reach nobody (§6, *(assumed)* there, of Q12).

Case: [actors/send-void-or-error.kz](../corpus/actors/send-void-or-error.kz)
```kurz
data Full

actor Store {
    mut int count = 0

    pub void | Full Add() {
        if count == 3 {
            return Full
        }
        count = count + 1
    }
}

s = spawn Store()
send s.Add()
```

### U8 (decided, §6) Its fields are its own

Nothing outside an actor reaches its fields (§6, "Nobody outside can touch its fields"): a read or
an assignment of one through a reference is the compile error `not-visible`, F9's id (§6, the owner, 2026-11-14, #519, comment 6880404917,
Q20, "All five leans (Recommended)"). A `pub` field or a `pub` property of an actor is the compile error `actor-field-public`,
at its declaration (§6, the same comment, Q4, "All nine leans (Recommended)") *(assumed: the id's name, which the answer did
not give, and that a property without `pub` is the actor's own, as a method without `pub` is)*.
The fields of an actor's primary constructor are its own too, since F9's exception for the fields of
a primary constructor does not hold for an actor (U1; §6, "Nobody outside can touch its fields"): a
read of one through a reference, `a.Start` of `actor Account(int Start)`, is `not-visible`, and none
of them is `actor-field-public`, which names a field or a property written `pub`.

Case: [actors/field-not-visible.kz](../corpus/actors/field-not-visible.kz)
```kurz
actor Counter {
    int count = 0

    pub int Get() => count
}

c = spawn Counter()
print(c.count)
```

Case: [actors/field-public.kz](../corpus/actors/field-public.kz)
```kurz
actor Counter {
    pub int count = 0

    pub int Get() => count
}

c = spawn Counter()
print(c.Get())
```

### U9 (decided, §6) A call through a reference calls a `pub` method

A method called through an actor's reference has `pub`, and a call of one without it is
`not-visible`, as F9 makes a method without `pub` private to its class (§6, the owner, 2026-11-14, #519, comment 6880404917, Q19, "All five leans (Recommended)"). A
`mut` parameter of a method called through a reference, waiting or sent, is the compile error
`mut-in-message`, since a message holds no place of its caller's (§6, the same comment, Q6, "All nine leans (Recommended)")
*(assumed: the id's name, which the answer did not give)*. The error stands at each call or `send`
through which a message reaches the parameter, once for each, and not at the parameter's
declaration, since a direct call of the actor's own method takes the place as a class's does (U10).

Case: [actors/private-method.kz](../corpus/actors/private-method.kz)
```kurz
actor Counter {
    int count = 0

    int Get() => count
}

c = spawn Counter()
print(c.Get())
```

Case: [actors/mut-in-message.kz](../corpus/actors/mut-in-message.kz)
```kurz
actor Counter {
    pub void Bump(mut int n) {
        n = n + 1
    }
}

c = spawn Counter()
mut x = 1
c.Bump(mut x)
print(x)
```

Case: [actors/first-error-mut-in-message.kz](../corpus/actors/first-error-mut-in-message.kz)
```kurz
actor Counter {
    pub int Bump(mut int n) {
        n = n + 1
        return n
    }
}

c = spawn Counter()
mut x = 1
send c.Bump(mut x)
print(x)
```

### U10 (decided, §6) Inside, its own members are direct

Inside an actor its own fields and methods are reached as a class's are, by their names or through
`this` (K7), and a call of its own method is a direct call and no message (§6, the owner,
2026-11-13, #519, comment 6847932993, Q14, "Direct call (Recommended)"). `this` as a value inside
an actor, not before a member, is the actor's own reference, a value of its type, which crosses into
a message as any reference does (U12) (§6, the owner, 2026-11-14, #519, comment 6880404917, Q7, "All nine leans (Recommended)").

Case: [actors/own-method-direct.kz](../corpus/actors/own-method-direct.kz)

Case: [actors/call-self-through-this.kz](../corpus/actors/call-self-through-this.kz)
```kurz
actor Counter {
    mut int count = 0

    pub int Next() {
        me = this
        count = count + 1
        return me.Get() + 100
    }

    pub int Get() => count
}

c = spawn Counter()
print(c.Next())
print(c.Next())
```

Case: [actors/this-reference.kz](../corpus/actors/this-reference.kz)
```kurz
actor Worker {
    pub void Join(Registry to) {
        send to.Add(this)
    }
}

actor Registry(Worker Expected) {
    mut bool matched = false

    pub void Add(Worker who) {
        matched = who == Expected
    }

    pub bool Matched() => matched
}

w = spawn Worker()
r = spawn Registry(w)
w.Join(r)
print(r.Matched())
```

Case: [actors/vouch-own-method.kz](../corpus/actors/vouch-own-method.kz)
```kurz
actor A {
    pub int Size() => 1

    pub int Twice() => vouch this.Size() + 1
}

a = spawn A()
print(a.Twice())
```

### U11 (decided, §6) `send` of its own method

`send` of an actor's own method inside it, `send Second()` or `send this.Second()`, is a message
queued behind the one that runs, at the end of the actor's inbox, behind every message that waits
there, as Akka's `self ! m` and Elixir's `GenServer.cast(self(), ...)`
are (§6, the owner, 2026-11-14, #519, comment 6880404917, Q5, "All nine leans (Recommended)"), and a `mut` parameter of the method so sent is `mut-in-message`, as in
U9, at each such `send` *(assumed: that U9's answer, Q6, holds this `send` too, which is a message)*.
It may name a method without `pub`, which the actor sees as F9 lets a type see its own members,
since U9 asks `pub` of a call through a reference alone (§6, the owner, 2026-11-14, #519, comment
6882624606, Q23, "All six leans (Recommended)").

Case: [actors/send-own-method.kz](../corpus/actors/send-own-method.kz)
```kurz
actor Worker {
    pub void Start() {
        send Second()
        send Third()
        print("first")
    }

    pub void Second() {
        print("second")
    }

    pub void Third() {
        print("third")
    }

    pub void Done() {
        print("done")
    }
}

w = spawn Worker()
w.Start()
w.Done()
```

Case: [actors/mut-in-own-send.kz](../corpus/actors/mut-in-own-send.kz)
```kurz
actor Worker {
    pub void Go() {
        mut x = 1
        send Bump(mut x)
        print(x)
    }

    void Bump(mut int n) {
        n = n + 1
    }
}
```

### U12 (decided, §6) What crosses

What crosses between actors is a value (a string, a number), a collection, a `data` value or an
immutable class instance where every type its type reaches crosses, a lambda the sending function shows
where every value it captures crosses (§6, comment 6896192351, Q5), and an actor's reference; a mutable class instance, and a collection or a `data` value that reaches one, crosses with `copy`
or `move` alone, where every type its type reaches crosses or is a mutable class, and with
`move` only where every path into it ends (§6, "What may cross between actors"). A message's arguments cross so, and its result too but for a function value and a value whose type reaches one, which are
`send-needs-word` as a result *(assumed, §6: as rustc asks `Send` of a task's output by its type)*, and so
do the arguments of an actor's constructor, which cross as a call's do (§6, the owner,
2026-11-13, #519, comment 6847932993, Q13). A mutable class instance among a message's arguments
or a constructor's with neither word is the compile error `send-needs-word`, at its argument
*(assumed: that place, as Q24 gives a function value's)*, which has its row in the table of
errors, since cases expect it (§6, the owner, 2026-11-13, #519, comment 6847932993, Q8, "These
ids (Recommended)"; 2026-11-15, #519, comment 6896192351), and so is a function value there whose captures do not
cross, at its argument, as rustc gives one error, E0277, for a closure as for any type that is not
`Send` (§6, the owner, 2026-11-14, #519, comment 6882624606, Q24, "All six leans (Recommended)").
No word at the argument makes such a function value cross, `copy f` and `move f` among them, the
fix being in what it captures (§6) *(assumed: Q24's cost, "the fix is in the capture", read for a
word at the argument, as rustc reads a closure's `Send` from the types of what it captures,
written `move` or not)*. A value of an interface's type is read by its written type, which may hold
a mutable class instance whatever the program puts there, so that one among a message's arguments
or a constructor's written with no word is `send-needs-word`, as K18 reads a `static` field's type
and as rustc finds no auto trait on a trait object but in its bounds (§4, §6, the owner,
2026-11-15, issue 519, comment 6891937666, item 3, option (a), "Written type, Rust"). No word makes
such a value cross: it is `send-needs-word` with `copy` or `move` too, as `Box<dyn Trait>` is not
`Send` in Rust, moved or cloned, unless its type says `+ Send` (§6, the owner, 2026-11-15, issue
519, comment 6892275110, item 2, option (a), "No, never crosses"), and so is a collection, a `data`
value or a class instance that reaches one *(assumed: the answer read through what a value reaches,
as a function value whose captures do not cross is read, as rustc reads `Send` through a struct's
fields)*. An actor's reference held in a variable of an interface's type, which a call as a message
through an interface that an actor implements would take, stands with that open point (§14, Q26). A
value of an interface's type given as a message's result is, by its written type, the
compile error of a mutable class instance given as a call's result (§6, the owner, 2026-11-15,
issue 519, comment 6892275110, item 3, option (a), "Same error, by written type"), and so is a
collection, a `data` value or a class instance that reaches one *(assumed: as §6 marks it)*.
An immutable class is one through which nothing
reachable has a `mut` field, and a value crosses where every type its type reaches crosses, a
collection's item type and a `data` value's field types among them, as Rust's `Send` is
structural and read from types *(assumed: the reading of Q14 (i) of #519, comment 6866132292, said
back in comment 6882879015, reading 4, read by types, as a crossing copies by the parameter's type)*; a lambda
the sending function shows crosses where every value it captures crosses (§6, comment 6896192351, Q5), and a
mutable class instance given as a call's result, where no word can stand, is a compile error (§6,
the owner, 2026-11-14, #519, comment 6880404917, Q14, "All eight leans (Recommended)"), and that
error is `send-needs-word`, as rustc gives the same error to a task's output as to its input
(`tokio::spawn` asks `F::Output: Send`) (§6, the owner, 2026-11-15, #519, comment 6896192351, Q4,
"Rust: send-needs-word"), at the line of the call *(assumed: that place, since no argument stands
for a result)*, and so is a function value and a value whose type reaches a type no word makes cross, or
a mutable class instance, given as a result *(assumed, §6: as rustc asks `Send` of a task's output by
its type, which Q4 named for a mutable class instance and an interface's value alone)*.
A collection or a `data` value that reaches a mutable class instance is `send-needs-word` at its
argument with neither word, and crosses with `copy`, a deep copy of all it reaches up to an actor's
reference, since one that reaches a function value does not cross (§6, comment 6896192351, Q5) *(assumed, §6: through an
item, as through a field, which the answer named)*, or with `move`, every path into it ending, `move-reachable` where
one cannot *(assumed: that reading of §6's `copy` and `move`, carried from a mutable class instance
to a value that holds one, where rustc reads `Send` through a struct's fields whatever a call
writes)*. A collection, a `data` value or a class instance that reaches a function value is
`send-needs-word` at its argument with a word or with none, as that function value is, one
reached through a field or an item being read by its written type (§6, comment 6896192351, Q5) *(assumed, §6: an item
read as the field the answer named is)*, the paths `move` ends stopping at a function value it reaches *(assumed: Q24's
cost read for a word at the argument as for that function value, as rustc reads `Send` through a
struct's fields and a closure's through its captures)*. A reference that crosses names the same
actor, so that `==` holds across a crossing (§6, the owner, 2026-11-14, #519, comment 6880404917,
Q10). The deep copy of `copy` adds nothing at a function value it reaches, which the receiver gets as it
would with no word, copied into its heap with the values it captures where the call crosses (§6,
the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big unbuilt"), there only
the argument itself, a lambda the sending function shows, since one reached through a field or an
item does not cross (§6, comment 6896192351, Q5) *(assumed, §6: an item read as the field the answer named is, and a
lambda inside an argument, a field of a construction there, read by its written type)*, an instance it captures arriving as the copy that an argument
reaching the same instance arrives as, in the memo of the call (§6) *(assumed, §6: the memo read
for what a lambda captures)*, and shared by its count with them where the call crosses nothing, at
any function value the copy reaches, `copy f` of one whose captures cross among them, and at an actor's reference, which the receiver gets naming the same
actor, as a reference that crosses names it *(assumed, §6: nothing added at a function value, Q24's cost read for a word at the argument, as rustc reads a closure's
`Send` through its captures; nothing added at an actor's reference, as Rust's clone of a channel's
`Sender` is another sender to the same channel, `impl<T> Clone for Sender<T>`,
`library/std/src/sync/mpsc.rs` of Rust 1.100.0, line 646)*. `copy` before a value that crosses
with no word, an immutable class instance or a number among them, stands and does what it does
before a mutable class instance, the receiver getting a deep copy *(assumed, §6: as Rust clones a
value that is `Send` already, the word being no error)*. `move` before such a value stands and ends
the variable it names alone, since a value that crosses with no word joins no region, so that
`s = x.Name` written before `move x` stays alive; a `move` of a field of `this`, of a static or of a
parameter not written `move` stays `move-reachable`, as Swift's SE-0414 tracks no `Sendable` value
and Rust's move of a `Send` binding ends that binding alone (§6, the owner, 2026-11-15, #519,
comment 6897778812, Q2, "Swift/Rust: that name only").

A use of a variable after its `move` is the compile error `used-after-move`, at the use, naming the
line of the `move`, on every path on which the value may have moved, as rustc reports a use after a
move at the use (E0382) (§6, the owner, 2026-11-15, #519, comment 6896192351, Q1, "Rust: at the
use, own id"), and its id has its row in the table of errors, as `move-reachable`'s and
`move-at-call`'s do, since cases of this rule expect them (§6). A bare `throw` in an arm of `else` is a use of the
arm's name (O7), so that one after a `move` that may have ended the name is `used-after-move` at the `throw` (§5,
§6, the owner, 2026-11-20, issue 596, comment 7051500690). Two paths to one block of a value that crosses, or of a
value after `copy`, arrive as two paths to one copy, as Rust's clone of an `Rc` graph keeps its
sharing, so that a copy is never larger than its source and a mutable graph keeps its shape (§6,
the owner, 2026-11-15, #519, comment 6896192351, Q2, "Sharing kept"). The copies one call makes,
of a message's arguments, of a `spawn`'s or of the arguments after `copy` at any call, share one
memo, so that two arguments that reach one block the copies copy arrive as two paths to one copy, and a `weak`
reference in one argument points at the copy of its target where another argument's copy holds
that target strongly; a result has a memo of its own. Each argument is copied where it is
evaluated, left to right, so that a later argument that changes a block an earlier one copied
changes the source alone, and one that reaches that block again reaches its copy as it was made;
a later argument whose call waits lets other calls cross meanwhile, each in a memo of its own; a
block the memo holds lives until the call's last copy, and a call left before its last copy, by a
case a later argument propagates, ends its memo there. A crossing copies by the type of the
parameter the argument fills, so that `null` given to a `Box?` of a class with a `mut` field needs
`copy` as a `Box` would *(assumed, §6: the memo of the call, which Q2 reads at a call; the order,
which keeps arguments evaluated left to right; and the parameter's type, the slot the message
holds)*. A `weak` reference (R3)
inside a value that crosses points at the copy of its target where the copy holds that target
strongly, and is null on arrival otherwise, so that a tree with weak parent links crosses whole
(§6, the owner, 2026-11-15, #519, comment 6896192351, Q3, "Points at the copy"). A function value
whose lambda the checker does not see, a parameter, a field or a call's result, is read by its
written type, as a value of an interface's type is: it crosses only as a lambda the function that
sends it shows, and any other is `send-needs-word`, so that a callback received as a parameter is
never sent (§6, the owner, 2026-11-15, #519, comment 6896192351, Q5, "By written type")
*(assumed: that a lambda the function shows is one that is the argument itself or the one an
immutable local variable is declared with, a lambda inside an argument, a field of a construction
there, being read by its written type, and that the named function the answer names stands for no case,
since F11 gives a function value through a lambda only)*. `copy` before an argument is a deep copy wherever it is
written, a call that crosses nothing included, as Rust's `.clone()` stands anywhere (§6, the owner,
2026-11-15, #519, comment 6896192351, Q6, "Rust: deep copy anywhere"), save that a block the call's
memo holds is not copied again (comment 6896192351, Q2), so that an argument's copy of it shows the block as the first
copy found it *(assumed, §6: the memo of the call and the order of its copies)*. The sender makes the copy,
at the send; every class instance a crossing reaches is copied, so that identity does not survive
a crossing; the copy walks a work list and never recurses, so that a chain of a million links crosses on a
stack of 2 MiB; and a copy runs no program code, no `Text()` and no `equal by` *(assumed, §6: the
points comment 6895076937 takes as following from §6, which the answers left standing)*, but for the
hash of each key of a map whose keys reach a class instance, by identity, or by the fields its class compares by
content, `equal by`'s among them (§6, the owner, 2026-11-16, #519, comment 6923513534), which runs no program
code, since a class declares no `Equals` or `GetHashCode` (K5). Each actor
having its own heap, a crossing gives the receiver a value that no program
can tell from a copy in its heap, a big one that reaches no class instance shared by pointer under §6's share of a big value, which
stays decided and unbuilt (§6, the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share
big unbuilt") *(assumed, §6: the share read for a
block that reaches no class instance, since every class instance a crossing reaches is copied)*; a `copy` at a call that crosses nothing copies every block on a path to a class
instance that passes through no function value and no actor's reference, the instance among them, and shares by
its count every other block *(assumed, §6: the answer read for a call inside one heap)*. A `weak` reference makes the
block that holds it count as on a path for what such a copy copies, where a path that passes through no function value and
no actor's reference reaches that block, so that the block is copied, and its target is
copied only through a strong path: the reference is no path the copy follows, and points at its target's copy where the
call's copies hold that target strongly, and is null otherwise, as at a crossing *(assumed, §6: Q3 of comment 6896192351
read for a call that crosses nothing, the block that holds a `weak` slot counted as on a path for what is copied, the walk
stopping at a function value and at an actor's reference for it as for every other block)*. An argument after `copy` whose parameter is `weak`, at such a call, is a `weak` reference to
the argument's copy, which the caller holds as a temporary to the end of its statement, so that the callee reads that
copy whether or not another copy of the call holds it *(assumed, §6: a temporary held to the end of its statement, read
for the copy that `copy` makes)*. The copy of a map whose keys reach a
class instance, where it crosses or stands after `copy`, places each entry again under its key's copied hash
once every block the copy reaches is filled, by identity or by content as its class compares (K4, K5)
*(assumed, §6: that order, since a key's hash reads fields of instances the copy can fill after the key)*, so
that the copied map finds what the source found; the cost, in the option's words, "the hash of the key's fields,
and its `equal by`, run inside a copy, once per key", falls on such maps alone
(§6, the owner, 2026-11-16, #519, comment 6923513534, "Rehash each key (Recommended)") *(assumed, §6: that cost
read as the key's hash, over the fields its `equal by` names, which compares no two keys)*, as a Rust
`HashMap` collected from cloned pairs hashes each key it takes *(assumed, §6: the comparison)*. `copy`
and `move` are words before an argument alone, as `mut` is there (L12), `move` before a parameter's
type and inside a function type too, as `mut` is before a parameter's (§6, the owner, 2026-11-05,
issue 156, comment 6657251688), and names everywhere else,
so that `copy(x)` is the word before `(x)` and a variable named `copy` is called as `(copy)(x)` (§6,
the owner, 2026-11-13, #519, comment 6847932993, Q10, "Any order + contextual (Recommended)"); a
`copy` or a `move` followed by `[`, `-`, `*`, `&` or `with`, which go on from a name, is the name,
so that `copy[0]` reads an item of a list named `copy`, and so is one that nothing that begins an
expression follows, so that `f(copy)` passes a variable named `copy` and `f(copy.N)` reads its
field *(assumed, §6: as a name before what goes on from a name is read, which the answers did not
give)*.

A `move` of a value that needs a word to cross ends every path into the value it sends, a value
that crosses with no word ending the variable it names alone (§6, the owner, 2026-11-15, #519,
comment 6897778812, Q2), but a loop's own variable, whose loop takes its list (this rule, "A `for`
loop whose body moves its variable"): the checker tracks, in each function, every
variable that reaches into the value, all of them dead after the `move`, and refuses the `move`
with `move-reachable` where the value may be reached from outside the function (§6, the owner,
2026-11-03, issue 156, comment 6603205575). A function's values form regions, as in Swift's
SE-0414: a parameter written `move` starts a region of its own, and every other parameter, `this`
with its fields, the captured variables and the statics form one region outside the function
*(assumed, §6: the point of comment 6897444564 that the answers left standing)*. The top-level code
is read as a function whose variables are its own, since F16 keeps them from the top-level
functions, so that they are no statics, and `move` before a value that names no place,
`f(move Cart(1))`, stands where the word fits the parameter (§6, comment 6897778812, Q3; comment 6657251688, Q3), and ends nothing, the call taking the value it made *(assumed, §6: the
top-level code read as a function, and a `move` that ends nothing, which no answer named)*. A call merges the regions of all its arguments not written `copy` or `move`, written `mut` or not,
and its result's, as SE-0414 does, since `F(a, b)` can store `b` in a `mut` field of `a`, and an argument that crosses
with no word merges nothing (§6, 2026-11-15, Q2) *(assumed, §6: the point of comment 6897444564 that the answers left standing,
and for an argument written `copy` or `move`, whose copy is new and whose `move` ends its region at the call, the checker's
reading, which no answer named)*. A lambda joins the regions of what it
captures, so that `f = () => m.N; send a.T(move m); f()` is `used-after-move` at `f()`, and a
`move` of a captured variable inside a lambda is `move-reachable` *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A `match` binding joins its
subject's region, as a field read does, and each arm is a branch *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. `p with { A = q }` is read
as a call that takes `p` and `q`, its result joining both regions *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. An `else` binding joins the region of the call it
takes a case of, as a `match` binding joins its subject's; an arm that ends in a block gives the
region of the block's last expression, as rustc reads a block's tail; an operator's value joins its
operands' regions; an actor's call and a `spawn` give a region of their own, since what they give
crossed; an assignment to a field or an item joins the value's region with the instance's or the
list's, a field of `this` with the region outside the function; the instance a method is called on
joins the regions of its call, as an argument does; a field read of a value the function made, a
construction or a call whose arguments and receiver merge no region, having none or each crossing
with no word or written `copy` or `move`, stays in that value's own region; and a value a round of
a loop makes is new in each round, what earlier rounds made there sharing one region of their own,
apart from the round's new value *(assumed, §6: the checker's readings, which no answer named, comment 6897444564 giving a `match`
binding, a call and a field read)*. Branches unite what they
moved: after an `if`, a `match`, `&&` or `||`, a use is `used-after-move` where any path moved the
value, and the error names every `move` line that reaches the use *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. In a loop, a `move x` in a
body that does not assign `x` again before the round ends is `used-after-move` at that `move`,
moved there in an earlier round; an assignment makes a variable live again, and a variable
declared in the body is new each round *(assumed, §6: the point of comment 6897444564 that the answers left standing)*, and so are the variables the loop's head declares *(assumed, §6: the checker's reading, which no answer named)*. `move p.A` and `move xs[0]` stand and end `p` or `xs`
with its whole region, as Swift's regions do, which track no single field, and not as Rust's
partial move; a `move` of a field of `this`, of a static or of a parameter not written `move` is
`move-reachable`, and so is the `move` of a variable that aliases one of them *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A use is any
read of the variable or of a variable in its region, an argument after it in the same call among
them, `s.Take(move c, c.N)`; an assignment to the variable itself is no use, and one error stands
for each variable and the `move`s it names (E1) *(assumed, §6: the point of comment 6897444564 that the answers left standing)*.

A `for` loop whose body moves its variable takes its list, as Rust's `for c in carts` takes a list
it owns: the list is dead after the loop, a use of it there being `used-after-move`, naming the
line of the `move`, and a list held outside the function, a field of `this` or a parameter not
written `move`, stays `move-reachable` (§6, the owner, 2026-11-15, #519, comment 6897778812, Q1,
"Rust: loop takes list"). A loop whose body moves its variable, whose value crosses with no word, a
number or a string among them, takes its list too, as Rust's `for n in nums` takes a `Vec<i32>`
(§6, the owner, 2026-11-17, #561, comment 6945857315, Q1, "The loop takes the list"). A parameter written `move` takes an argument written `move`, or one
written `copy`, since its copy is new, as a parameter written `sending` takes any value in a region
of its own in Swift (SE-0430); no word before such an argument is `send-needs-word` at a message,
which U16 reads first, and `move-at-call` at a plain call (§6, the owner, 2026-11-15, #519, comment
6897778812, Q3, "Swift/Rust: copy fits"), and so is no word at a message before such a parameter of
a value that crosses with no word, where `send-needs-word` says nothing *(assumed, §6: as at a plain
call)*. `move` before an argument of a parameter not written `move`, at a call that is no message,
is `move-at-call`, before what its caller holds too, the call's word checked first (§6, the owner,
2026-11-05, issue 156, comment 6657251688, Q3) *(assumed, §6: the readings of comment 6666386552 and
of comment 6668638888)*, at a call outside a statement of a `parallel` block, inside which `move`
hands the instance to the statement (§6, "Parallel work") *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A lambda's parameter whose type is
not written takes `move` from the function type the lambda is given for, a lambda whose parameter's
type is written without `move` fits such a type and holds the parameter as its caller's, and one written
`move` where that type has none is `type-mismatch` (§6, the owner, 2026-11-05, issue 156, comment
6657251688, Q4). A parameter takes one of `mut` and `move`, both of them, in either order, being `syntax`, `move`
standing before its type, a type
named `move` before a name is read as the word, and a parameter of a C function's type (R17) takes no
`move`, which is `syntax`, since C code keeps no count *(assumed, §6: as `mut` and `move` are words
before an argument, which the round did not ask)*. On one thread a `move` into a parameter copies
nothing, and of a value whose place has an empty value, an address or a string, which the `move`
empties, changes no count: the moved place's count becomes the parameter's, and a variable moved on
some paths alone is let go of through a null test at its end, as rustc keeps a drop flag (§6, the
owner, 2026-11-15, #519, comment 6895076937) *(assumed, §6: the point of comment 6897444564 that the answers left standing)*; a value whose place a `move` leaves as it was, inline data,
a fat class reference or a function value, has each counted block it holds counted once more
*(assumed, §6: the build's reading, which no answer named)*. A `move` into a message or a `spawn`
copies the value into the receiver's heap, as an argument with no word crosses, a big value shared
by pointer under §6's share of a big value, which stays decided and unbuilt and which no program
tells from a copy, and lets go of the original, which frees what was copied, and into a `spawn` whose policy can restart the supervisor keeps a second
copy (U22) *(assumed, §6: on comment 6902210646, Q5, "Copy all, share big unbuilt", the answer given
after comment 6895076937, which gave the moved count to "the message's or the parameter's" place and
which Q5 narrows to a parameter)*.

A pointer (R10) and a view of an atomic type (R19) cross into no actor: each is the compile error
`pointer-crosses`, as Rust refuses a raw pointer, `*mut T` being no `Send` (§6, the owner,
2026-11-17, #572, comment 6961064484, Q1, "Refuse with an id (Recommended)"), so that a program that
shares a word between actors passes its address as a `long` and makes the view again on the other
side, in a `raw` block (§6, the cost of that answer). The error stands at its argument, with `copy`,
with `move` or with no word, as no clone or move of a `*mut T` makes it `Send`, and so it does for a
value that reaches one, a collection, a `data` value or a class instance among them, as rustc reads
`Send` through a struct's fields, and for a lambda the sending function shows that captures one; a
message's result that reaches one is that error at the line of the call, as rustc asks `Send` of a
task's output; and of a message's or a constructor's arguments the first that does not cross is
reported, `pointer-crosses` standing in the stage of `send-needs-word` (U16), and before it in an
argument that reaches both a pointer and a mutable class instance, since no word makes it cross
*(assumed in §6: the id, the words, what a value reaches, the result and the stage)*.

Case: [actors/relay.kz](../corpus/actors/relay.kz)

Case: [actors/union-result.kz](../corpus/actors/union-result.kz)

Case: [actors/this-reference.kz](../corpus/actors/this-reference.kz)

Case: [actors/immutable-instance.kz](../corpus/actors/immutable-instance.kz)
```kurz
class Order(string Customer, int Items)

actor Shop {
    mut List<Order> orders = []

    pub void Place(Order o) {
        orders.Add(o)
    }

    pub Order First() => orders[0]
}

order = Order("Ann", 3)
s = spawn Shop()
s.Place(order)
f = s.First()
print("{f.Customer} {f.Items}")
// `Order` has no `mut` field and no `weak` one, so it compares by content (K4), and the copy of its copy equals it
print(f == order)
```

Case: [actors/references-in-list.kz](../corpus/actors/references-in-list.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

actor Hub {
    pub void Tell(List<Counter> all) {
        for c in all {
            send c.Add(1)
        }
    }
}

a = spawn Counter()
b = spawn Counter()
h = spawn Hub()
h.Tell([a, b, a])
print(a.Get() + b.Get())
```

Case: [actors/references-map-key.kz](../corpus/actors/references-map-key.kz)

Case: [actors/send-needs-word.kz](../corpus/actors/send-needs-word.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Keep(Cart c) {
        print(c.Items)
    }
}

cart = Cart(1)
s = spawn Shop()
s.Keep(cart)
```

Case: [actors/send-needs-word-interface.kz](../corpus/actors/send-needs-word-interface.kz)
```kurz
interface Shape {
    int Area()
}

class Square(int Side) : Shape {
    pub int Area() => Side * Side
}

actor Painter {
    pub void Paint(Shape s) {
        print(s.Area())
    }
}

p = spawn Painter()
p.Paint(copy Square(2))
```

Case: [actors/send-needs-word-function.kz](../corpus/actors/send-needs-word-function.kz)
```kurz
class Tally(mut int Count)

actor Runner {
    pub void Run(() => int f) {
        print(f())
    }
}

t = Tally(1)
r = spawn Runner()
r.Run(() => t.Count)
```

Case: [actors/send-lambda.kz](../corpus/actors/send-lambda.kz)
```kurz
class Rate(int Per)

actor Worker {
    pub int Run(() => int job) => job()
}

r = Rate(3)
k = 4
w = spawn Worker()
print(w.Run(() => r.Per + k))
```

Case: [actors/result-mutable.kz](../corpus/actors/result-mutable.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub Cart Open() => Cart(0)
}

s = spawn Shop()
c = s.Open()
print(c.Items)
```

Case: [actors/pointer-crosses.kz](../corpus/actors/pointer-crosses.kz)
```kurz
actor Keeper {
    pub void Keep(long* p) {
        print(1)
    }
}

mut long word = 5
k = spawn Keeper()
// a pointer crosses into no actor, with a word or with none (U12)
k.Keep(&word)
```

Case: [actors/copy-mutable.kz](../corpus/actors/copy-mutable.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        c.Items = c.Items + 1
        print("in the shop: {c.Items}")
    }
}

cart = Cart(1)
s = spawn Shop()
s.Add(copy cart)
print("at home: {cart.Items}")
```

Case: [actors/copy-shared-graph.kz](../corpus/actors/copy-shared-graph.kz)
```kurz
class Account(mut int Balance)
class Owner(string Name, Account Main, Account Savings)

actor Bank {
    pub string Deposit(Owner o) {
        main = o.Main
        main.Balance = main.Balance + 10
        return "{o.Main.Balance} {o.Savings.Balance}"
    }
}

joint = Account(5)
ann = Owner("Ann", joint, joint)
b = spawn Bank()
print(b.Deposit(copy ann))
print(joint.Balance)
```

Case: [actors/copy-weak-parent.kz](../corpus/actors/copy-weak-parent.kz)
```kurz
class Folder(string Name, List<Folder> Children, mut weak Folder? Parent)

actor Archive {
    pub string Path(Folder top) {
        child = top.Children[0]
        p = child.Parent
        if p != null {
            return "{p.Name}/{child.Name} {p == top}"
        }

        return "lost"
    }
}

docs = Folder("docs", List<Folder>(), null)
home = Folder("home", [docs], null)
docs.Parent = home
a = spawn Archive()
print(a.Path(copy home))
```

Case: [actors/copy-word-on-number.kz](../corpus/actors/copy-word-on-number.kz)
```kurz
actor Printer {
    pub void Show(int n, string s) {
        print("{n} {s}")
    }
}

k = 7
p = spawn Printer()
p.Show(copy k, copy "seven")
print(k)
```

Case: [actors/copy-at-call.kz](../corpus/actors/copy-at-call.kz)
```kurz
class Cart(mut int Items)

void Fill(Cart c) {
    c.Items = c.Items + 10
    print("filled: {c.Items}")
}

cart = Cart(1)
Fill(copy cart)
print("kept: {cart.Items}")
Fill(cart)
print("changed: {cart.Items}")
```

Case: [actors/copy-derived.kz](../corpus/actors/copy-derived.kz)
```kurz
class Cart(mut int Items) {
    pub virtual string Show() => "a cart: {Items}"
}

class OwnedCart(string Owner, int Start) : Cart(Start) {
    pub override string Show() => "{Owner}'s cart: {Items}"
}

actor Shop {
    pub void Add(Cart c) {
        c.Items = c.Items + 1
        print(c.Show())
    }
}

Cart cart = OwnedCart("Ann", 2)
s = spawn Shop()
s.Add(copy cart)
print("at home: {cart.Show()}")
```

Case: [actors/copy-map-instance-keys.kz](../corpus/actors/copy-map-instance-keys.kz)
```kurz
class Box(mut int N)
class Tag(string Name, Box Owner)
data Shelf(Tag First, Map<Tag, int> Counts)

int Found(Map<Box, string> known) {
    mut found = 0
    for e in known {
        if (known[e.Key] ?? "") == e.Value {
            found = found + 1
        }
    }

    return found
}

string ByBox(Map<Box, string> known, Box a, Box b) => "{known[a] ?? "none"} {known[b] ?? "none"}, {Found(known)} found"

string ByTag(Shelf s) {
    mut found = 0
    for e in s.Counts {
        if (s.Counts[e.Key] ?? 0) == e.Value {
            found = found + 1
        }
    }

    bob = Tag("bob", s.First.Owner)
    return "{s.Counts[s.First] ?? 0} {s.Counts[bob] ?? 0}, {found} found"
}

actor Hub {
    pub string Names(Map<Box, string> known, Box a, Box b) => ByBox(known, a, b)

    pub string Tags(Shelf s) => ByTag(s)

    pub string Moved(Map<Box, string> known) => "{Found(known)} found"
}

one = Box(1)
two = Box(2)
mut names = Map<Box, string>()
names[one] = "a"
names[two] = "b"
box = Box(3)
ann = Tag("ann", box)
mut counts = Map<Tag, int>()
counts[ann] = 3
counts[Tag("bob", box)] = 4
h = spawn Hub()
print(h.Names(copy names, copy one, copy two))
print(h.Tags(copy Shelf(ann, counts)))
mut kept = Map<Box, string>()
kept[Box(5)] = "e"
kept[Box(6)] = "f"
print(h.Moved(move kept))
print(ByBox(copy names, copy one, copy two))
print(ByTag(copy Shelf(ann, counts)))
```

Case: [actors/copy-two-arguments.kz](../corpus/actors/copy-two-arguments.kz)
```kurz
class Counter(mut int N)

actor Desk {
    pub string Both(Counter a, Counter b) {
        a.N = a.N + 10
        return "{a.N} {b.N}"
    }
}

c = Counter(1)
d = spawn Desk()
print(d.Both(copy c, copy c))
print(c.N)
```

Case: [actors/copy-weak-across-arguments.kz](../corpus/actors/copy-weak-across-arguments.kz)
```kurz
class Box(mut int N)
class Tag(weak Box? Of)

actor Shelf {
    pub string Look(Tag t, Box b) {
        b.N = b.N + 10
        o = t.Of
        if o != null {
            return "{o.N}"
        }

        return "gone"
    }
}

box = Box(1)
tag = Tag(box)
s = spawn Shelf()
print(s.Look(copy tag, copy box))
print(box.N)
```

Case: [actors/copy-weak-argument.kz](../corpus/actors/copy-weak-argument.kz)
```kurz
class Box(mut int N)
data Link(weak Box? To)

actor Shelf {
    pub string Look(weak Box? w, Link l, Box b) {
        b.N = 2
        v = w
        t = l.To
        if v != null && t != null {
            return "{v.N} {t.N} {v == b} {t == b}"
        }

        return "gone"
    }
}

box = Box(1)
s = spawn Shelf()
print(s.Look(copy box, copy Link(box), copy box))
print(box.N)
```

Case: [actors/copy-two-at-call.kz](../corpus/actors/copy-two-at-call.kz)
```kurz
class Cart(mut int Items)

string Both(Cart a, Cart b) {
    a.Items = a.Items + 10
    return "{a.Items} {b.Items}"
}

cart = Cart(1)
print(Both(copy cart, copy cart))
print(cart.Items)
```

Case: [actors/copy-weak-at-call.kz](../corpus/actors/copy-weak-at-call.kz)
```kurz
class Box(mut int N)
data Link(weak Box? To)

string Look(Box a, Link l, Box b, weak Box? w) {
    b.N = 2
    t = l.To
    v = w
    if t != null && v != null {
        return "{a.N} {t.N} {v.N} {a == b} {t == b} {v == b}"
    }

    return "gone"
}

box = Box(1)
print(Look(copy box, copy Link(box), copy box, copy box))
f = (Box a, Link l, Box b) => Look(a, l, b, a)
print(f(copy box, copy Link(box), copy box))
print(box.N)
other = Box(3)
print(Look(copy other, copy Link(other), box, copy box))
```

Case: [actors/copy-left-to-right.kz](../corpus/actors/copy-left-to-right.kz)
```kurz
class Cart(mut int Items)

int Bump(Cart c) {
    c.Items = c.Items + 10
    return c.Items
}

string Show(Cart c, int now) => "{c.Items} {now}"

cart = Cart(1)
print(Show(copy cart, Bump(cart)))
print(cart.Items)
```

Case: [actors/copy-memo-keeps-block.kz](../corpus/actors/copy-memo-keeps-block.kz)
```kurz
class Box(mut int N)
class Holder(mut Box B)

Box Swap(Holder held) {
    held.B = Box(3)
    return Box(2)
}

string Pair(Holder a, Box b) => "{a.B.N} {b.N}"

h = Holder(Box(1))
print(Pair(copy h, copy Swap(h)))
print(h.B.N)
```

Case: [actors/copy-left-early.kz](../corpus/actors/copy-left-early.kz)
```kurz
data Missing

class Box(mut int N)
class Holder(mut Box B)
class Tag(weak Box? Of)

Box | Missing Lose(Holder held) {
    held.B = Box(2)
    return Missing
}

string Pair(Box a, Box b) => "{a.N} {b.N}"

string | Missing Try(Holder held) {
    return Pair(copy held.B, copy Lose(held))
}

h = Holder(Box(1))
tag = Tag(h.B)
match Try(h) {
    string s => print(s)
    Missing => print("missing")
}
print(tag.Of == null)
```

Case: [actors/move-mutable.kz](../corpus/actors/move-mutable.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        c.Items = c.Items + 1
        print("in the shop: {c.Items}")
    }
}

cart = Cart(1)
s = spawn Shop()
s.Add(move cart)
print("sent")
```

Case: [actors/move-parameter.kz](../corpus/actors/move-parameter.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print("took {c.Items}")
}

cart = Cart(1)
Take(move cart)
other = Cart(2)
Take(copy other)
print("kept {other.Items}")
```

Case: [actors/move-in-loop-reassigned.kz](../corpus/actors/move-in-loop-reassigned.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print("took {c.Items}")
}

mut cart = Cart(0)
for i in 1..3 {
    Take(move cart)
    cart = Cart(i)
}

print("last {cart.Items}")
```

Case: [actors/move-on-one-branch.kz](../corpus/actors/move-on-one-branch.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print("took {c.Items}")
}

void Maybe(bool give, int n) {
    c = Cart(n)
    if give {
        Take(move c)
    }
}

Maybe(true, 1)
Maybe(false, 9)
Maybe(true, 2)
```

Case: [actors/move-loop-takes-list.kz](../corpus/actors/move-loop-takes-list.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print("in the shop: {c.Items}")
    }
}

s = spawn Shop()
carts = [Cart(1), Cart(2), Cart(3)]
for c in carts {
    if c.Items != 2 {
        s.Add(move c)
    }
}

print("sent")
```

Case: [actors/move-crossing-value.kz](../corpus/actors/move-crossing-value.kz)
```kurz
class Cart(mut int Items, string Name)

actor Shop {
    pub void Add(string name) {
        print("in the shop: {name}")
    }
}

cart = Cart(1, "apples")
s = spawn Shop()
label = cart.Name
s.Add(move label)
print("kept {cart.Name}")
```

Case: [actors/used-after-move.kz](../corpus/actors/used-after-move.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

cart = Cart(1)
s = spawn Shop()
s.Add(move cart)
print(cart.Items)
```

Case: [actors/used-after-move-branch.kz](../corpus/actors/used-after-move-branch.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print(c.Items)
}

void Maybe(bool give) {
    c = Cart(1)
    if give {
        Take(move c)
    }
    print(c.Items)
}

Maybe(true)
```

Case: [actors/used-after-move-loop.kz](../corpus/actors/used-after-move-loop.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print(c.Items)
}

cart = Cart(1)
for i in 0..<3 {
    Take(move cart)
}
```

Case: [actors/used-after-move-region.kz](../corpus/actors/used-after-move-region.kz)
```kurz
class Line(mut int Count)
class Cart(Line First)

void Take(move Cart c) {
    print(c.First.Count)
}

void Run() {
    cart = Cart(Line(1))
    first = cart.First
    Take(move cart)
    print(first.Count)
}

Run()
```

Case: [actors/used-after-move-list.kz](../corpus/actors/used-after-move-list.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

s = spawn Shop()
carts = [Cart(1), Cart(2)]
for c in carts {
    s.Add(move c)
}
print(carts.Count)
```

Case: [actors/move-reachable-field.kz](../corpus/actors/move-reachable-field.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

class Owner(Cart Mine, Shop To) {
    pub void Give() {
        To.Add(copy Mine)
        To.Add(move Mine)
    }
}

Owner(Cart(1), spawn Shop()).Give()
```

Case: [actors/move-reachable-parameter.kz](../corpus/actors/move-reachable-parameter.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

void Give(Shop to, Cart cart) {
    to.Add(move cart)
}

Give(spawn Shop(), Cart(1))
```

Case: [actors/move-reachable-capture.kz](../corpus/actors/move-reachable-capture.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

cart = Cart(1)
give = (Shop s) => s.Add(move cart)
give(spawn Shop())
```

Case: [actors/move-reachable-merged.kz](../corpus/actors/move-reachable-merged.kz)
```kurz
class Cart(mut int Items)
class Basket(mut List<Cart> Carts)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

void Fill(Basket basket, Shop to) {
    mut carts = [Cart(1)]
    c = Cart(2)
    carts.Add(c)
    basket.Carts = carts
    to.Add(move c)
}

Fill(Basket([]), spawn Shop())
```

Case: [actors/move-reachable-list.kz](../corpus/actors/move-reachable-list.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Add(Cart c) {
        print(c.Items)
    }
}

void Send(Shop to, List<Cart> carts) {
    for c in carts {
        to.Add(move c)
    }
}

Send(spawn Shop(), [Cart(1)])
```

Case: [actors/move-at-call.kz](../corpus/actors/move-at-call.kz)
```kurz
class Cart(mut int Items)

void Take(move Cart c) {
    print(c.Items)
}

cart = Cart(1)
Take(cart)
```

Case: [actors/move-at-call-plain.kz](../corpus/actors/move-at-call-plain.kz)
```kurz
class Cart(mut int Items)

void Show(Cart c) {
    print(c.Items)
}

cart = Cart(1)
Show(move cart)
```

Case: [actors/first-error-move.kz](../corpus/actors/first-error-move.kz)
```kurz
class Cart(mut int Items)

actor Shop {
    pub void Take(move Cart c, mut int n) {
        n = c.Items
    }
}

void Give(Shop to, Cart cart) {
    mut n = 0
    send to.Take(move cart, mut n)
}

Give(spawn Shop(), Cart(1))
```

Case: [actors/used-after-move-capture.kz](../corpus/actors/used-after-move-capture.kz)
```kurz
class Cart(mut int N)

actor Shop {
    pub void Take(Cart c) {
        print(c.N)
    }
}

s = spawn Shop()
m = Cart(1)
f = () => m.N
send s.Take(move m)
print(f())
```

Case: [outcomes/else-throw-after-move.kz](../corpus/outcomes/else-throw-after-move.kz)
```kurz
data Missing

int | List<int> | Missing Plain(int id) {
    if id == 1 {
        return [id, 7, 9]
    }

    if id == 2 {
        return Missing
    }

    return id
}

int Use(move List<int> l) => l.Count

int Rethrown(int id) {
    v = Plain(id) else {
        List<int> n => {
            c = Use(move n)
            print("used {c}")
            throw
        }
        Missing => 0
    }

    return v
}

print(Rethrown(3))
print(Rethrown(1))
```

Case: [outcomes/else-throw-after-move-other-path.kz](../corpus/outcomes/else-throw-after-move-other-path.kz)
```kurz
data Missing

int | List<int> | Missing Plain(int id) {
    if id == 1 {
        return [id, 7, 9]
    }

    if id == 2 {
        return Missing
    }

    return id
}

int Use(move List<int> l) => l.Count

int Rethrown(int id, bool give) {
    v = Plain(id) else {
        List<int> n => {
            if give {
                return Use(move n)
            } else {
                throw
            }
        }
        Missing => 0
    }

    return v
}

print(Rethrown(3, true))
print(Rethrown(1, true))
print(Rethrown(1, false))
```

### U13 (decided, §6) The end of the program drops what is queued

When the top-level code ends, a message that runs in an actor it spawned runs to its end, and the
messages still queued behind it are dropped, since the end of the root actor stops its children
after their current message (§6, the owner, 2026-11-13, #519, comment 6847932993, Q16, and the owner, 2026-11-14, #519, comment 6880404917,
Q8, "All nine leans (Recommended)"). The end of any actor stops the actors it spawned so, after
their current message, their queued messages dropped, so that the end of the top-level code reaches
every actor of the machine (§6, the owner, 2026-10-30, "With its supervisor", and the owner,
2026-11-13, #519, comment 6847932993, Q16). An end stops them from the last slot of the table down
(U2), so that the callers that wait on them wake with `actor-gone` in that order *(assumed: §6, as the
actors the build kept before the table were ended newest first, which this order keeps where no slot
was taken again)*. A message parked in a wait when the top-level code ends
is no current message that holds that end, and is dropped with the process, as a queued one is, its lines after the wait never running, as tokio
drops its tasks when its runtime shuts down (§6, the owner, 2026-11-15, #519, comment 6892835500, Q5, "Rust:
dropped").

No case: for the end of the top-level code, since which message runs when it ends is the scheduler's to
say where actors run on more than one thread, so that a case's output would hang on it; the end of a
crashed supervisor's children has the case below.

Case: [actors/crash-children-end.kz](../corpus/actors/crash-children-end.kz)
```kurz
actor Child(int N) {
    pub void Tick(int t) {
        print("child {N} tick {t}")
    }
}

actor Parent {
    mut Child? kid = null

    pub void Make() {
        kid = spawn Child(1)
    }

    pub void Boom() {
        throw "parent fails"
    }

    pub Child? Kid() => kid
}

p = spawn Parent()
p.Make()
kid = p.Kid()
if kid != null {
    send kid.Tick(1)
    send p.Boom()
    send kid.Tick(2)
}

print(p.Kid() == null)
```

### U14 (decided, §6) `spawn` and `send` before what is not theirs

`spawn` before a name and its argument list whose name is declared as no actor, a class, a data
type, an interface, an enum, a flags type, a function or a variable, is `type-mismatch`, a name that
nothing declares being `unknown-name` (V10), and `send` before what is no call of an
actor's method is `syntax` (E4), as Rust gives a type error (E0277) for a value that is not what
`tokio::spawn` takes (§6, the owner, 2026-11-14, #519, comment 6880404917, Q3, "All nine leans (Recommended)"). `spawn Name(...)` on its own, its reference dropped, is
no statement and is `syntax`, as a value on its own is (F5), since nothing can send to an actor that
no reference reaches (§6, the same comment, Q21, "All five leans (Recommended)"). What follows
`spawn` that is no name, its type arguments written or not (U2), and its argument list,
`spawn Box`, `spawn x.Make()` or `spawn 3`, is `syntax` (E4), as U2's form gives, `spawn Box` of
a class among them, its form read before its name (U16). After an actor's name, its type arguments
(U2): a count that does not match its type parameters is T19's count, `syntax` (T35), of the
stage of the name (U16), and type arguments neither written nor inferable are `cannot-infer`
(T27), said only where every argument reads clean, an argument's own error standing alone, as rustc
reports no "type annotations needed" once the body has another error (§6, the owner, 2026-11-15,
issue 519, comment 6892275110, item 1, option (a), "Argument's error only").

Case: [actors/spawn-not-actor.kz](../corpus/actors/spawn-not-actor.kz)
```kurz
class Box(int Size)

b = spawn Box(1)
print(b.Size)
```

Case: [actors/send-not-call.kz](../corpus/actors/send-not-call.kz)
```kurz
void Show(int n) {
    print(n)
}

send Show(1)
```

Case: [actors/spawn-alone.kz](../corpus/actors/spawn-alone.kz)
```kurz
actor Greeter {
    pub void Hello() {
        print("hello")
    }
}

spawn Greeter()
```

Case: [actors/first-error-spawn-without-arguments.kz](../corpus/actors/first-error-spawn-without-arguments.kz)
```kurz
class Box(int Size)

b = spawn Box
print(b.Size)
```

### U15 (decided, §6) A message sent inside a message

On one machine, a message that an actor sends while it runs a message stands in its receiver's
inbox before every message that the caller of the running one sends after the running message's
method returned, which a caller that waits sees as its call returning, as a
queue keeps the order in which its items were put in; across machines only the order of U5 holds
(§6, the owner, 2026-11-14, #519, comment 6880404917, Q18, "All five leans (Recommended)").

Case: [actors/relay.kz](../corpus/actors/relay.kz)

Case: [actors/chain-send-waits.kz](../corpus/actors/chain-send-waits.kz)
```kurz
actor Store(Checker Check) {
    mut int saved = 0

    pub int Save(int n) {
        saved = n
        valid = Check.Valid(this)
        print("saved")
        return valid
    }

    pub int Load() {
        print("load")
        return saved
    }

    pub void Note() {
        print("note")
    }
}

actor Checker {
    pub int Valid(Store store) {
        send store.Note()
        return store.Load() * 10
    }
}

c = spawn Checker()
s = spawn Store(c)
v = s.Save(4)
print(s.Load())
print(v)
```

Case: [actors/send-own-method.kz](../corpus/actors/send-own-method.kz)

Case: [actors/this-reference.kz](../corpus/actors/this-reference.kz)

Case: [actors/references-in-list.kz](../corpus/actors/references-in-list.kz)

### U16 (decided, §6) One error for a construct that breaks two rules

A construct of this chapter, `spawn`, a call of an actor's name, a call through an actor's
reference or `send`, that breaks two rules, one of them this chapter's, is one compile error, the
first in the order the construct is read: its form, the name after `spawn`, whom the call reaches,
its arguments, then its result. So `syntax` comes before every other id; `spawn-required`, U14's
`type-mismatch` and `unknown-name`, and `not-visible` come before what its arguments break; and
`send-result` comes last, as E1 holds a case to one error and reads a type's name before its
arguments, so that `unknown-name` comes after `syntax` and before what the arguments break (§6,
the owner, 2026-11-14, #519, comment 6882624606, Q27, "All six leans (Recommended)"). In the
stage of its arguments an argument's own error comes first, a `type-mismatch` among them, then
`send-needs-word` (U12), then `mut-in-message` (U9, U11), as rustc reports a type that is not
`Send` (E0277) in its type check and its borrow check skips a body with a type error (§6, the
owner, 2026-11-14, #519, comment 6884160133, item 6, option (a), "Rust's order"). `move-reachable`
of an argument written with `move` (§6, U12) comes after `send-needs-word` too, and so do
`move-at-call` of an argument, the word that fits a parameter written `move` being §6's (the owner,
2026-11-15, #519, comment 6897778812, Q3), and `used-after-move` of an argument after a `move` in
the same call *(assumed, §6: those two places, which the round of comment 6897444564 did not
ask)*, and the four, `move-reachable`, `move-at-call`, `used-after-move` and `mut-in-message`,
stand by argument order in that stage, the error of the argument written first coming first, as
rustc's borrow check sorts its errors by their place (§6, the owner, 2026-11-15, issue 519,
comment 6891937666, item 4, option (a), "Rust: by argument order", which named `move-reachable` and
`mut-in-message`); on one argument the checker reads `used-after-move` first, then `move-at-call`,
then `move-reachable`, the call having the one error *(assumed, §6: the checker's reading, which no
answer named)*. `mut-in-message` stands at the
first argument the call writes that fills a `mut` parameter, and on one argument `mut-in-message` and `move-at-call` or
`move-reachable` never meet, an argument written with `move` that fills a `mut` parameter being `mut-at-call` (M7), its own error
*(assumed: as §6 marks it)*, while an argument written `mut` whose variable a `move` ended before it, which fills a `mut`
parameter and reads that variable, has both `used-after-move` and `mut-in-message` at its line, in that order *(assumed, §6:
the checker's reading, which no answer named)*. An argument's own error
stands alone before U14's `cannot-infer` of `spawn`'s type arguments, which is said only where every
argument reads clean (§6, the owner, 2026-11-15, issue 519, comment 6892275110, item 1, option (a),
"Argument's error only"), one with no error of its own and no `send-needs-word`, so that
`cannot-infer` comes after `send-needs-word` and before `move-reachable`, `move-at-call`,
`used-after-move` and `mut-in-message`, each standing alone, as rustc's borrow check skips a body
whose type check failed *(assumed: as §6 marks it)*. A declaration
in an actor that breaks two rules, one of them this chapter's, is the error of the word read
first, so that `pub static mut int n = 0` is `actor-field-public` (U8) and not `static-state`
(U1), and `static mut Nope x = 0` and `static mut int n = "a"` are `static-state` and not the
type's `unknown-name` (V10) or the initial value's `type-mismatch`, as Q27's order of reading
holds of a declaration as of a call (§6, the owner, 2026-11-14, #519, comment 6884160133, item 7,
option (a), "word order").

Case: [actors/first-error-send-as-value.kz](../corpus/actors/first-error-send-as-value.kz)

Case: [actors/first-error-spawn-without-arguments.kz](../corpus/actors/first-error-spawn-without-arguments.kz)

Case: [actors/first-error-spawn-required.kz](../corpus/actors/first-error-spawn-required.kz)

Case: [actors/first-error-mut-in-message.kz](../corpus/actors/first-error-mut-in-message.kz)

Case: [actors/first-error-move.kz](../corpus/actors/first-error-move.kz)

### U17 (decided, §6) A waiting actor takes the calls of its chain

Every call through a reference and every `send` carries a chain ID: each call that waits and each
`send` of the top-level code starts a chain of its own, and one made inside a message carries that
message's chain *(assumed in §6, as a chain's deadline is the one of the call at its root)*. An actor
that waits inside a chain lets in the calls that wait and belong to the same chain, so that
`A.Save`, waiting on `B.Check`, which calls `A.Load`, runs `A.Load` as a nested call; every other
message waits in the inbox, a `send` of the chain among them, which runs after the message it was
sent from (§6, "Deadlocks", layer 1; the owner, 2026-11-14, #519, comment 6880404917, Q13, "All
eight leans (Recommended)"). Such a call runs on a stack of its own from the pool, as any message
starts, and the parked message resumes once the calls above it end (§6, the owner,
2026-11-15, #519, comment 6892835500, Q6, "Own pooled stack"). A call that waits gets in where, as
it is queued, the newest message its target has started and not ended, running or parked in a wait,
carries its chain, so that a call that a message makes through a reference to its own actor gets in
too *(assumed in §6)*. A path of calls that wait that comes back to an actor type on it, which two chains can enter at once, is
U56's compile error, and a call back to the caller that waits makes no edge of it (U57) (§6, the owner, 2026-11-20, #519, comment 7051071630, T40 (c)). An actor under construction takes no
message until its `spawn` returns, one of its chain neither *(assumed in §6)*, and an actor whose
restart runs its constructor takes no call of the restart's chain, which waits in its inbox for the
constructor's end (U22; §6, the owner, 2026-11-20, #599, comment 7051501153, "Option 1"). The chain ID is 16 bytes,
drawn at random from a generator of its thread that the system's randomness seeds once, and a system
that gives no randomness when a thread starts its first chain ends the program with `random-failed`,
as a thread's first map does (M9) *(assumed in §6)*.

Case: [actors/callback.kz](../corpus/actors/callback.kz)
```kurz
actor A(B Other) {
    mut int saved = 0

    pub int Save(int n) {
        saved = n
        return Other.Check(this) + 1
    }

    pub int Load() => saved
}

actor B {
    pub int Check(A x) {
        return x.Load() * 10
    }
}

b = spawn B()
a = spawn A(b)
print(a.Save(4))
```

Case: [actors/callback-chain.kz](../corpus/actors/callback-chain.kz)
```kurz
actor Store(Checker Check) {
    mut int saved = 0

    pub int Save(int n) {
        saved = n
        return Check.Valid(this) + 1
    }

    pub int Load() => saved
}

actor Checker(Verifier V) {
    pub int Valid(Store store) => V.Verify(store) * 10
}

actor Verifier {
    pub int Verify(Store store) => store.Load() * 10
}

v = spawn Verifier()
c = spawn Checker(v)
s = spawn Store(c)
print(s.Save(4))
```

Case: [actors/waits-one-message.kz](../corpus/actors/waits-one-message.kz)
```kurz
actor Clock {
    pub int Now() => 7
}

actor Worker(Clock C) {
    pub void Slow() {
        print("slow starts")
        t = C.Now()
        print("slow ends {t}")
    }

    pub void Fast() {
        print("fast")
    }

    pub void Last() {
        print("last")
    }
}

c = spawn Clock()
w = spawn Worker(c)
send w.Slow()
send w.Fast()
w.Last()
```

Case: [actors/chain-send-waits.kz](../corpus/actors/chain-send-waits.kz)

Case: [actors/call-self-through-this.kz](../corpus/actors/call-self-through-this.kz)

Case: [actors/crash-waiter-chain.kz](../corpus/actors/crash-waiter-chain.kz)
```kurz
actor B {
    pub int Get(int n) {
        if n == 0 {
            throw "zero"
        }

        return n
    }
}

actor A(B Other) {
    pub void Ask(int n) {
        print("asked {Other.Get(n)}")
    }

    pub int Ping() => 1
}

b = spawn B()
a = spawn A(b)
send a.Ask(0)
print(a.Ping())
print(b.Get(2))
```

Case: [actors/restart-then-callback.kz](../corpus/actors/restart-then-callback.kz)
```kurz
// after a restart whose constructor ended, `B.Check`'s call of `Load` gets into `A`, which waits in `Save` on that chain

actor B {
    pub int Check(A x) => x.Load() * 10
}

actor A(B Other) {
    mut int saved = 0

    pub int Save(int n) {
        saved = n
        return Other.Check(this) + 1
    }

    pub int Load() => saved

    pub void Fail() {
        throw "a fails"
    }
}

b = spawn B()
a = spawn A(b)
print(a.Save(4))
send a.Fail()
print(a.Save(5))
```

Case: [actors/vouch-same-chain.kz](../corpus/actors/vouch-same-chain.kz)
```kurz
// `vouch` says that the call reaches another `Node`, and it reaches the one that waits, in the same chain: it gets in (U17)
actor Node {
    pub int Size(Node other, int n) {
        if n == 0 {
            return 1
        }

        return vouch other.Size(other, n - 1) + 1
    }
}

root = spawn Node()
print(root.Size(root, 2))
```

### U18 (decided, §6) A wait of the top-level code that nothing can end

The top-level code's call that waits sleeps on the main thread until its reply is set, while the
worker threads run the actors' messages, each on a stack of its own (U1, U17, U35), as tokio's
`block_on` waits on the thread that calls it beside its runtime's workers (§6, the owner,
2026-11-17, #519, comment 6946657505, Q3, "tokio block_on (Recommended)"). A wait of it that finds no
actor left to run on any thread, no timer pending and nothing registered with the poller *(assumed in
§6)*, ends the process with
`error: deadlock at <file>:<n>`, the line of that call, and the exit code 1, so that two actors that
call each other from different chains and wait for each other do not hang silently
(§6, "Deadlocks"; the owner, 2026-11-15, #519, comment 6892812780, Q4, "deadlock at top-level
wait"). Layer 3 raises first in an actor whose wait closes a loop (U62), so that on one thread this end is reached by a
loop that a crash closes, whose waits then stand below the error line, which ends with a colon (U64) (§6, the owner,
2026-11-20, #519, comment 7051071630, T70 1; the owner, 2026-11-21, #519, comment 7060155980, T85 2). A call that waits in a function the top-level code called is the top-level code's, at its
own line. Where a call that an actor's start keeps out closes the loop, the error names that call's
line (U22) (§6, the owner, 2026-11-21, #599, comment 7060156109, Q6 1), and where more than one
such call closes it, the error line names the first place in source order and ends with a colon, and
each further place stands under it on a line `kept out at <file>:<n>`, before the trace (U22) (§6, the
owner, 2026-11-21, #599, comment 7067131310, several 2; comment 7067139953, the form 1), a place with
the count of its calls where more than one, at most 10 such lines and then `... and K more` (U22)
(§6, the owner, 2026-11-21, #599, comment 7067639209, the order 1, the same place 1, the cap 1). Two chains that cross through two different methods, as `deadlock.kz`'s do, `Left.Ask` waiting on `Right.Value` and
`Right.Ask` on `Left.Value`, are no loop of U56, whose loop comes back to a type on it (§6, the owner, 2026-11-20, #519, comment 7051071630, T40 (c)), so
that `deadlock.kz` reaches its run with no `vouch` (the owner, 2026-11-21, #519, comment 7060155980, T76 1). A deadline of a call that waits in a message is nothing left to run (U42) *(assumed: T6 of comment 6967625156)*. A loop that layer 3 does not see (U64) and that the top-level code never waits on stays until the end of the program,
where U13 drops it.

Case: [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz)
```kurz
// `Boss`'s `Go` waits on `Get`, queued behind `Fail` in `Worker`'s inbox; `Fail` crashes, and the block of `Worker`'s policy
// waits behind `Go`, so that a crash closes the loop and no wait does: layer 3 does not see it (U60), the top-level code's wait
// on `Go`, under its bound, finds nothing left to run (U18, U42), and its walk names the loop below the error line (U64)

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Get() => 5
}

actor Boss {
    pub void Go() {
        w = spawn Worker() on crash (x) {
            print("decided")
            restart
        }

        send w.Fail()
        print("got {w.Get()}")
    }
}

b = spawn Boss()
b.Go()
print("after")
```

Case: [actors/waitline-into-loop.kz](../corpus/actors/waitline-into-loop.kz)
```kurz
// `Worker`'s crash closes a loop of `Boss` and `Worker` that no wait closes (U64); `Asker`'s call of `Ping`, made after it,
// leads into that loop, which `Asker` is not in, so its wait parks and raises nothing (U61); the top-level code's wait on `Ask`
// finds nothing left to run (U18), and its walk meets the loop at `Boss`, whose wait starts the text below the error line (U64)

actor Clock {
    pub int Now() => 7
}

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Get() => 5
}

actor Boss {
    pub void Go() {
        w = spawn Worker() on crash (x) {
            print("decided")
            restart
        }

        send w.Fail()
        print("got {w.Get()}")
    }

    pub int Ping() => 1
}

actor Asker(Clock C) {
    pub void Ask(Boss boss) {
        t = C.Now()
        print("asks at {t}")
        print("pinged {boss.Ping()}")
    }
}

b = spawn Boss()
c = spawn Clock()
k = spawn Asker(c)
send b.Go()
k.Ask(b)
print("after")
```

### U19 (decided, §6) A crash kills the actor it happens in

An exception raised in a message, a `throw`, a check of the run-time table (chapter 12) and
`print-failed` among them, and on Linux and on Windows a stack overflow in the actor's own stack, is the crash of
the actor whose message raised it: no line of the actor's code runs after it and none of its frames
is unwound, as BEAM ends a process, and its supervisor, the actor whose code spawned it or the
top-level code, handles it (U22) (§6, "An exception kills only the actor it happens in"; the crash
path of §6, which switches the thread to the scheduler's stack). An exception in the top-level code
is the crash of the root actor, which ends the process with the exit code 1, the error line and the
trace, as before (§6). `allocation-failed`, `random-failed` and a fault outside a guard page end the
process wherever they happen (O11, M9), and so do, on the actor's stack too, a stack fault inside the
allocator, the scheduler or the copy of a crossing (O11) and, in a test build, a check of the runtime's
own code wherever it fires, as a stack fault inside the allocator or the scheduler ends it (chapter 12,
"Run-time errors").
On Windows the build ends the process at an actor's stack overflow, as the system ends it
(`compiler/README.md`). The words of a
policy, `on crash`, say what the supervisor does with the crash (U25 to U27), and an actor whose
`spawn` and type write none has the default, `on crash restart` (§6).

Case: [actors/crash-restarts-fresh.kz](../corpus/actors/crash-restarts-fresh.kz)
```kurz
actor Counter {
    mut int n = 0

    pub void Add(int k) {
        if k < 0 {
            throw "negative"
        }

        n = n + k
        print("added {k}, now {n}")
    }

    pub int Get() => n
}

c = spawn Counter()
c.Add(5)
send c.Add(-1)
c.Add(2)
print(c.Get())
```

Case: [actors/crash-overflow-restarts.kz](../corpus/actors/crash-overflow-restarts.kz)
```kurz
actor Deep {
    mut int calls = 0

    int Down(int n) => Down(n) + 1

    pub void Go() {
        print(Down(0))
    }

    pub int Count() {
        calls = calls + 1
        return calls
    }
}

d = spawn Deep()
print(d.Count())
send d.Go()
print(d.Count())
```

Case: [actors/crash-log-line.kz](../corpus/actors/crash-log-line.kz)
```kurz
actor Shop {
    pub void Buy(string item) {
        throw "no {item}"
    }

    pub string Name() => "shop"
}

s = spawn Shop()
send s.Buy("stock")
print(s.Name())
```

### U20 (decided, §6) A waiting caller gets `callee-crashed`

A call that waits on an actor that crashes before it answers, while it runs the call's message or a
message below it in the chain, raises `callee-crashed` at the line of that call, as Erlang's
`gen_server:call` exits its caller, and the cause stands in the callee's log line (U24) (the owner,
2026-11-15, #519, comment 6902210646, Q1, "Erlang: callee-crashed"). A caller whose callee crashed once the caller's own
deadline had passed raises `call-timeout` instead (U43) *(assumed: T7 of comment 6967625156)*. Either raise is the crash of the
waiting actor in its turn, and so on up the chain to the top-level code, whose crash ends the
process (§6, "The exception kills the caller as well"). A call whose caller crashed, where the callee
runs on another thread than its caller, runs to its end, and its result goes nowhere (§6); on one
thread a caller that waits crashes only through a call of its own chain, which its callee waits on, so that the callee then crashes with `callee-crashed` too.
A call that waits on an actor that ended, among them a child whose supervisor crashed (U21), one
its policy stopped (U25) and one past its limit (U26), raises `actor-gone` there (§6). An actor that ends frees its memory at once, and the slot its reference holds
then holds no actor of that number (U2), so that a call through the reference reaches no later actor of
that slot and raises `actor-gone` (§6, the owner, 2026-11-16, #519, comment 6919351399, "Erlang: id +
generation"), as a `send` does (U6).

Case: [actors/crash-waiter.kz](../corpus/actors/crash-waiter.kz)
```kurz
actor Calc {
    pub int Div(int n) => 10 / n
}

c = spawn Calc()
print(c.Div(2))
print(c.Div(0))
print("after")
```

Case: [actors/crash-waiter-chain.kz](../corpus/actors/crash-waiter-chain.kz)

Case: [actors/crash-child-gone.kz](../corpus/actors/crash-child-gone.kz)

Case: [actors/timeout-crashed.kz](../corpus/actors/timeout-crashed.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Worker {
    pub int Work() {
        Hold(300ms)
        throw "late"
    }
}

w = spawn Worker()
within 100ms {
    print(w.Work())
}
```

Case: [actors/timeout-crashed-early.kz](../corpus/actors/timeout-crashed-early.kz)
```kurz
// `Failing` crashes before `Caller`'s deadline, and `Holder`'s message, queued ahead of `Caller`, holds the one thread past
// it: the crash's time, not the time `Caller` reads it, is held to the deadline

// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Holder {
    pub void Busy() {
        Hold(300ms)
    }
}

actor Failing(Holder H) {
    pub int Fail() {
        send H.Busy()
        throw "early"
    }
}

actor Caller(Failing F) {
    pub void Go() {
        within 100ms {
            print(F.Fail())
        }
    }

    pub string Done() => "done"
}

h = spawn Holder()
f = spawn Failing(h)
c = spawn Caller(f) on crash (e) {
    match e.Reason {
        RuntimeError r => print(r.Kind)
    }

    restart
}
send c.Go()
print(c.Done())
```

Case: [actors/timeout-chain-nested.kz](../corpus/actors/timeout-chain-nested.kz)
```kurz
// `Ask` holds the thread past the 100 ms of the chain's root before it calls `S.Get()`: the top-level code's call raises
// `call-timeout`, its callee's crash set once its deadline had passed (U43), where a crash set before it is `callee-crashed`
// (U20); the time the nested call takes from its chain is shown by `timeout-chain-left.kz` (U37)

// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub int Get() => 1
}

actor Front(Store S) {
    pub int Ask() {
        Hold(150ms)
        return S.Get()
    }
}

s = spawn Store()
f = spawn Front(s)
within 100ms {
    print(f.Ask())
}
```

Case: [actors/timeout-crashed-before.kz](../corpus/actors/timeout-crashed-before.kz)
```kurz
// `Failing` crashes before the deadline of the call: `callee-crashed`, not `call-timeout`
actor Failing {
    pub int Fail() {
        throw "early"
    }
}

f = spawn Failing()
within 1s {
    print(f.Fail())
}
```

Case: [actors/timeout-carried.kz](../corpus/actors/timeout-carried.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Ticker {
    pub int Ping() => 1
}

actor Worker(Ticker T) {
    pub int Work() {
        Hold(200ms)
        return T.Ping()
    }
}

actor Front(Worker W) {
    pub int Go() {
        within 100ms {
            return W.Work()
        }
    }
}

t = spawn Ticker()
w = spawn Worker(t)
f = spawn Front(w)
print(f.Go())
```

Case: [actors/sleep-chain-crash.kz](../corpus/actors/sleep-chain-crash.kz)
```kurz
// `Boom`, a call of the chain, is let in while `Outer` sleeps, and its crash takes the sleep off the timers and wakes the caller
actor Helper {
    pub void Poke(Napper n) {
        print(n.Boom())
    }
}

actor Napper {
    pub int Outer(Helper helper) {
        send helper.Poke(this)
        Environment.Sleep(50ms)
        return 1
    }

    pub int Boom() {
        throw "boom"
    }
}

poker = spawn Helper()
a = spawn Napper()
print(a.Outer(poker))
```

Case: [actors/waitline-root.kz](../corpus/actors/waitline-root.kz)
```kurz
// `Right`'s `Ask`, which the top-level code waits on, gets `deadlock`: the top-level code's call raises `callee-crashed`, and
// the `deadlock` stands in `Right`'s log line (U62, U20)

actor Left {
    pub void Ask(Right other) {
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        print("right got {other.Value()}")
    }

    pub int Value() => 2
}

l = spawn Left()
r = spawn Right()
send l.Ask(r)
r.Ask(l)
print("after")
```

Case: [actors/waitline-pending-block.kz](../corpus/actors/waitline-pending-block.kz)
```kurz
// `Worker`'s `Fail` crashes while `Boss`'s `Go` waits on the clock, so the block of `Worker`'s policy waits behind `Go`;
// `Go`'s call of `Get` then waits on a child whose decision waits on `Go`, and gets `deadlock` at once (U60, U34); the
// top-level code, which waits on `Go`, gets `callee-crashed`

actor Clock {
    pub int Now() => 7
}

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Get() => 5
}

actor Boss(Clock C) {
    pub void Go() {
        w = spawn Worker() on crash (x) {
            print("decided")
            restart
        }

        send w.Fail()
        t = C.Now()
        print("now {t}")
        print("got {w.Get()}")
    }
}

c = spawn Clock()
b = spawn Boss(c)
b.Go()
print("after")
```

Case: [actors/waitline-got-in.kz](../corpus/actors/waitline-got-in.kz)
```kurz
// `A`'s `F` waits on `B`'s `G`, which waits on `C`'s `H`, whose call of `B`'s `K` gets in by its chain and runs above `G`; `K`
// waits on `D`, busy in `Y`, which waits on `C`, busy in `H`: the line goes on through `K`, the activation that runs the call
// that got in (U60), and `B`'s wait closes the loop and gets `deadlock` at `K`'s call (U62); under the default policy `C`'s `H`
// and then `A`'s `F` get `callee-crashed`, and so does the top-level code's call (U20)

actor E {
    pub int Delay() => 1
}

actor C {
    pub int H(B b) => b.K()

    pub int Z() => 3
}

actor B(D Last, C Mid) {
    pub int G() => Mid.H(this)

    pub int K() => Last.X()
}

actor D(E Slow) {
    pub void Y(C c) {
        t = Slow.Delay()
        print("y got {c.Z() + t}")
    }

    pub int X() => 4
}

actor A {
    pub int F(B b) => b.G()
}

ae = spawn E()
ad = spawn D(ae)
ac = spawn C()
ab = spawn B(ad, ac)
aa = spawn A()
send ad.Y(ac)
print(aa.F(ab))
```

### U21 (decided, §6) The inbox is kept, and the crashed instance's heap goes

A crashed actor's inbox is kept and its new instance serves it, in its order, the crashing message
dropped, as Akka's restart keeps the mailbox (§6, the owner, 2026-11-14, #519, comment 6880404917,
Q15, "All eight leans (Recommended)"); a call of the chain that waited on the crashed instance stands
before them *(assumed: the side list of U17 goes to the front of the inbox, which the record does not
say)*. The crashed instance's heap is freed whole but for its first page, which holds the record and the
state *(assumed: §6, a reading of the crash path the points did not name)*, and its stacks go back to the
pool, the messages of
its inbox copied into the new instance's heap first, as a crossing copies them (U12), and the
actor's reference names the same actor after the restart (U2) (§6; the taken points of comment
6899225864). A crash ends the actors the crashed instance spawned as an end does, after their
current message, their queued messages dropped (U13), and none of them restarts. A message whose
builder crashes at an argument that can raise after one that crosses leaves no copy of its arguments:
its crossing arguments are copied into the callee's heap after its last argument is evaluated, a value
read from a name held with no copy across the arguments after it, which M7's `changed-while-held`
guards and none of which runs a call, and an argument made in place, a list literal, built in the
caller's heap and copied once (§6, the owner, 2026-11-21, #519, comment 7060155980, "T83 1", which
retired the heap of the message's own that T24 (1), comment 7051492844, built such a message in). An
argument can raise where it has no call in it and is neither a literal, a number, a string with no hole,
a `char`, a `bool`, `null` or a duration's, a number's sign and a duration's included, nor a read of a name, either in brackets;
a message whose argument that can raise comes before every argument that crosses, or none of whose
arguments crosses, copies each crossing argument where it is evaluated; and a `send`, a message to a
`per node` actor and a call with a clause take the same path *(assumed: the reading of the product
pass, #519, comment 7056985643, and a reading of T83 1, which names the copies after the last argument
for the messages T24 (1) built in a heap of their own and no other)*. A message with an argument that
can wait after one that crosses is built in a heap of its own, as any message with an argument that
can wait is, and its crossing arguments are copied into that heap where they are evaluated *(assumed:
a reading of T83 1, whose words keep that heap and do not say where its copies are made)*. A `spawn`
makes the new actor's heap after its last argument where no argument crosses, and just before its first
crossing argument otherwise, so that a raise before that point finds no heap; where the heap cannot be
made (`allocation-failed`), the arguments before that point have run (§6, the owner, 2026-11-23, #519,
comment 7095825398, "T87 3"). A `spawn` whose first crossing argument, or an argument after it, can
raise takes the same rule as a message: the heap, which the arguments cross into, is freed with their
copies where its spawner crashes at that raise, and so where one of those arguments can wait; its actor
takes its number, its slot and its place under its supervisor once its last argument is evaluated, so
that such a raise takes none of them (§6, the owner, 2026-11-21, #519, comment 7060155980, "T82 1")
*(assumed: a reading of T82 1, the heap freed whole, and the readings of the product pass, #519, comment
7071789394, Q2 and Q3; a reading of T87 3, the first crossing argument evaluated after the heap is made,
an index or a list literal whose item raises among them, and a wait from that point on listed as T82 1's
reading lists it)*. Retired by that answer, T87 3: a `spawn` whose argument that can raise comes before
every argument that crosses, or none of whose arguments crosses, leaving its heap at such a raise, which
was assumed until then as a reading of T82 1 whose words name the copies a raise leaves behind and not
the heap they cross into.

Case: [actors/crash-inbox-kept.kz](../corpus/actors/crash-inbox-kept.kz)
```kurz
actor Notes {
    mut int count = 0

    pub void Note(int n) {
        if n == 2 {
            throw "two"
        }

        count = count + 1
        if count == 1 {
            print("first {n}")
        } else {
            print("then {n}")
        }
    }

    pub int Count() => count
}

l = spawn Notes()
send l.Note(1)
send l.Note(2)
send l.Note(3)
send l.Note(4)
print(l.Count())
```

Case: [actors/crash-children-end.kz](../corpus/actors/crash-children-end.kz)

Case: [actors/crash-during-arguments.kz](../corpus/actors/crash-during-arguments.kz)
```kurz
actor Slow {
    pub int Get() => 7
}

actor Box {
    pub void Boom() {
        throw "boom"
    }

    pub int Sum(List<int> xs, int k) {
        mut total = k
        for x in xs {
            total = total + x
        }

        return total
    }
}

b = spawn Box()
s = spawn Slow()
send b.Boom()
print(b.Sum([10, 20, 30], s.Get()))
```

Case: [actors/crash-after-copy.kz](../corpus/actors/crash-after-copy.kz)
```kurz
// the first `Go` crashes `Caller` at the index, after the list that crosses into the message: the list is copied after the last
// argument, so that no copy was made, and `Keeper` runs on with its total unchanged

actor Keeper {
    mut int total = 0

    pub int Take(List<int> xs, int n) {
        total = total + xs.Count + n
        return total
    }

    pub int Total() => total
}

actor Caller(Keeper Target) {
    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        print(Target.Take(xs, ys[at]))
    }
}

k = spawn Keeper()
c = spawn Caller(k)
send c.Go([10, 20, 30], 5)
c.Go([10, 20, 30], 1)
print(k.Total())
```

Case: [actors/crash-after-copy-send.kz](../corpus/actors/crash-after-copy-send.kz)
```kurz
// the first `Go` crashes `Caller` at the index, after the list that crosses into the message it sends: the list is copied after
// the last argument, so that no copy was made, and `Keeper` runs on with its total unchanged

actor Keeper {
    mut int total = 0

    pub void Add(List<int> xs, int n) {
        total = total + xs.Count + n
    }

    pub int Total() => total
}

actor Caller(Keeper Target) {
    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        send Target.Add(xs, ys[at])
    }
}

k = spawn Keeper()
c = spawn Caller(k)
send c.Go([10, 20, 30], 5)
c.Go([10, 20, 30], 1)
print(k.Total())
```

Case: [actors/crash-before-copy.kz](../corpus/actors/crash-before-copy.kz)
```kurz
// the first `Go` crashes `Caller` at the index, before the list after it crossed: no copy was made, and `Keeper` runs on with its
// total unchanged

actor Keeper {
    mut int total = 0

    pub int Take(int n, List<int> xs) {
        total = total + xs.Count + n
        return total
    }

    pub int Total() => total
}

actor Caller(Keeper Target) {
    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        print(Target.Take(ys[at], xs))
    }
}

k = spawn Keeper()
c = spawn Caller(k)
send c.Go([10, 20, 30], 5)
c.Go([10, 20, 30], 1)
print(k.Total())
```

Case: [actors/crash-between-copies.kz](../corpus/actors/crash-between-copies.kz)
```kurz
// the first `Go` crashes `Caller` at the index, between the two lists that cross into the message: both are copied after the last
// argument, so that no copy was made, and `Keeper` runs on with its total unchanged

actor Keeper {
    mut int total = 0

    pub int Take(List<int> xs, int n, List<int> zs) {
        total = total + xs.Count + n + zs.Count
        return total
    }

    pub int Total() => total
}

actor Caller(Keeper Target) {
    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        print(Target.Take(xs, ys[at], xs))
    }
}

k = spawn Keeper()
c = spawn Caller(k)
send c.Go([10, 20, 30], 5)
c.Go([10, 20, 30], 1)
print(k.Total())
```

Case: [actors/crash-spawn-after-copy.kz](../corpus/actors/crash-spawn-after-copy.kz)
```kurz
// the first `Go` crashes `Spawner` at the index, after the list crossed into the heap of the actor its `spawn` was making: that
// heap is freed with the list's copy at the crash, and no actor was made, so that the restarted `Spawner`'s `spawn` makes `Holder#2`

actor Holder(List<int> Items, int N) {
    pub int Count() => Items.Count + N
}

actor Spawner {
    mut int made = 0

    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        h = spawn Holder(xs, ys[at])
        made = made + 1
        print("{h} {h.Count()}")
    }

    pub int Made() => made
}

s = spawn Spawner()
send s.Go([10, 20, 30], 5)
s.Go([10, 20, 30], 1)
print(s.Made())
```

Case: [actors/crash-spawn-before-copy.kz](../corpus/actors/crash-spawn-before-copy.kz)
```kurz
// the first `Go` crashes `Spawner` at the index, which comes before the list that crosses into the heap of the actor its `spawn`
// makes: that heap is made just before the list, so that the crash finds none, and no actor was made, so that the restarted
// `Spawner`'s `spawn` makes `Holder#2`

actor Holder(int N, List<int> Items) {
    pub int Count() => Items.Count + N
}

actor Spawner {
    mut int made = 0

    pub void Go(List<int> xs, int at) {
        ys = [1, 2]
        h = spawn Holder(ys[at], xs)
        made = made + 1
        print("{h} {h.Count()}")
    }

    pub int Made() => made
}

s = spawn Spawner()
send s.Go([10, 20, 30], 5)
s.Go([10, 20, 30], 1)
print(s.Made())
```

Case: [actors/crash-child-gone.kz](../corpus/actors/crash-child-gone.kz)

Case: [actors/send-gone.kz](../corpus/actors/send-gone.kz)

Case: [actors/block-escalate.kz](../corpus/actors/block-escalate.kz)
```kurz
// `Worker`'s `Fail` runs before `Boss` takes the call the top-level code makes after `Go`, as one thread runs the ready actors
// first in, first out (U35)

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }
}

actor Boss {
    mut int n = 0

    pub void Go() {
        n = n + 1
        w = spawn Worker() on crash (c) {
            print("escalating")
            escalate
        }

        send w.Fail()
    }

    pub int Count() => n
}

b = spawn Boss()
b.Go()
print(b.Count())
```

Case: [actors/block-reason.kz](../corpus/actors/block-reason.kz)
```kurz
data Lost(int Code)

actor Worker {
    pub void Lose() {
        throw Lost(7)
    }

    pub void Divide(int n) {
        print(10 / n)
    }

    pub int Half(int n) => n / 2
}

w = spawn Worker() on crash (c) {
    match c.Reason {
        Lost l => print("lost {l.Code}")
        RuntimeError e => print(e.Kind)
    }

    restart
}
send w.Lose()
send w.Divide(0)
print(w.Half(4))
```

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)
```kurz
// `Worker`'s `Fail` runs before `Boss` takes the call the top-level code makes after `Go`, as one thread runs the ready actors
// first in, first out (U35)

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

actor Boss {
    mut int count = 0

    pub Worker Go() {
        count = count + 1
        kid = spawn Worker() on crash (c) {
            throw "block fails"
        }

        send kid.Fail()
        return kid
    }

    pub int Count() => count
}

b = spawn Boss()
w = b.Go()
print(b.Count())
print(w.Ping())
```

Case: [actors/per-node-child.kz](../corpus/actors/per-node-child.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

actor Holder per node {
    Worker worker = spawn Worker()
    mut int count = 0

    pub Worker Get() => worker

    pub int Count() => count

    pub void Fail() {
        throw "holder fails"
    }
}

w = Holder.Get()
print(w.Ping())
send Holder.Fail()
print(Holder.Count())
print(w.Ping())
```

### U22 (decided, §6) The supervisor restarts the actor

The default policy, `on crash restart`, runs the actor's constructor again, after the pause of U23,
on a copy of the arguments its `spawn` was given, their defaults among them, which the spawner's
heap keeps, every block they reach copied, a `move` argument's too (§6, the owner, 2026-11-14, #519,
comment 6880404917, Q13, "All eight leans (Recommended)"; the taken points of comment 6899225864).
The runtime runs the restart before any message of the inbox, and runs it in no message of the
supervisor *(assumed, §6)*. The new instance has its state from the constructor and its body's
defaults alone, as a new actor has it; a throw in the constructor at a restart is the actor's own
crash, which restarts it again (U3).

A call that reaches the actor while its restart runs its constructor, a call of the chain the restart
starts, waits in its inbox and is not let in by that chain (U17), so that a constructor that waits on
it waits on a call that waits for that constructor, which the top-level code's wait on one thread
reads as `deadlock` (U18) (§6, the owner, 2026-11-20, #599, comment 7051501153, "Option 1"). No
actor crashes on it, so that no restart loops on it, and a `send` of that chain to the actor waits in its inbox behind the restart and is no
`deadlock` *(assumed: the reading of "as T29 option 1 reads", #599, comment 7056452417)*. The error
names the line of the call the inbox holds, the call that closes the loop, not the line of the
top-level code's call that waits (§6, the owner, 2026-11-21, #599, comment 7060156109, Q6 1), and the
trace below it is the top-level code's where it waits, the stack that raises (§6, the owner,
2026-11-21, #599, comment 7063693195, the trace 1). Where more than one call is kept out, one by each
of several actors' starts, the error names every one: its line names the first place in source order
and ends with a colon, and one line `kept out at <file>:<n>` for each further place stands under it,
before the trace, while one kept-out call keeps the error line of U18 (§6, the owner,
2026-11-21, #599, comment 7067131310, several 2; comment 7067139953, the form 1). The places stand
in source order, by file and then line, not in the order the runtime meets the actors; a place that
keeps out more than one call, as two actors that run the same code do, takes one line with the count
of its calls, `kept out at kept-same.kz:12 (2 calls)`, and on the error line
`deadlock at kept-same.kz:12 (2 calls):`, so also where it is the one place kept out, while a place
kept out once keeps its text with no count; at most 10 `kept out at` lines stand under the error
line, then one line `... and K more` (§6, the owner, 2026-11-21, #599, comment 7067639209, the
order 1, the same place 1, the cap 1). A path sorts as the error line writes it, byte for byte as
`<` compares two strings (T32), and K counts places, not calls *(assumed in §6)*.

Case: [actors/crash-in-constructor-at-restart.kz](../corpus/actors/crash-in-constructor-at-restart.kz)
```kurz
actor Tally {
    mut int count = 0

    pub int Next() {
        count = count + 1
        return count
    }
}

int Checked(int n) {
    if n == 2 {
        throw "second start"
    }

    return n
}

actor Worker(Tally T) {
    mut int start = Checked(T.Next())

    pub void Fail() {
        throw "fail"
    }

    pub int Start() => start
}

t = spawn Tally()
w = spawn Worker(t)
print(w.Start())
send w.Fail()
print(w.Start())
```

Case: [actors/crash-restarts-fresh.kz](../corpus/actors/crash-restarts-fresh.kz)

Case: [actors/crash-children-end.kz](../corpus/actors/crash-children-end.kz)

Case: [actors/send-gone.kz](../corpus/actors/send-gone.kz)

Case: [actors/crash-child-gone.kz](../corpus/actors/crash-child-gone.kz)

Case: [actors/block-escalate.kz](../corpus/actors/block-escalate.kz)

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)

Case: [actors/timeout-callee-crash.kz](../corpus/actors/timeout-callee-crash.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Ticker {
    pub int Ping() => 1
}

actor Store(Ticker T) {
    mut int count = 0

    pub void Work() {
        count = count + 1
        print("work starts")
        Hold(1200ms)
        print("ping {T.Ping()}")
    }

    pub int Count() => count
}

actor Front(Store S) {
    pub void Start() {
        send S.Work()
    }
}

t = spawn Ticker()
s = spawn Store(t)
f = spawn Front(s)
within 1s {
    f.Start()
}
print(s.Count())
```

Case: [actors/timeout-capped.kz](../corpus/actors/timeout-capped.kz)
```kurz
// `Front`'s `Ask` runs, and its `within 2s` keeps the 100 ms its chain has left from the top-level code's `within` (U38), so
// that its call `S.Get()`, which waits behind `Busy`'s 300 ms in `Store`'s inbox, is dropped there past that deadline (U37):
// `Ask` raises `call-timeout`, `Front` restarts with `asked` at 0 (U22), and `Store` ran no `Get`

// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    mut int gets = 0

    pub void Busy() {
        Hold(300ms)
    }

    pub int Get() {
        gets = gets + 1
        return gets
    }

    pub int Gets() => gets
}

actor Front(Store S) {
    mut int asked = 0

    pub void Start() {
        send this.Ask()
    }

    pub void Ask() {
        asked = asked + 1
        within 2s {
            print("got {S.Get()}")
        }
    }

    pub int Asked() => asked
}

s = spawn Store()
f = spawn Front(s)
within 100ms {
    f.Start()
}
send s.Busy()
print(f.Asked())
print(s.Gets())
```

Case: [actors/per-node-restart.kz](../corpus/actors/per-node-restart.kz)
```kurz
actor Counter per node {
    mut int count = 0

    pub void Fail() {
        throw "counter fails"
    }

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

send Counter.Add(1)
send Counter.Fail()
send Counter.Add(5)
print(Counter.Get())
```

Case: [actors/restart-reentrant.kz](../corpus/actors/restart-reentrant.kz)
```kurz
// at its restart `A`'s constructor waits on `B.Ask`, whose call of `Peek` waits in `A`'s inbox for that constructor, so that the
// top-level code's wait on `Get` finds nothing left to run, and the error names that call of `Peek`, which closes the loop

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()
    List<int> items = [1, 2, 3]

    pub int Peek() => items.Count

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

b = spawn B()
a = spawn A(b)
print(a.Get())
b.Set(a)
send a.Fail()
print(a.Get())
```

Case: [tests/test-restart-reentrant.kz](../corpus/tests/test-restart-reentrant.kz)
```kurz
// at its restart `A`'s constructor waits on `B.Ask`, whose call of `Peek` waits in `A`'s inbox for that constructor, so that the
// runner's wait for the test finds nothing left to run, and the words under its `FAILED` line name that call of `Peek`, not the test

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()
    List<int> items = [1, 2, 3]

    pub int Peek() => items.Count

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

test "restarts into a call it keeps out" {
    b = spawn B()
    a = spawn A(b)
    print(a.Get())
    b.Set(a)
    send a.Fail()
    print(a.Get())
}
```

Case: [actors/restart-reentrant-two.kz](../corpus/actors/restart-reentrant-two.kz)
```kurz
// at their restarts `A`'s and `C`'s constructors each wait on a call whose call back waits in the inbox for that constructor, so
// that the top-level code's wait on `Get` finds nothing left to run, and the error names both calls kept out, `A`'s on its line and
// `C`'s on a line of its own under it

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor D {
    mut C? c = null

    pub void Set(C x) {
        c = x
    }

    pub int Ask() {
        x = c
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

actor C(D Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "c fails"
    }

    pub int Get() => first
}

b = spawn B()
a = spawn A(b)
d = spawn D()
c = spawn C(d)
print(a.Get())
print(c.Get())
b.Set(a)
d.Set(c)
send a.Fail()
send c.Fail()
print(a.Get())
```

Case: [tests/test-restart-reentrant-two.kz](../corpus/tests/test-restart-reentrant-two.kz)
```kurz
// at their restarts `A`'s and `C`'s constructors each wait on a call whose call back waits in the inbox for that constructor, so
// that the runner's wait for the test finds nothing left to run, and the words under its `FAILED` line name both calls, `A`'s first

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor D {
    mut C? c = null

    pub void Set(C x) {
        c = x
    }

    pub int Ask() {
        x = c
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

actor C(D Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "c fails"
    }

    pub int Get() => first
}

test "restarts two actors into calls they keep out" {
    b = spawn B()
    a = spawn A(b)
    d = spawn D()
    c = spawn C(d)
    print(a.Get())
    print(c.Get())
    b.Set(a)
    d.Set(c)
    send a.Fail()
    send c.Fail()
    print(a.Get())
}
```

Case: [actors/restart-reentrant-places.kz](../corpus/actors/restart-reentrant-places.kz)
```kurz
// at their restarts five `A`s' constructors each wait on `B.Ask`, whose call back waits in the inbox for that constructor, from
// three places of `Ask`, spawned so that the runtime's walk of the actors meets the places in another order than the file's: the
// error names them in the file's order, the first on its line, and each place that keeps out two calls with their count

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask(int k) {
        x = a
        if x == null {
            return 0
        }

        return match k {
            0 => vouch x.Peek()
            1 => vouch x.Peek()
            else => vouch x.Peek()
        }
    }
}

actor A(B Other, int K) {
    int first = Other.Ask(K)

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

mut sum = 0
mut all = List<A>()
for k in [2, 1, 0, 1, 0] {
    b = spawn B()
    a = spawn A(b, k)
    sum = sum + a.Get()
    b.Set(a)
    all.Add(a)
}

print(sum)
for target in all {
    send target.Fail()
}

print(all[0].Get())
```

Case: [actors/restart-reentrant-one-place.kz](../corpus/actors/restart-reentrant-one-place.kz)
```kurz
// at their restarts two `A`s' constructors each wait on `B.Ask`, whose call back waits in the inbox for that constructor, both from
// one place of `Ask`: the error line names that place with the count of its calls and ends with a colon, and no line stands under it

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

b = spawn B()
a = spawn A(b)
d = spawn B()
c = spawn A(d)
print(a.Get() + c.Get())
b.Set(a)
d.Set(c)
send a.Fail()
send c.Fail()
print(a.Get())
```

Case: [actors/restart-reentrant-cap.kz](../corpus/actors/restart-reentrant-cap.kz)
```kurz
// at their restarts fourteen `A`s' constructors each wait on `B.Ask`, whose call back waits in the inbox for that constructor, from
// thirteen places of `Ask`, spawned in the reverse of the file's order, the last place twice: the error line names the first place
// in the file, ten lines `kept out at` the ten after it, and one line the two places past those

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask(int k) {
        x = a
        if x == null {
            return 0
        }

        return match k {
            0 => vouch x.Peek()
            1 => vouch x.Peek()
            2 => vouch x.Peek()
            3 => vouch x.Peek()
            4 => vouch x.Peek()
            5 => vouch x.Peek()
            6 => vouch x.Peek()
            7 => vouch x.Peek()
            8 => vouch x.Peek()
            9 => vouch x.Peek()
            10 => vouch x.Peek()
            11 => vouch x.Peek()
            else => vouch x.Peek()
        }
    }
}

actor A(B Other, int K) {
    int first = Other.Ask(K)

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

mut sum = 0
mut all = List<A>()
for k in [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 12] {
    b = spawn B()
    a = spawn A(b, k)
    sum = sum + a.Get()
    b.Set(a)
    all.Add(a)
}

print(sum)
for target in all {
    send target.Fail()
}

print(all[0].Get())
```

Case: [tests/test-restart-reentrant-places.kz](../corpus/tests/test-restart-reentrant-places.kz)
```kurz
// each test restarts `A`s whose constructors wait on `B.Ask`, whose call back waits in the inbox for that constructor, from places
// of `Ask` in the order the list gives, so that the runner's wait for the test finds nothing left to run; the words under its
// `FAILED` line name the places in the file's order, each place that keeps out two calls with their count, at most ten lines
// `kept out at` and then one line for the places past those

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask(int k) {
        x = a
        if x == null {
            return 0
        }

        return match k {
            0 => vouch x.Peek()
            1 => vouch x.Peek()
            2 => vouch x.Peek()
            3 => vouch x.Peek()
            4 => vouch x.Peek()
            5 => vouch x.Peek()
            6 => vouch x.Peek()
            7 => vouch x.Peek()
            8 => vouch x.Peek()
            9 => vouch x.Peek()
            10 => vouch x.Peek()
            11 => vouch x.Peek()
            else => vouch x.Peek()
        }
    }
}

actor A(B Other, int K) {
    int first = Other.Ask(K)

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

int KeepOut(List<int> places) {
    mut sum = 0
    mut all = List<A>()
    for k in places {
        b = spawn B()
        a = spawn A(b, k)
        sum = sum + a.Get()
        b.Set(a)
        all.Add(a)
    }

    print(sum)
    for target in all {
        send target.Fail()
    }

    return all[0].Get()
}

test "keeps out calls of thirteen places" {
    print(KeepOut([12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 12]))
}

test "keeps out calls of three places, two of them twice" {
    print(KeepOut([12, 1, 0, 1, 0]))
}

test "keeps out two calls of one place" {
    print(KeepOut([0, 0]))
}
```

Case: [actors/restart-no-call-back.kz](../corpus/actors/restart-no-call-back.kz)
```kurz
// the program of `actors/restart-reentrant.kz` with `B` given no `A`: at the restart `B.Ask` calls nothing back, and the constructor
// ends

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()
    List<int> items = [1, 2, 3]

    pub int Peek() => items.Count

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

b = spawn B()
a = spawn A(b)
print(a.Get())
send a.Fail()
print(a.Get())
```

Case: [actors/restart-send-waits.kz](../corpus/actors/restart-send-waits.kz)
```kurz
// at its restart `A`'s constructor waits on `B.Ask`, whose `send` of `Note` waits in `A`'s inbox behind that constructor and behind the
// top-level code's `Get`, and runs on the new instance

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            send x.Note()
            return 5
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()
    List<int> items = [1, 2, 3]

    pub void Note() {
        print("note {items.Count}")
    }

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

b = spawn B()
a = spawn A(b)
print(a.Get())
b.Set(a)
send a.Fail()
print(a.Get())
print(a.Get())
```

Case: [actors/restart-then-callback.kz](../corpus/actors/restart-then-callback.kz)

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)
```kurz
// `Right`'s `Ask` waits on `Left`, whose `Ask` waits on `Right` with `Right`'s `Ask` ahead of it in `Right`'s inbox, as one
// thread runs the ready actors first in, first out (U35): `Right`'s wait closes the loop and gets `deadlock`, and its new
// instance answers `Left`'s call from the kept inbox (U60, U61, U62, U21)

actor Left {
    pub void Ask(Right other) {
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        print("right got {other.Value()}")
    }

    pub int Value() => 2
}

l = spawn Left()
r = spawn Right()
send l.Ask(r)
send r.Ask(l)
print(l.Value())
```

### U23 (decided, §6) The pause before a restart grows

A restart waits a pause of 100 ms at the actor's first crash, twice the one before at each crash
after it up to 30 s, each varied at random by up to 20% either way, and 100 ms again after a run
of 30 s since the last restart with no crash, as Akka's backoff supervisor waits (§6, the owner,
2026-10-30, "100 ms doubling to 30 s"; the variation drawn from the thread's generator and one
clock of the runtime, the taken points of comment 6899225864). What doubles is the pause before its
variation, the cap holds that pause before it is varied, so that a pause reaches 36 s, and the run of
30 s counts from the start of the last restart *(assumed: as §6 marks them, readings of the build
that the answer of 2026-10-30 did not give)*. While a pause runs, a wait of the top-level code that
finds nothing else to run sleeps until it ends, and raises no `deadlock` (U18)
(§6, the taken points of comment 6899225864). A corpus case crashes at most three times, about
0.7 s of pauses, unless it writes `after 1ms` (U26) *(assumed: as §6 marks it)*. The case below holds
the wait that sleeps and raises no `deadlock`, and prints what a restart with no pause prints too; the
pauses are held by `ActorTests.The_pause_doubles_up_to_30_s_and_starts_again_after_a_full_cap`, through
a clock the test stands in for.

Case: [actors/crash-wait-sleeps.kz](../corpus/actors/crash-wait-sleeps.kz)
```kurz
actor Flaky {
    pub void Fail() {
        throw "flaky"
    }

    pub int Ping() => 1
}

f = spawn Flaky()
send f.Fail()
print(f.Ping())
```

### U24 (decided, §6) One log line per crash

Each crash the supervisor handles writes one line at `Error` in the layout of P7 on standard error,
through the runtime's writer on every target, with the category `Kurz.Actor`: the actor's type and
its number of U2, the error's id, the text below the error line, a thrown value's in double quotes as
a `string` field is written, and the place, as the error line of a `throw` at the root carries them,
`Counter#3 crashed: thrown "no stock" at shop.kz:12`, and for an id with no text below its error line
the id and the place alone, `Calc#1 crashed: divide-by-zero at calc.kz:2`, a stack overflow's as its
error line writes them, and no chain ID while P8 is open (§6, the owner, 2026-11-13, #519, comment
6847932993, Q24; the owner, 2026-11-14, comment 6880404917, Q16; the owner, 2026-11-15, comment
6902210646, Q8, "With thrown text; points stand") *(assumed: the words of the line, and `KURZ_LOG`
read at the start of a program that spawns, so that its filter holds for the line (P4))*. The
corpus runner leaves such a line out of standard error in a case that names a rule of a crash (E3),
so that the case below shows its output alone, the restart (U19), and the words of the line are held
by `ActorTests.The_log_line_carries_the_actor_the_id_the_place_and_the_thrown_text`.

Case: [actors/crash-log-line.kz](../corpus/actors/crash-log-line.kz)

### U25 (decided, §6) The policy at `spawn`

The words after `spawn`'s argument list say what the supervisor does at the actor's crash:
`on crash restart` restarts it (U22, U23), the default, `on crash stop` leaves it stopped, and
`on crash escalate` makes the supervisor crash too, with the child's crash passed up whole, its id,
its place and its text (§6, "Policy at `spawn`"; the owner, 2026-11-15, #519, comment 6902210646, Q4).
A stopped actor's crash writes its log line (U24) (§6, Q24), a caller that waited on its crashing
message gets `callee-crashed` (U20), and a call or a `send` that comes after the stop raises `actor-gone`, as one
to an actor that ended does (U20, U6) (§6; a `send`: the owner, 2026-11-16, #519, comment 6923513534); its inbox goes as the inbox of an actor that ended does, so
that a call queued in it at the crash raises `actor-gone` too (§6, the owner, 2026-11-16, #519,
comment 6919351399, "actor-gone"). An escalated crash
writes no log line, and the supervisor that takes it, the first up the chain of supervisors whose
policy restarts or stops, writes the one line (§6, Q24), naming the actor its policy handles, with the
id, the place and the text of the first crash *(assumed: as §6 marks it)*; the supervisor's crash ends
its other children, as any crash does (U21), and a crash passed up into an actor whose constructor
runs at its `spawn` goes on to its spawner, since a throw there is the spawner's crash (U3) *(assumed:
as §6 marks it)*. At the top level the root is the supervisor, so that `escalate` there ends the
process with the child's error line, the text below it and the exit code 1 (§6, comment 6902210646,
Q4), and with the trace of the child's stack, from its raise to its message's method *(assumed: as §6
marks it, the trace of the stack that raised, as at a raise of the top-level code)*. Under `kurz test` a
crash that escalates into a test's own actor is taken there with no log line and fails that test, the
child's words under its `FAILED` line, and the run goes on with the test after it (Y5; §11, the owner,
2026-11-20, issue 586, comment 7051499099, Q2 of comment 6992687784 at its lean) *(assumed: F3 of comment
6993110710, a reading of the build said back there)*.

Case: [actors/crash-stop.kz](../corpus/actors/crash-stop.kz)
```kurz
actor Flaky {
    pub void Fail() {
        throw "flaky"
    }

    pub int Ping() => 1
}

actor Other {
    pub int Ping() => 2
}

f = spawn Flaky() on crash stop
o = spawn Other()
print(f.Ping())
send f.Fail()
print(o.Ping())
print(f.Ping())
```

Case: [actors/crash-escalate-root.kz](../corpus/actors/crash-escalate-root.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash escalate
print(w.Ping())
send w.Fail()
print(w.Ping())
print("after")
```

Case: [actors/crash-policy-word.kz](../corpus/actors/crash-policy-word.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

w = spawn Worker() on crash retry
print(w.Ping())
```

Case: [actors/block-escalate.kz](../corpus/actors/block-escalate.kz)

Case: [actors/timeout-gone-queued.kz](../corpus/actors/timeout-gone-queued.kz)
```kurz
// `Store` crashed and its policy stopped it in the wait of `o.Ping()`, before the call of `Get` is queued past the block's deadline,
// which the hold of 100 ms passes: the actor is looked up before the deadline is compared

// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub void Fail() {
        throw "gone"
    }

    pub int Get() => 1
}

actor Other {
    pub int Ping() => 2
}

gone = spawn Store() on crash stop
o = spawn Other()
send gone.Fail()
print(o.Ping())
within 50ms {
    Hold(100ms)
    print(gone.Get())
}
```

Case: [actors/within-clause-zero-gone.kz](../corpus/actors/within-clause-zero-gone.kz)
```kurz
// `Store` crashed and its policy stopped it in the wait of `o.Ping()`; the bound of the call of `Get`, computed as zero, is read
// after the call's arguments and before the call is queued, where the actor is looked up, so that it is refused, and not `actor-gone`

actor Store {
    pub void Fail() {
        throw "gone"
    }

    pub int Get() => 1
}

actor Other {
    pub int Ping() => 2
}

gone = spawn Store() on crash stop
o = spawn Other()
send gone.Fail()
print(o.Ping())
zero = 1s - 1s
print(gone.Get() within zero)
```

Case: [actors/timeout-chain-left.kz](../corpus/actors/timeout-chain-left.kz)
```kurz
// `Ask` holds the thread past the 100 ms of the chain's root, the top-level code's call, so that its call `S.Get()`, which
// takes the time its chain has left, raises `call-timeout` at its line, the crash of `Front`, whose policy passes the crash up
// to the root, which ends the process with that line (U25); a call that took its own 5 seconds would wait, and the top-level
// code's call raise `call-timeout` at its own line

// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub int Get() => 1
}

actor Front(Store S) {
    pub int Ask() {
        Hold(150ms)
        return S.Get()
    }
}

s = spawn Store()
f = spawn Front(s) on crash escalate
within 100ms {
    print(f.Ask())
}
```

Case: [tests/test-child-escalates.kz](../corpus/tests/test-child-escalates.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "down"
    }

    pub int Get() => 1
}

test "child escalates" {
    w = spawn Worker() on crash escalate
    send w.Fail()
    print(w.Get())
}
```

Case: [actors/per-node-start-throws.kz](../corpus/actors/per-node-start-throws.kz)
```kurz
// `User`'s `Use` waits in `Cache`'s start for `Starts.Take` while `Probe`'s `Ping` answers the top-level code, and `User`, woken by
// that `Take`, throws before `Starts` takes the top-level code's `Peek`, as one thread runs the ready actors first in, first out (U35)

actor Starts per node {
    mut int count = 0

    pub int Take() {
        count = count + 1
        return count
    }

    pub int Peek() => count
}

actor Cache per node {
    int first = Checked(Starts.Take())

    pub int Get() => first
}

int Checked(int n) {
    if n == 1 {
        throw "first start"
    }

    return n
}

actor User {
    pub void Use() {
        print(Cache.Get())
    }
}

actor Probe {
    pub int Ping() => 9
}

u = spawn User() on crash stop
p = spawn Probe()
send u.Use()
print(p.Ping())
print(Starts.Peek())
print(Cache.Get())
```

Case: [actors/per-node-queued-gone.kz](../corpus/actors/per-node-queued-gone.kz)
```kurz
// `User`'s `Use` starts `Cache`, whose default waits on `Slow`, while `Probe`'s `Ping` answers the top-level code, whose call
// then waits in `Cache`'s inbox, as one thread runs the ready actors first in, first out (U35)

actor Slow per node {
    pub int Get() => 1
}

actor Cache per node {
    int first = Fails(Slow.Get())

    pub int Get() => first
}

int Fails(int n) {
    throw "start fails at {n}"
}

actor User {
    pub void Use() {
        print(Cache.Get())
    }
}

actor Probe {
    pub int Ping() => 9
}

u = spawn User() on crash stop
p = spawn Probe()
send u.Use()
print(p.Ping())
print(Cache.Get())
```

Case: [actors/per-node-outlives-caller.kz](../corpus/actors/per-node-outlives-caller.kz)
```kurz
// `Worker`'s `Fail` runs before `Counter`'s `Get`, as one thread runs the ready actors first in, first out (U35)

actor Counter per node {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

actor Worker {
    pub void Start() {
        Counter.Add(2)
    }

    pub void Fail() {
        throw "worker fails"
    }
}

w = spawn Worker() on crash stop
w.Start()
send w.Fail()
print(Counter.Get())
```

Case: [actors/per-node-escalate.kz](../corpus/actors/per-node-escalate.kz)
```kurz
actor Cache per node on crash escalate {
    pub void Fail() {
        throw "cache fails"
    }

    pub int Ping() => 1
}

actor Worker {
    pub void Start() {
        print(Cache.Ping())
    }
}

w = spawn Worker()
w.Start()
send Cache.Fail()
print(Cache.Ping())
print("after")
```

Case: [actors/per-node-stop.kz](../corpus/actors/per-node-stop.kz)
```kurz
// `Counter`'s `Fail` runs before `Probe`'s `Ping`, as one thread runs the ready actors first in, first out (U35)

actor Counter per node on crash stop {
    mut int count = Started()

    pub void Add() {
        count = count + 1
    }

    pub void Fail() {
        throw "counter fails"
    }

    pub int Get() => count
}

int Started() {
    print("started")
    return 0
}

actor Probe {
    pub int Ping() => 9
}

p = spawn Probe()
send Counter.Add()
print(Counter.Get())
send Counter.Fail()
print(p.Ping())
print(Counter.Get())
```

Case: [actors/per-node-stop-gone.kz](../corpus/actors/per-node-stop-gone.kz)
```kurz
actor Counter per node on crash stop {
    mut int count = 0

    pub void Fail() {
        throw "counter fails"
    }

    pub int Get() => count
}

send Counter.Fail()
// the call waits in the inbox behind `Fail`, as one sender's messages run in the order it sent them (U5) and no worker thread
// runs `Fail` before the top-level code waits (U35), and the stop at `Fail`'s crash gives it `actor-gone`
print(Counter.Get())
```

Case: [actors/per-node-stop-sends.kz](../corpus/actors/per-node-stop-sends.kz)
```kurz
// `Counter`'s `Fail` runs before `Probe`'s `Ping`, as one thread runs the ready actors first in, first out (U35), and the stop
// at its crash takes the `send` of `Add` queued behind it with the inbox (U25), so that the fresh start counts 0

actor Counter per node on crash stop {
    mut int count = 0

    pub void Add() {
        count = count + 1
    }

    pub void Fail() {
        throw "counter fails"
    }

    pub int Get() => count
}

actor Probe {
    pub int Ping() => 9
}

p = spawn Probe()
send Counter.Fail()
send Counter.Add()
print(p.Ping())
print(Counter.Get())
```

Case: [actors/waitline-stop.kz](../corpus/actors/waitline-stop.kz)
```kurz
// `Right` gets `deadlock` and its policy stops it: `Left`'s call queued in its inbox raises `actor-gone`, which `Left`'s block
// reads, and `Left` restarts and answers the top-level code's call (U62, U25, U28)

actor Left {
    pub void Ask(Right other) {
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        print("right got {other.Value()}")
    }

    pub int Value() => 2
}

l = spawn Left() on crash (c) {
    match c.Reason {
        RuntimeError e => print(e.Kind)
    }

    restart
}
r = spawn Right() on crash stop
send l.Ask(r)
send r.Ask(l)
print(l.Value())
print(r.Value())
```

Case: [actors/waitline-escalate.kz](../corpus/actors/waitline-escalate.kz)
```kurz
// `Right`'s policy escalates its `deadlock` to the top-level code, which ends the process with `Right`'s error line and the
// text of the loop under it (U63, U25)

actor Left {
    pub void Ask(Right other) {
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        print("right got {other.Value()}")
    }

    pub int Value() => 2
}

l = spawn Left()
r = spawn Right() on crash escalate
send l.Ask(r)
send r.Ask(l)
print(l.Value())
```

Case: [actors/waitline-vouch.kz](../corpus/actors/waitline-vouch.kz)
```kurz
// `vouch` takes `Left`'s call of `Value` out of U56's loop, `Left.Ask` waiting on `Right.Value`, whose code waits on `Left`, a
// promise that does not hold here: `Right`'s `Ask` waits on `Left`, whose `Ask` waits on `Right`, `Right`'s wait closes the
// loop and gets `deadlock`, and the line of `Left`'s wait in its text is the line where the word stands (U65, U63); `Right`'s
// policy escalates it to the top-level code (U25)

actor Left {
    pub void Ask(Right other) {
        print("left got {vouch other.Value()}")
    }

    pub int Value() => 1
}

actor Right(Left Peer) {
    pub void Ask() {
        print("right got {Peer.Value()}")
    }

    pub int Value() => Peer.Value() + 1
}

l = spawn Left()
r = spawn Right(l) on crash escalate
send l.Ask(r)
send r.Ask()
print(l.Value())
```

### U26 (decided, §6) The first pause and the limit

After `restart`, `after <duration>` sets the first pause of the growing pause (U23), which doubles
from it at each crash up to the cap of 30 s and comes back to it after a run of 30 s with no crash,
and `max N in <duration>` sets a limit, where by default none holds (§6, "Policy at `spawn`"; the
owner, 2026-10-30, "100 ms doubling to 30 s" and "No limit, as Akka"). The limit counts the crashes
the policy restarts in a window of that duration that slides with each crash, as Erlang's
`supervisor` keeps the times of its restarts within its period (`can_restart`), and the crash that
finds N earlier crashes its policy restarted in the window, one exactly a window old among them
*(assumed: as §6 marks it, a reading of the build that the answers did not give)*, passes it: the
actor ends, and its
supervisor crashes with `restart-limit` at the line of the `spawn`, which the supervisor's own policy
takes in turn, at the top level the end of the process (§6, the owner, 2026-11-15, #519, comment 6902210646, Q4, "Erlang:
restart-limit id") *(assumed: as §6 marks it, each crash counted at its time, before its pause, the
log line of the crash that passes the limit written, as Erlang's supervisor reports a child's exit
before it shuts down, and at the top level the trace of the top-level code where it waits)*. A `per` actor,
which is never spawned, takes its limit from its policy, the words after its name (U27), and one that
passes the limit ends, and the next call of its key starts a fresh one,
its constructor run again, with no `restart-limit` and no limit across those starts (§6, the owner, 2026-11-17, #561, comment 6945857315,
Q2, "Key restarts fresh on next call", as Akka's sharding does) *(assumed, §6: below the limit the
runtime restarts the `per` actor in place as its policy says, its window counted from its fresh start;
the calls queued in it at the crash that passes the limit raise `actor-gone`, and that crash writes its log line as any
crash does)*. The
duration after `after` is above 0 and at most the cap, the count after `max` an `int` of at least 1
and the duration after `in` above 0, and a value of another type or past its bound is
`type-mismatch` (T1), while a literal's own error stands (L10, L14) (§6; `after` past the cap, the
owner, 2026-11-16, #519, comment 6919351399, "Compile error") *(assumed: as §6 marks it, the other
bounds, as Akka's backoff supervisor refuses a minimum of 0 or one past its maximum)*.

Case: [actors/crash-after.kz](../corpus/actors/crash-after.kz)
```kurz
actor Flaky {
    mut int n = 0

    pub void Fail() {
        throw "flaky"
    }

    pub int Ping() {
        n = n + 1
        return n
    }
}

f = spawn Flaky() on crash restart after 1ms
send f.Fail()
print(f.Ping())
send f.Fail()
send f.Fail()
print(f.Ping())
```

Case: [actors/crash-restart-limit.kz](../corpus/actors/crash-restart-limit.kz)
```kurz
actor Flaky {
    pub void Fail() {
        throw "flaky"
    }

    pub int Ping() => 1
}

f = spawn Flaky() on crash restart after 1ms max 2 in 1min
send f.Fail()
send f.Fail()
print(f.Ping())
send f.Fail()
print(f.Ping())
print("after")
```

Case: [actors/crash-policy-knob.kz](../corpus/actors/crash-policy-knob.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

w = spawn Worker() on crash stop after 1ms
print(w.Ping())
```

Case: [actors/crash-policy-count.kz](../corpus/actors/crash-policy-count.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

w = spawn Worker() on crash restart max 0 in 1min
print(w.Ping())
```

Case: [actors/per-node-limit.kz](../corpus/actors/per-node-limit.kz)
```kurz
// `Counter`'s second `Fail` runs before `Probe`'s `Ping`, as one thread runs the ready actors first in, first out (U35)

actor Counter per node on crash restart after 1ms max 1 in 1min {
    mut int count = Started()

    pub void Add() {
        count = count + 1
    }

    pub void Fail() {
        throw "counter fails"
    }

    pub int Get() => count
}

int Started() {
    print("started")
    return 0
}

actor Probe {
    pub int Ping() => 9
}

p = spawn Probe()
send Counter.Fail()
send Counter.Add()
print(Counter.Get())
send Counter.Fail()
print(p.Ping())
print(Counter.Get())
```

### U27 (decided, §6) The type's default

The words of a policy after the name of an actor that is not `per` (U1) are the type's default,
which holds at every `spawn` of it that writes none, and a policy at `spawn` wins over it, as a call's
`within` wins over the declaration's (§6, "Policy at `spawn`"; 2026-11-13, Q11; the owner,
2026-11-15, #519, comment 6902210646, Q3, "Elixir: type's default"), whole, its knobs with it
*(assumed: as §6 marks it, as a child spec's `restart` replaces the module's default)*. The same
words after a `per` actor's name are its policy (§6, the owner, 2026-11-13, #519, comment 6847932993,
Q15), `per node` among them (U54); `per` with other words than `node` has no rule here (U1, U51), and `compiler/README.md` says what
the build does with both.

Case: [actors/crash-type-default.kz](../corpus/actors/crash-type-default.kz)
```kurz
actor Flaky on crash stop {
    mut int n = 0

    pub void Fail() {
        throw "flaky"
    }

    pub int Ping() {
        n = n + 1
        return n
    }
}

f = spawn Flaky() on crash restart after 1ms
print(f.Ping())
send f.Fail()
print(f.Ping())
g = spawn Flaky()
send g.Fail()
print(f.Ping())
print(g.Ping())
```

Case: [actors/block-type.kz](../corpus/actors/block-type.kz)
```kurz
string Note(int line) => "type's block at {line}"

actor Worker on crash (c) {
    print(Note(c.Place.Line))
    restart
} {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker()
send w.Fail()
print(w.Ping())
```

### U28 (decided, §6) The block of a policy

In the place of the words of a policy, after `spawn`'s arguments and after an actor's name, a block
can decide what the supervisor does at the actor's crash: `on crash (c) { ... }`, its one parameter
in parentheses, which names the crash (U30), and its block, opened on that line (§6, "Policy at
`spawn`"). The block runs as a message of the supervisor, after its current message, with the calls
of the chain that message lets in (U17), and before its queued ones, and its decision (U29) is what
the supervisor does (§6, the owner, 2026-11-15, #519, comment 6902210646, Q2, "Erlang/Akka:
supervisor msg"); at a `spawn` of the top-level code it runs in that code's wait (U33). The block
ends the expression at `spawn`, as the words of a policy do (U2), and after an actor's name it is the
type's default (U27), the braces after it the actor's body; no parentheses after `on crash` before a block, no
name in them, a second one, and a block that does not open on the line of its parameter are `syntax` (E4)
*(assumed: as §6 marks it)*. The blocks `attempt { ... }` and `retry { ... }` of §6, which keep a crash to a piece of
code, have no rule here but where each word is a name, and where, inside a header's brackets, arms, blocks and
parentheses, each is the block's word again, but for `attempt`'s block, whose rules are U46 to U50, and
`compiler/README.md` says what the build does with `retry`'s; in
the condition, the collection or the subject before the block of an `if`, a `while`, a `for` or a `match` each word is a
name, so that a variable of either name stands there, as a struct literal is no condition in Rust *(assumed: §6, the
owner, 2026-11-13, #519, comment 6847932993, Q10, which makes the words words only where they stand, so that a variable
may be named as one)*, and inside that header's brackets, arms and blocks it is the block's word again, as Rust
reads a struct literal there (§6, the owner, 2026-11-17, #570, comment 6961064167, Q3, "Block's words
(Recommended)"), and inside its parentheses too *(assumed, §6: the parentheses, which the answer did not name, read as its
brackets)*.

Case: [actors/block-sample.kz](../corpus/actors/block-sample.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    print("died: {c.Reason}")
    restart
}
send w.Fail()
print(w.Ping())
```

Case: [actors/block-order.kz](../corpus/actors/block-order.kz)
```kurz
// `first`'s `Fail` runs before `Other` answers `Go`'s call, so that its block stands ahead of `Queued` in `Boss`'s inbox, as one
// thread runs the ready actors first in, first out (U35)

actor Other {
    pub int Ping() => 1
}

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }
}

actor Boss {
    pub void Go(Other other) {
        first = spawn Worker() on crash (c) {
            print("first's block")
            stop
        }

        send this.Queued()
        send first.Fail()
        print(other.Ping())
        print("go ends")
    }

    pub void Queued() {
        print("queued")
    }

    pub int Ping() => 2
}

o = spawn Other()
b = spawn Boss()
b.Go(o)
print(b.Ping())
```

Case: [actors/block-no-parameter.kz](../corpus/actors/block-no-parameter.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

w = spawn Worker() on crash {
    stop
}
print(w.Ping())
```

Case: [actors/block-supervisor-state.kz](../corpus/actors/block-supervisor-state.kz)
```kurz
// `Worker`'s `Fail` runs before `Boss` takes the call the top-level code makes after `Go`, as one thread runs the ready actors
// first in, first out (U35)

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }
}

actor Boss {
    mut int crashes = 0

    pub void Go() {
        name = "first"
        w = spawn Worker() on crash (c) {
            crashes = crashes + 1
            print("{name} {crashes}")
            stop
        }

        send w.Fail()
    }

    pub int Crashes() => crashes
}

b = spawn Boss()
b.Go()
print(b.Crashes())
```

Case: [actors/block-crash-order.kz](../corpus/actors/block-crash-order.kz)
```kurz
// `first`'s `Fail` runs before `second`'s, and both before `Other` answers `Go`'s call, so that both blocks stand in `Boss`'s
// inbox ahead of the top-level code's `Ping`, as one thread runs the ready actors first in, first out (U35)

actor Other {
    pub int Ping() => 1
}

actor Worker {
    pub void Fail() {
        throw "worker fails"
    }
}

actor Boss {
    pub void Go(Other other) {
        first = spawn Worker() on crash (c) {
            print("first's block")
            stop
        }

        second = spawn Worker() on crash (c) {
            print("second's block")
            stop
        }

        send first.Fail()
        send second.Fail()
        print(other.Ping())
        print("go ends")
    }

    pub int Ping() => 2
}

o = spawn Other()
b = spawn Boss()
b.Go(o)
print(b.Ping())
```

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)

Case: [actors/waitline-text.kz](../corpus/actors/waitline-text.kz)
```kurz
// three chains, each actor busy in one and waiting on another: `C`'s wait closes the loop, and the block of its policy prints
// the text of its `deadlock`, one line for each wait of the loop, from the wait that raised (U63); each line's chain ID, 32
// lowercase hexadecimal digits that differ on every run, is checked and left out, the raiser's being the chain of the crash

// the lines of `detail`, each without the chain ID that ends it, or what of them does not hold
string Waits(string detail, string chain) {
    id = Regex.New(r" in chain [0-9a-f]{32}$")
    lines = detail.Split("\n")
    if !lines[0].EndsWith(" in chain {chain}") {
        return "the first line's chain is not the crash's"
    }

    mut string text = ""
    mut string gap = ""
    for line in lines {
        if !id.IsMatch(line) {
            return "no chain ID: {line}"
        }

        bare = id.Replace(line, "")
        text = "{text}{gap}{bare}"
        gap = "\n"
    }

    return text
}

actor A {
    pub void Ask(B other) {
        print("a got {other.Value()}")
    }

    pub int Value() => 1
}

actor B {
    pub void Ask(C other) {
        print("b got {other.Value()}")
    }

    pub int Value() => 2
}

actor C {
    pub void Ask(A other) {
        print("c got {other.Value()}")
    }

    pub int Value() => 3
}

a = spawn A()
b = spawn B()
c = spawn C() on crash (x) {
    match x.Reason {
        RuntimeError e => {
            print(e.Kind)
            print(Waits(e.Detail, x.Chain))
        }
    }

    restart
}
send a.Ask(b)
send b.Ask(c)
send c.Ask(a)
print(a.Value())
```

Case: [actors/waitline-within.kz](../corpus/actors/waitline-within.kz)
```kurz
// `Right`'s call that closes the loop has a bound of 2 s and still gets `deadlock` at once, not `call-timeout` at its bound (U61)

actor Left {
    pub void Ask(Right other) {
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        n = other.Value() within 2s
        print("right got {n}")
    }

    pub int Value() => 2
}

l = spawn Left()
r = spawn Right() on crash (c) {
    match c.Reason {
        RuntimeError e => print(e.Kind)
    }

    restart
}
send l.Ask(r)
send r.Ask(l)
print(l.Value())
```

Case: [actors/waitline-stop.kz](../corpus/actors/waitline-stop.kz)

### U29 (decided, §6) The decisions

`restart`, `stop` and `escalate` are the statements of the block that decide, and each does what the
same word does after `on crash` (U22, U23, U25): `restart` restarts the actor after the growing pause,
`stop` leaves it stopped, so that a call that waits in its inbox or comes after raises `actor-gone`,
and `escalate` passes its crash up whole, which at the top level ends the process with the child's
error line, the text below it, the trace of the child's stack and the exit code 1 (§6, "Policy at
`spawn`"), the trace being the top-level code's for a `stack-overflow`, the overflowed stack not kept (§6, the owner,
2026-11-17, #570, comment 6961064167, Q4, "Top-level trace (Recommended)"). A decision ends its path, as `return` ends a function's, and each path of the block ends
with one or with a `throw` (U32); a path that decides nothing is `missing-return`, as a lambda's
path that ends with no value is (F3) (§6, the owner, 2026-11-16, #519, comment 6919351399,
"missing-return error"). `restart` there takes no knobs, the default's pause and no limit, as Akka's
`Restart` directive carries none, and a knob after it, a type before the parameter and `return` in
the block, outside a lambda in it, are `syntax` (E4); the three words are statements in the block alone,
where each is the decision before anything but `=`, and the token after the decision ends its statement as L5 ends one, the
end of its line, a `;`, which is `semicolon` (L5), or a `}` that closes a block it stands in, what follows on the line read by
that line's rules, as `else` after the `}` of an `if`'s block is, and any other token after the decision is `syntax` (E4) *(assumed: L5's
end of a statement and its one statement of a block written on one line, read for a decision, which §6 makes a statement)*,
and names elsewhere, before `=` in the block among it, as `send` is (U6), and a lambda inside the
block decides nothing, its words names there *(assumed: as §6 marks it)*. A block can restart, so that its `spawn` keeps the
copy of its arguments that a restart constructs from, as `on crash restart` keeps it (U22).

Case: [actors/block-stop.kz](../corpus/actors/block-stop.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    print("stopped at {c.Place.Line}")
    stop
}
send w.Fail()
print(w.Ping())
```

Case: [actors/block-escalate.kz](../corpus/actors/block-escalate.kz)

Case: [actors/block-decides-nothing.kz](../corpus/actors/block-decides-nothing.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    if c.Place.Line > 1 {
        restart
    }
}
print(w.Ping())
```

Case: [actors/block-restart-knob.kz](../corpus/actors/block-restart-knob.kz)
```kurz
actor Worker {
    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    restart after 1s
}
print(w.Ping())
```

Case: [actors/block-escalate-root.kz](../corpus/actors/block-escalate-root.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    escalate
}
send w.Fail()
print(w.Ping())
```

### U30 (decided, §5, §6) The crash the block reads

The block's parameter is a value of the standard library's `Crashed<R>`, whose fields are `Reason`,
what the child threw, of the type `R`, `Place`, the place that raised it, a `Place(string File, int
Line)` whose `File` is the file the error line names, and `Chain`, the ID of the chain of the message that
crashed (§5, the place and the chain ID fields of the `Crashed` case beside `Reason`), a `string` of its 32
hexadecimal digits in lowercase *(assumed: as §6 marks it, the names, the place's type, as Rust's
`panic::Location`, its file the error line's, and the digits, as OpenTelemetry writes a trace-id, in
lowercase)*. `R` is the union of every type the child's code can throw, found over the whole program,
and of the runtime's error type, `RuntimeError(RuntimeKind Kind, string Detail)`, a member of every
`Reason` (O7; §5, the owner, 2026-10-03), so that a run-time error of chapter 12 reaches the block as
a `RuntimeError` of its kind, with the detail its error line names, the kinds being `Overflow`, `DivideByZero`,
`IndexOutOfRange`, `ReversedRangeAtRunTime`, `InvalidOrderingAtRunTime`, `StackOverflow`, `PrintFailed`, `AllocationFailed`,
`NotCharBoundary`, `RandomFailed`, `Deadlock`, `CalleeCrashed`, `ActorGone`, `RestartLimit`, `CallTimeout`, `CpuLacksSet`,
`SettingsUnpublished`, `CheckFailed`, `InvalidInput` and `AdvanceNegative`, each run-time row's id of chapter 12 as one capitalised word for each of its
parts, in the table's order, `thrown` left out, and `InvalidArgument` after them, for `invalid-argument`
(S13), which chapter 12 names outside its table *(assumed: its place after the table's, which the order
of the table does not give)*; the child's code is its
construction and its methods, what they run, the construction of each actor they spawn, and the code
of each actor they spawn under `escalate` or a block, whose crash can pass up to the child, with
what that block throws (U32) *(assumed: as §5 marks it, the type's name, its kinds and its place,
and the order of `R`'s cases, the program's own first and the runtime's last)*. `RuntimeError` and
`RuntimeKind` are names every file sees, as the standard library's `HttpError` is (W1) *(assumed: as
§6 marks it)*. The thrown value is moved out of the child's heap into the supervisor's, with
everything it reaches, before the child's heap is freed (O7; §5, the owner, 2026-10-04), and the
`Crashed` value goes when the block's run ends.

Case: [actors/block-reason.kz](../corpus/actors/block-reason.kz)

Case: [actors/block-stop.kz](../corpus/actors/block-stop.kz)

Case: [actors/block-type.kz](../corpus/actors/block-type.kz)

Case: [actors/timeout-crashed-early.kz](../corpus/actors/timeout-crashed-early.kz)

Case: [actors/block-reason-atomic-order.kz](../corpus/actors/block-reason-atomic-order.kz)
```kurz
actor Reader {
    pub void Show(MemoryOrder order) {
        mut long word = 5
        cell = AtomicLong.FromPtr(&word)
        raw {
            print(cell.Load(order))
        }
    }

    pub int Half(int n) => n / 2
}

r = spawn Reader() on crash (c) {
    match c.Reason {
        RuntimeError e => print("{e.Kind} at {c.Place.Line}")
    }

    restart
}
send r.Show(MemoryOrder.Release)
send r.Show(MemoryOrder.Acquire)
print(r.Half(4))
```

### U31 (assumed, §6) What the block reads

A block at a `spawn` in an actor's code reads and assigns the fields of that actor, the supervisor,
as a message of it does, since it runs as one (U28), and takes the variables of the function around
it that it names by value at the `spawn`, as a lambda takes them (F15), as Rust's `move` closure
does; a block after an actor's name, where no supervisor is known, reads its parameter and what a
top-level function reads, and a block in the top-level code takes the top-level variables it names by
value at the `spawn`, as a lambda takes them (F15), assigning one being `capture-assign` (F11) (§6, assumed
there, as Akka Typed's `Behaviors.supervise` takes no state of the actor that spawns). A block at a `spawn` in
a function declared at the top level is read as a lambda of that function, taking the variables of the
function it names by value and reading no field of an actor *(assumed: as F15 reads a lambda there, which §6
does not name)*. A block takes the variables it reads after the `spawn`'s arguments ran, F12's left to right with the
block written last (§6, the owner, 2026-11-17, #570, comment 6961064167, Q2, "After the arguments (Recommended)").

Case: [actors/block-supervisor-state.kz](../corpus/actors/block-supervisor-state.kz)

Case: [actors/block-type.kz](../corpus/actors/block-type.kz)

### U32 (decided, §6) A throw in the block

A throw inside the block is the supervisor's own crash, and the child stays stopped (§6, the owner,
2026-11-15, #519, comment 6902210646, Q2): the child is ended with the supervisor's other children
(U21), so that a call that waits in its inbox or comes after raises `actor-gone`, and the
supervisor's crash goes as any crash goes (U19 to U25). The child's crash writes its log line as at
`stop` before the supervisor's crash writes its own (U24) *(assumed: as §6 marks it)*.

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)

Case: [actors/block-throw-root.kz](../corpus/actors/block-throw-root.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

w = spawn Worker() on crash (c) {
    throw "block fails"
}
send w.Fail()
print(w.Ping())
```

### U33 (decided, §6) The block of the top-level code

A block at a `spawn` in the top-level code runs in that code's wait: the wait runs the scheduler in
place (U18), and it runs a pending block before it goes on, and the end of the top-level code drops a
pending block and ends its child (U13) (§6, the owner, 2026-11-16, #519, comment 6919351399, "Root's
wait runs it"). A block at a `spawn` in a function declared at the top level runs as a message of the actor
whose message runs the call, and in the top-level code's wait where the top-level code runs it *(assumed: the
supervisor read as the code that runs the `spawn`, which the answer named for the top-level code's own)*. A wait
of the top-level code on the call that crashed the child ends with `callee-crashed` (U20) before the block runs, and
the process ends with that error, the block dropped with no line.

Case: [actors/block-sample.kz](../corpus/actors/block-sample.kz)

Case: [actors/block-dropped.kz](../corpus/actors/block-dropped.kz)
```kurz
actor Worker {
    pub int Fail() {
        throw "worker fails"
    }
}

w = spawn Worker() on crash (c) {
    print("never")
    restart
}
print(w.Fail())
```

Case: [actors/block-reason.kz](../corpus/actors/block-reason.kz)

Case: [actors/block-throw-root.kz](../corpus/actors/block-throw-root.kz)

Case: [actors/timeout-crashed-early.kz](../corpus/actors/timeout-crashed-early.kz)

### U34 (assumed, §6) Pending blocks and the log line

The blocks pending in one supervisor run in the order of the crashes, and a block still pending at
the supervisor's crash or end is dropped with no line, its child ended with the supervisor's other
children (U21, U13); while its block is pending, the child takes no message, so that a call to it
waits in its kept inbox, and the calls queued in its inbox go on waiting, until the decision (§6, the
owner, 2026-11-16, #519, comment 6926540546, "Keep: it waits"), so that a message of the supervisor, an actor,
that calls its own child once the child's block is queued behind that message gets `deadlock` at that call (U60, U62),
and one whose call already waits in the child's inbox when the child crashes ends only by the call's timeout (§6, the owner, 2026-11-20, #519, comment 7051071630,
T63 2), §6's `call-timeout` at the line of that call, and a wait of the top-level code on that message is `deadlock` (U18) at the line of that wait where no
timeout holds (U64) *(assumed: as §6 marks it)*; and the
block's own calls run in a chain of their own, as a restart's construction does (U22). The log line
of a crash that a block decides (U24) is written at the decision, at `restart` and at `stop`, and
none at `escalate` (U25) (§6, assumed there, as Erlang's supervisor reports a child's exit as it
acts on it).

Case: [actors/block-crash-order.kz](../corpus/actors/block-crash-order.kz)

Case: [actors/block-reason.kz](../corpus/actors/block-reason.kz)

Case: [actors/waitline-pending-block.kz](../corpus/actors/waitline-pending-block.kz)

Case: [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz)

Case: [actors/waitline-into-loop.kz](../corpus/actors/waitline-into-loop.kz)

### U35 (decided, §6, §8) Threads

The actors' messages run on the scheduler's worker threads, as many as the cores the process may use,
a container's CPU quota counted, where neither the line `threads = 4` of a `deploy` block in
`project.kz` nor the environment variable `KURZ_THREADS`, read at the start, sets their count, and
`KURZ_THREADS` wins over the line (§6, §8, the owner, 2026-11-17, #519, comment 6946657505, Q4,
"Both" and "threads = N line (Recommended)"), the line standing in a `deploy` block *(assumed in
§6)*. A value of the line that is not a whole number above 0 is the compile error `threads-line`,
and one of `KURZ_THREADS` ends the start with an error line and the id `threads-variable` *(assumed
in §6: both ids, and the end of the start)*. A whole number is written in digits alone and is no
greater than 2147483647, the greatest `int`, so that an empty `KURZ_THREADS` is a bad value, as tokio
reads `TOKIO_WORKER_THREADS` as a whole number and refuses an empty one; a container's CPU quota counts
its whole cores, rounded down and at least 1, as Rust's `available_parallelism` counts it; and a bad
`KURZ_THREADS` ends the process before its first statement with the line `error: threads-variable
(<the value>)` on standard error and the exit code 1, as a bad `KURZ_LOG` ends it with `log-filter`
(P5) *(assumed in §6)*. A worker thread that the system refuses at its start ends the program with an
error line and an id of its own and the exit code 1, as tokio's `Builder::build` returns an
`io::Error` (§6, the owner, 2026-11-17, #572, comment 6961064484, Q2, "Error line + exit 1
(Recommended)"), so that a program on a machine at its thread limit does not start, where it could
have run on fewer (§6, the cost of that answer). The id is `thread-refused`, its start is the moment
the scheduler starts its worker threads, at the program's first `spawn` or first form that splits its
work, the threads that did start ending with the process, and its line is `error: thread-refused
(<the count of worker threads asked>)` on standard error, with no place and no trace *(assumed in
§6)*. The main thread runs the top-level code alone, and
sleeps while it waits (U18; §6, Q3), except in a case marked `// threads: 1`, where its wait runs the ready actors and
no worker thread runs (§6, the owner, 2026-11-17, #570, comment 6961064167, Q1). A message parked in a wait goes on on the thread that parked it,
and an actor between messages moves to any thread (§6, the owner, 2026-11-17, #519, comment
6946657505, Q2, "Stays on its thread (Recommended)"). A message that runs on another thread when the
top-level code ends runs to its end or its wait, a wait then dropped as a parked message is (U13), and
the process ends after it; a supervisor that is running acts on its child's crash once its own message
ends or waits (U22) *(assumed in §6)*. In a case marked `// threads: 1` (chapter 0) one thread runs the
actors that are ready first in, first out, the top-level code's wait running them (§6, the owner,
2026-11-17, #570, comment 6961064167, Q1, "FIFO, as the build (Recommended)").

No case: for the count of the threads, since a case is a single file with no `project.kz` (chapter 0) and
gives its program no environment, as `log-filter`'s place in chapter 12 says, and cannot make its system
refuse a worker thread, as `thread-refused`'s place there says, the thread that runs a message shows in no
output, and the two outcomes a program can see, a message that runs on another thread when the top-level
code ends and the moment a running supervisor acts on a crash, depend on the timing between threads, which
a case's output cannot fix; the order of one thread has the cases below.

Case: [actors/block-escalate.kz](../corpus/actors/block-escalate.kz)

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)

Case: [actors/block-order.kz](../corpus/actors/block-order.kz)

Case: [actors/block-supervisor-state.kz](../corpus/actors/block-supervisor-state.kz)

Case: [actors/block-crash-order.kz](../corpus/actors/block-crash-order.kz)

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)

Case: [actors/waitline-text.kz](../corpus/actors/waitline-text.kz)

### U36 (decided, §6) A call that waits has a bound

Every call through a reference that waits has a bound, 5 seconds where no `within` names another (U38, U39), as Elixir's
`GenServer.call` waits 5 seconds by default (§6, "Every call has a timeout"). A call past its bound raises `call-timeout` in the
caller, at the line of that call, with no parameter (§6, the owner, 2026-10-30, "Deadline on chain ID"; the id: the owner,
2026-11-13, #519, comment 6847932993, Q8, "These ids (Recommended)"). The caller cannot handle it: the raise is the crash of the
actor that waits (U19), each caller that waits on that actor up the chain crashing in its turn, and in the top-level code it ends
the process with the error line, the trace and the exit code 1 (§6, "A caller cannot handle a callee's crash, timeout or
unreachability"). The top-level code's calls have the same bound *(assumed: as "Every call has a timeout" reads, T2 of comment
6967147952)*. A bound is counted on the monotonic clock in whole milliseconds, a part of a millisecond rounded up *(assumed: as
comment 6967147952 took it with no question)*. A deadline has passed once the monotonic clock reaches it, as Tokio's timer fires
at its deadline (not checked at source) *(assumed in §6)*. A result set before the caller reads its deadline is the call's
result, as Tokio's `Timeout::poll` polls its future before its timer (not checked at source) and Erlang's `receive` takes a
message that stands before its `after` (not checked at source) *(assumed in §6)*, so that in a case marked `// threads: 1`
(U35), where a caller reads its deadline between messages alone, a callee that runs past the bound and answers with no call and
no wait after the deadline gives the call its result; a crash or an end of the callee is held to the deadline at the time it
was set (U43).

Case: [actors/timeout-default.kz](../corpus/actors/timeout-default.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Worker {
    pub void Slow() {
        Hold(5200ms)
        print("slow done")
    }

    pub int Get() => 1
}

w = spawn Worker()
send w.Slow()
print(w.Get())
```

Case: [actors/timeout-send.kz](../corpus/actors/timeout-send.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Ticker {
    mut int ticks = 0

    pub void Tick() {
        ticks = ticks + 1
    }

    pub int Ticks() => ticks
}

actor Front(Ticker T) {
    pub int Go() {
        Hold(200ms)
        send T.Tick()
        return 1
    }
}

t = spawn Ticker()
f = spawn Front(t)
within 100ms {
    print(f.Go())
}

print(t.Ticks())
```

### U37 (decided, §6) The deadline of the chain

The chain ID carries a deadline, the one of the call at the chain's root: a call made inside the chain gets the time left, a call
or a wait past the deadline raises `call-timeout` in the caller (U36), and an inbox drops a message whose deadline passed before
its callee starts it (§6, the owner, 2026-10-30, "Deadline on chain ID", against a fixed timeout for each call with no cancel, and
against an explicit token as a parameter, as C#'s `CancellationToken`). The root's deadline caps every message of the chain,
and the message of each call carries the smaller of the call's own bound and the time its chain has left (U44) *(assumed: T8 of
comment 6967625156)*. A `send` of the chain carries the chain's deadline and waits in the inbox behind the message it was sent
from (U17) (§6, the owner, 2026-11-14, #519, comment 6880404917, Q13). A message whose chain's deadline passes while it runs
goes on, nothing interrupting it: its first call or wait past the deadline
raises `call-timeout` in it, the crash of its actor, which its supervisor handles (U22) *(assumed: T1 of comment 6967147952)*,
and a message that makes neither runs to its end, its result the call's where it was set before the caller reads its deadline
(U36, assumed in §6), and going nowhere where the caller already raised `call-timeout`, as the call of a caller that crashed runs
(§6, the owner, 2026-10-30, "Run it, drop the reply"). The inbox drops a message with no line *(assumed: T3 of comment
6967147952)*: a `send` has no caller that waits, and the caller of a call raises `call-timeout` (U36). The deadline a call's own
message carries is U44's, and the deadline of a chain that no call starts is U45's.

Case: [actors/timeout-callee-crash.kz](../corpus/actors/timeout-callee-crash.kz)

Case: [actors/timeout-capped.kz](../corpus/actors/timeout-capped.kz)

Case: [actors/timeout-send.kz](../corpus/actors/timeout-send.kz)

Case: [actors/timeout-chain-left.kz](../corpus/actors/timeout-chain-left.kz)

### U38 (decided, §6) The block `within`

`within d { ... }` is a statement whose entry sets one deadline, `d` from then, for every call inside it, each call taking the
smaller of that deadline and the time its chain has left, so that ten calls in a row share the block's time, as Kotlin's
`withTimeout` (not checked at source) and a C# `CancellationTokenSource(TimeSpan)` passed down bind a group of calls (§6, the
owner, 2026-11-13, #519, comment 6850539879, "One deadline for the block (Recommended)", against `d` for each call). The
deadline binds the calls that wait, and their waits, made by the code the block runs, in the functions it calls too, as Tokio's
`timeout` cancels its future with every wait in it (checked in §6 on 2026-11-13, at Tokio 1.54.1) *(assumed in §6)*. The
message of such a call carries the call's own deadline (U44), which caps the calls its callee makes, each taking its own bound
(U36, U39) *(assumed in §6: T8 of comment 6967625156)*. A `send` in the block carries the chain's own deadline (U37), and a message of
another chain or a policy's block that runs in the top-level code's wait is not bound by it (U44, U45). The waits it binds are
those of calls on actors: a socket's wait keeps its own bound of §12 (S9), which no `within` bounds *(assumed in §6: whether a
block bounds a socket's wait stands open in §14, T5 of comment 6967147952)*. The deadline
is set at the block's entry and put back at every way out of the block *(assumed in §6, 2026-11-13)*. A block inside another takes
the smaller of its own deadline and the enclosing block's, as Tokio's outer `timeout` still ends a future that holds an inner one
(not checked at source) *(assumed in §6)*.

Case: [actors/timeout-block.kz](../corpus/actors/timeout-block.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub int Get() => 1
}

// a call that the block's deadline binds, in a function the block calls
int Ask(Store store) => store.Get()

s = spawn Store()
within 100ms {
    print(s.Get())
    Hold(150ms)
    print(Ask(s))
}
```

Case: [actors/timeout-capped.kz](../corpus/actors/timeout-capped.kz)

### U39 (decided, §6) A clause on a call and on a declaration, and their order

A clause on a call, `store.Get(id) within 2s`, bounds that call, and a clause on a method's declaration,
`pub User Get(int id) within 2s`, bounds every call of the method through a reference, set by the callee for every caller, as a
gRPC service's default (§6, the owner, 2026-11-13, #519, comment 6847932993, Q11, "All three and 1 wins over 2 and 2 wins over
3"). Where more than one form names a bound for one call, the block's bound wins over the call's clause and the call's clause
over the declaration's, and every bound stays capped by the time the chain has left (U37) *(assumed in §6: the reading of the
answer's words, said back on 2026-11-13)*. The bound of a call's clause runs after the call's arguments, in the order written at
the call, as F12 evaluates the arguments *(assumed in §6: proposed with this record and not put to the owner)*, and it runs at
every call it stands on, under a `within` block whose deadline wins over it too *(assumed: a reading of the build, proposed with
this record and not put to the owner)*. The bound of either clause is read as U40 reads a block's, so that a call's bound of
another type, `s.Get() within 2`, is `type-mismatch` *(assumed in §6: U40's reading, the three forms there writing a duration and
naming no other type)*. Where a clause may stand, and what its operand may be, are U41's.

Case: [actors/within-clauses.kz](../corpus/actors/within-clauses.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub void Busy() {
        Hold(150ms)
    }

    // each call of `Get` through a reference is bounded by 100 ms where nothing else bounds it
    pub int Get() within 100ms => 1
}

s = spawn Store()
send s.Busy()
print(s.Get() within 1s)
send s.Busy()
within 1s {
    print(s.Get() within 50ms)
}

send s.Busy()
print(s.Get())
```

Case: [actors/within-clause-under-block.kz](../corpus/actors/within-clause-under-block.kz)
```kurz
// the bound of a call's clause runs at the call, under a `within` block whose deadline wins over it too
duration Bound() {
    print("bound")
    return 50ms
}

actor Store {
    pub int Get() => 1
}

s = spawn Store()
within 1s {
    print(s.Get() within Bound())
}
```

Case: [actors/within-clause-not-duration.kz](../corpus/actors/within-clause-not-duration.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
print(s.Get() within 2)
```

### U40 (assumed, §6) The word `within` and the bound's value

`within` is a word only where it stands, as `on crash`, `attempt` and `vouch` are, so that a variable may be named `within`
(§6, the owner, 2026-11-13, #519, comment 6847932993, Q10, "Any order + contextual (Recommended)") *(assumed in §6: `within`
among those words)*. At the start of a statement `within` is the word where an expression, read as a condition is, follows it
and a `{` follows that expression, on the line where the expression ends or, with nothing but line breaks between, on a following
line, the latter `braces-required`, a comment after the bound, a line of a comment and an empty line counting as line breaks;
otherwise it is a name, so that `within(3)` is a call, `within + 1` a sum and
`within = 3` an assignment *(assumed in §6: the build's reading, proposed there and not put to the owner)*. The bound is a
`duration`, read as a condition is (C3): a value of another type there is
`type-mismatch`, a `duration?` is `nullable-unchecked` (N2), and a call with a `void` success is `syntax` (O10); and a `{`
of the block on a line after the bound's is `braces-required` at the line where the bound ends, as C1 has it of an `if`
*(assumed in §6: proposed there and not put to the owner)*. A bound of zero or less is refused, as the sockets refuse such a
time (S9), against a deadline that has passed (§6, the owner, 2026-11-20, #519, comment 7051492844, "T4 (2)"). A constant bound
of 0 ns or less, a constant as T25 reads one, a literal or one folded from literals, `0s`, `-1s`, `(0s)` or `1s - 1s`, is the
compile error `bound-not-positive` at the line where the bound starts, whose text names the form, the bound as written and the rule, "the bound of
`within` is 0s; a bound is positive (U40)"; every other bound, a variable's name among them whatever it was given, is read at run time, at the
block's entry, before its first statement, so that a block with no call in it is refused too, and one of 0 ns or less raises
`invalid-input` at the line where the bound starts, the crash of the code that runs it, at the top-level code the end of the process with the
exit code 1 (U19), its value standing on the line after the error line, "the bound
of `within` is 0ms; a bound is positive", while 1 ns is a bound *(assumed in §6: T77, T78 and T81 of comment 7055305138, T79 of
it as corrected on 2026-11-21, and the product pass's reading of zero, comment 7055304984)*. A bound of another type has its
`type-mismatch` and a constant its own error, `inexact-literal` or `constant-overflow`, alone (E1); and the value after the error
line is written as a duration's text is (A12), as the position of `not-char-boundary` is written (X2) *(assumed in §6: readings of
the build, proposed there and not put to the owner)*. Both name the line where the bound starts, for every form of the bound, as
an error of the whole reports its first line (E1): a bound written `3s -`, `1s -`, `2s` over three lines reports its first, and
not the line of its last operator (§6, the owner, 2026-11-24, issue 39, comment 7144769568, "Start line"). A
bound whose deadline would pass the largest time the clock holds sets no deadline of its own: the calls it bounds wait with no
bound of their own in place of U36's 5 seconds, each capped by the time its chain has left (U37) and by the deadline of a block
it stands in (U38), as Tokio's `timeout` falls back on a far future where its sum overflows (not checked at source)
*(assumed in §6)*.

Case: [actors/timeout-zero.kz](../corpus/actors/timeout-zero.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
print(s.Get())
within 0s {
    print(s.Get())
}
```

Case: [actors/within-negative.kz](../corpus/actors/within-negative.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
within -1s {
    print(s.Get())
}
```

Case: [actors/within-folded-zero.kz](../corpus/actors/within-folded-zero.kz)
```kurz
// a bound folded from literals is a constant, as T25 reads one, and one of zero is refused where it is written

actor Store {
    pub int Get() => 1
}

s = spawn Store()
within 1s - 1s {
    print(s.Get())
}
```

Case: [actors/within-computed-zero.kz](../corpus/actors/within-computed-zero.kz)
```kurz
// a bound computed at run time is read at the block's entry: one of 1 s runs, and one of zero is refused

actor Store {
    pub int Get() => 1
}

s = spawn Store()
one = 2s - 1s
within one {
    print(s.Get())
}

zero = one - 1s
within zero {
    print(s.Get())
}
```

Case: [actors/within-zero-no-call.kz](../corpus/actors/within-zero-no-call.kz)
```kurz
// the bound is read at the block's entry, before its first statement, so that a block with no call in it is refused too

zero = 1s - 1s
within zero {
    print("inside")
}
```

Case: [actors/within-lines-zero.kz](../corpus/actors/within-lines-zero.kz)
```kurz
// a bound over several lines reports the line where it starts, as an error of the whole does (E1), not its last operator's

actor Store {
    pub int Get() => 1
}

s = spawn Store()
within 3s -
    1s -
    2s {
    print(s.Get())
}
```

Case: [actors/within-lines-run.kz](../corpus/actors/within-lines-run.kz)
```kurz
// a bound read at run time over several lines reports the line where it starts, not its last operator's

one = 2s - 1s
within one +
    one -
    2s {
    print("inside")
}
```

Case: [actors/within-name.kz](../corpus/actors/within-name.kz)
```kurz
within = 3
print(within + 1)
```

Case: [actors/within-not-duration.kz](../corpus/actors/within-not-duration.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
within 2 {
    print(s.Get())
}
```

### U41 (assumed, §6) Where a clause stands, and its operand

A call's clause stands on a call of an actor's method through a reference that waits (U4) alone: on a call after `send`, which
waits for nothing (U6), on a call of a function, of a class's method or of an actor's own method inside it, a direct call (§6,
2026-11-13, Q14, "Direct call"), and after a `spawn`, it is `syntax`, as `send` before what is no call of an actor's method is
(U14) and as `tokio::spawn` and Elixir's `cast` take no timeout (not checked at source); and a declaration's clause stands on a
message, a `pub` method
of an actor (U9), alone, on the declaration of a function, of a class's, a data type's or an interface's method, or of an actor's
method without `pub` being `syntax` *(assumed: T11 of comment 6967625156)*. An actor with a part the checker refuses or does not
read, a constructor in its body (U2) or a `static` method (U1), has that one diagnostic, and the clauses of its declarations add
none (E1); and the bound of a clause that is `syntax` where it stands, and that of a call that reported its own error, is read with
no diagnostic of its own, a variable it reads counting as read, so that `send s.Run() within 2` has its `syntax` alone and
`s.Missing() within 2` its `unknown-name` alone (E1) *(assumed: a reading of the build, proposed with this record and not put to
the owner)*. The operand of a call's clause is a unary expression,
as a constant type parameter's default is read (T35), so that `a.F() within d * 2` reads as `(a.F() within d) * 2`, a computed
bound standing in parentheses, and the clause stands before a postfix `else` (O5); Rust has no such form (not checked at source)
*(assumed: T12 of comment 6967625156)*. An `else` after a call inside the operand is that call's, as a unary expression takes its
own postfix forms (T35) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. A call takes one
clause, a second `within` after its clause being `syntax`, and outside brackets the clause stands on the line where the call
ends: a line break ends the call first, so that a `within` at the start of the next line begins a statement, read as U40 reads
one, the word where a `{` follows its expression and a name otherwise, so that `within 2s` there with no block after it is
`syntax` at its line, `within` being a name there; inside a
`(` or a `[`, where a line break ends no statement (L4), it may stand on a line after the call's *(assumed: a
reading of the build, proposed with this record and not put to the owner)*. The bound of a call's clause of zero or less is refused
as U40 refuses a block's, a constant as `bound-not-positive`, "the bound of the call's `within` is 0ms; a bound is positive
(U41, U40)", and every other bound at run time as `invalid-input`, each at the line where the bound starts (U40), read after the call's arguments and before the call is queued, where its actor is looked up, so that over an actor that
ended it is `invalid-input` and not `actor-gone` (U43), and under a block whose deadline wins over the clause too (U39)
*(assumed in §6: T80 and T81 of comment 7055305138, and T79 of it as corrected on 2026-11-21)*. The operand of a declaration's clause is a number
literal alone, its sign included, any other operand being `syntax`, its type read as U40 reads a bound. That the operand is a
literal alone, any other operand `syntax`, as a knob of a policy is read (U26), is T13's *(assumed: T13 of comment 6967625156,
whose option names a duration literal)*; that the literal is a number literal of any type, its sign included, as a knob of a
policy takes one (U26), and is read as U40 reads a bound, so that a number literal of another type, `within 2`, is
`type-mismatch`, as a knob's is (U26), and not `syntax`, is the build's *(assumed: a reading of the build, proposed with this
record and not put to the owner)*; and a `duration` literal of zero or less there is `bound-not-positive`, "the bound of the
declaration's `within` is -1s; a bound is positive (U41, U40)", as U40 reads one and as a knob refuses a value of 0 or less
(U26), the one error a declaration's `duration` bound of zero or less meets, while one of another type has its `type-mismatch`
alone (E1) *(assumed in §6: T80 of comment 7055305138)*.

Case: [actors/within-clause-send.kz](../corpus/actors/within-clause-send.kz)
```kurz
actor Store {
    pub void Run() {
        print("run")
    }
}

s = spawn Store()
send s.Run() within 2s
```

Case: [actors/within-clause-function.kz](../corpus/actors/within-clause-function.kz)
```kurz
int Twice(int x) => x * 2

print(Twice(2) within 2s)
```

Case: [actors/within-declaration-private.kz](../corpus/actors/within-declaration-private.kz)
```kurz
actor Store {
    int Get() within 2s => 1

    pub int Twice() => Get() + Get()
}

s = spawn Store()
print(s.Twice())
```

Case: [actors/within-clause-operand.kz](../corpus/actors/within-clause-operand.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
// the clause takes a unary expression: `* 2` applies to the call's result
print(s.Get() within 1s * 2)
print(s.Get() within (1s + 1s) + 2)
```

Case: [actors/within-declaration-operand.kz](../corpus/actors/within-declaration-operand.kz)
```kurz
actor Store {
    pub int Get() within 1s + 1s => 1
}

s = spawn Store()
print(s.Get())
```

Case: [actors/within-declaration-not-duration.kz](../corpus/actors/within-declaration-not-duration.kz)
```kurz
actor Store {
    pub int Get() within 2 => 1
}

s = spawn Store()
print(s.Get())
```

Case: [actors/within-clauses.kz](../corpus/actors/within-clauses.kz)

Case: [actors/within-clause-zero.kz](../corpus/actors/within-clause-zero.kz)
```kurz
actor Store {
    pub int Get() => 1
}

s = spawn Store()
print(s.Get() within 0ms)
```

Case: [actors/within-clause-lines-zero.kz](../corpus/actors/within-clause-lines-zero.kz)
```kurz
// the bound of a call's clause stands in brackets where it is computed, and over several lines it reports the line where it starts (U40)

actor Store {
    pub int Get() => 1
}

s = spawn Store()
print(s.Get() within (2s -
    1s -
    1s))
```

Case: [actors/within-clause-lines-run.kz](../corpus/actors/within-clause-lines-run.kz)
```kurz
// the bound of a call's clause read at run time over two lines reports the line where the bound starts, not its member's (U40)

data Limits(duration Zero)

actor Store {
    pub int Get() => 1
}

s = spawn Store()
cfg = Limits(0s)
print(s.Get() within cfg
    .Zero)
```

Case: [actors/within-declaration-zero.kz](../corpus/actors/within-declaration-zero.kz)
```kurz
actor Store {
    pub int Get() within 0s => 1
}

s = spawn Store()
print(s.Get())
```

Case: [actors/within-clause-zero-gone.kz](../corpus/actors/within-clause-zero-gone.kz)

### U42 (assumed, §6) A wait of the top-level code with no message to run while a deadline stands

A deadline of a call that waits in a message is nothing left to run (U18): a wait of the top-level code that finds no message to
run and no restart's pause ends the process with `deadlock` at once, as U18 reads, and the deadline of a call parked in a message
passes only while other messages run or a restart's pause runs, the top-level code then sleeping to the first of that pause, its
own deadline and the earliest deadline of a parked call *(assumed: detection first, as U18 (2026-11-15) was chosen after
"Deadline on chain ID" (2026-10-30) and §6's layer 3, "Instant runtime detection", leaves to the timeout what detection cannot
see, T6 of comment 6967625156)*, against a timer counted as work, as Tokio's runtime sleeps to its earliest timer (not checked
at source), under which no program would reach `deadlock`. So [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz) ends as
`deadlock` at once, though the call of `Boss` waits under its deadline.

Case: [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz)

### U43 (assumed, §6) A caller whose callee crashed or ended once the caller's deadline had passed

A call that waits gets `callee-crashed` where its callee crashed (U20) and `actor-gone` where it ended; where the crash or the
end was set while the call waits, once the caller's own deadline had passed, it raises `call-timeout`, its own deadline, the
time the crash or the end was set held to the deadline and not the time the caller reads it, as a gRPC client of Rust (tonic)
reports its own deadline (not checked at source), so that a chain of three calls whose third passed the deadline of the top-level
code's call ends at the top-level code as `call-timeout` *(assumed: as §6 names `call-timeout` "a call past its bound or its
chain's deadline", T7 of comment 6967625156, "the earlier of the two")*, against `callee-crashed` or `actor-gone` as U20 reads,
the timeout standing in the log line of the actor that raised it (U24). A deadline set by a `within` inside a message ends that
message's actor with `call-timeout` and its caller with `callee-crashed` where that caller's own deadline had not passed when the
crash was set (U20, U44). A result set before the caller reads its deadline is the call's result (U36), and a crash or an end set
before the deadline is `callee-crashed` or `actor-gone` though the caller reads it after. A call queued past its deadline to an
actor that had ended before the call was queued raises `actor-gone`, its actor looked up before its deadline is compared, so that
a call to such an actor in a block whose deadline has passed is `actor-gone`, as Tokio's `Timeout::poll` polls its future before its timer (not checked at source)
*(assumed: T14 of comment 6972461413)*, against `call-timeout` first, as a gRPC client fails a call whose deadline passed before
it starts (not checked at source).

Case: [actors/timeout-crashed.kz](../corpus/actors/timeout-crashed.kz)

Case: [actors/timeout-crashed-early.kz](../corpus/actors/timeout-crashed-early.kz)

Case: [actors/timeout-crashed-before.kz](../corpus/actors/timeout-crashed-before.kz)

Case: [actors/timeout-chain-nested.kz](../corpus/actors/timeout-chain-nested.kz)

Case: [actors/timeout-gone-queued.kz](../corpus/actors/timeout-gone-queued.kz)

### U44 (assumed, §6) The deadline a call's message carries

A call's message carries the call's own deadline, the smaller of its bound (U36, U38, U39) and the time its chain has left (U37), and
the callee's calls and its inbox read it, as a gRPC client of Rust (tonic) sends its call's timeout (not checked at source), so
that a short bound on one call ends the callee's calls past it, with the callee's crash (U37) *(assumed: as §6 has a block's
deadline bind every wait made while the block runs, T8 of comment 6967625156)*, against the deadline of the chain's root, the
caller's shorter bound holding its own wait alone, as an actor that Tokio spawned runs on after its caller's `timeout` ended
(not checked at source).

Case: [actors/timeout-carried.kz](../corpus/actors/timeout-carried.kz)

### U45 (assumed, §6) A chain that no call starts, and a `send` past its deadline

A chain that a `send` of the top-level code starts (U17), the chain of a restart (U22) and the chain of a policy's block (U28)
carry no deadline, each call the chain makes bounded on its own (U36), as `tokio::spawn` and Elixir's `cast` carry none (not
checked at source): a `send` inside a `within` of the top-level code carries none either, and a block that runs in the wait of
the top-level code (U33) calls under no deadline of that code's `within` *(assumed: T9 of comment 6967625156)*. A `send` made
past its chain's deadline raises nothing: its message is dropped with no line (U37), as a send on Tokio's channel reads no
deadline (not checked at source) *(assumed: T10 of comment 6967625156)*, against `call-timeout` at the `send`, `send` read as a
call.

Case: [actors/timeout-send.kz](../corpus/actors/timeout-send.kz)

### U46 (assumed, §6, §5) `attempt { ... }` and its value

`attempt { ... }` runs its block as a one-shot child actor whose supervisor is the actor whose code runs it, or the
top-level code (U49); the child's exception arrives as the union case `Crashed`, and the parent lives: an actor never
catches its own exception, only its child's death, so that §5's "no `catch`" holds (§6). The value of an `attempt` is a
union, the block's value, its success, then `Crashed<R>`, read by §5's rules as a call's union is: a postfix `else`
handles the cases it names and the others propagate, a case that propagates is listed in the function's signature, the
top-level code handles every case (`unlisted-case`), and a written union type keeps the cases *(assumed: T18 of comment
6980152255, built at the default while it waits for the owner)*. The case is U30's `Crashed<R>`, with its `Reason`,
`Place` and `Chain`, and `R` is the union of every type the block's code can throw, found over the whole program as an
actor's `Reason` is (§5), and `RuntimeError`; what the block throws is the child's crash, which leaves neither the
function around the `attempt` nor the `Reason` of the actor whose code runs it *(assumed: the small defaults of comment
6980152255, not asked)*. Every file sees `Crashed`, as it sees `RuntimeError` (U30), so that a signature names the
case, and a file's own type of that name is `redeclared` (V9); in the arms of an `attempt`'s `else`, `Crashed` written
with no type arguments names that case, as the record's sample in §6 writes `Crashed c` *(assumed: a reading of the
build, proposed with this record and not put to the owner)*. `retry { ... }`, a loop over `attempt`, is `not supported`:
how its attempts' calls are told apart is open (§14).

Case: [actors/attempt-crash.kz](../corpus/actors/attempt-crash.kz)
```kurz
data Lost(int Code)

int Take(int n) {
    if n == 7 {
        throw Lost(n)
    }

    xs = [1, 2]
    return xs[n]
}

// the crash of the block is held as the case `Crashed`, whose `Reason` is what `Take` throws or the runtime's error
int Read(int n) {
    v = attempt { Take(n) } else {
        Crashed c => {
            match c.Reason {
                Lost l => print("lost {l.Code}")
                RuntimeError e => print(e.Kind)
            }

            -1
        }
    }

    return v
}

print(Read(7))
print(Read(5))
print(Read(0))
```

Case: [actors/attempt-success.kz](../corpus/actors/attempt-success.kz)
```kurz
v = attempt { 1 + 2 } else {
    Crashed c => 0
}
print(v)
name = "kurz"
s = attempt { "hello {name}" } else {
    Crashed c => "crashed"
}
print(s)
```

Case: [actors/attempt-propagates.kz](../corpus/actors/attempt-propagates.kz)
```kurz
data Bad(string Why)

int Check(int n) {
    if n > 9 {
        throw Bad("too big")
    }

    return n
}

// with no `else`, the case `Crashed` leaves `Read` through its signature, as a call's case does
int | Crashed<Bad | RuntimeError> Read(int n) {
    v = attempt { Check(n) }
    return v + 6
}

r = Read(9) else {
    Crashed<Bad | RuntimeError> c => 0
}
print(r)
q = Read(10) else {
    Crashed<Bad | RuntimeError> c => {
        print(c.Place.Line)
        0
    }
}
print(q)
```

Case: [outcomes/else-throw-void-attempt.kz](../corpus/outcomes/else-throw-void-attempt.kz)
```kurz
data Big(int N)

void Work(int n) {
    if n > 1 {
        throw Big(n)
    }

    print("worked {n}")
}

// a bare `throw` in the arm `else` over a block whose value is `void` throws the case `Crashed` that reached the arm
void Run(int n) {
    attempt { Work(n) } else {
        else => throw
    }
}

// the throw is a crash of the outer child, whose `Reason` holds that case
void Read(int n) {
    attempt { Run(n) } else {
        Crashed c => {
            match c.Reason {
                Crashed<Big | RuntimeError> d => print("inner {d.Reason} at {d.Place.Line}, outer at {c.Place.Line}")
                else => print("other {c.Reason}")
            }
        }
    }
}

Read(1)
Read(2)
```

### U47 (assumed, §6) What the block of an `attempt` takes

The block gets values only and cannot touch the parent's `mut` state, so that nothing half-changed survives (§6). It takes
values, immutable class instances and actor references freely, and a mutable class instance only with `copy` before it,
for a deep copy, or `move`, else the compile error `capture-needs-word`, at the name; an assignment of a variable it takes
is `capture-assign`, as in any lambda (F11); and a field of the actor whose code runs the `attempt` that the block names
is taken by value at the block's start, as a variable is, so that an assignment of it is `capture-assign` too. What it
takes is taken by value at its start, as a lambda takes it (F15), and copied into the child's heap as a message's
arguments are (U12) *(assumed: T19 of comment 6980152255, built at the default while it waits for the owner)*. A pointer (R10)
or a view of an atomic type (R19) that the block takes, or a value that reaches one, is the compile error `pointer-crosses`
at its first read in the block, and so is a value of the block that reaches one, at the `attempt`, as for a message's
argument and result (U12; §6, the owner, 2026-11-21, #582, comment 7062996932, Q25 1). A field of
an instance of a class that is no actor, whose method runs the `attempt`, is taken so too *(assumed: a reading of the
build, proposed with this record and not put to the owner)*.

Case: [actors/attempt-capture-needs-word.kz](../corpus/actors/attempt-capture-needs-word.kz)
```kurz
class Cart(mut int Items)

int Count(Cart c) => c.Items

// the block takes the mutable instance `cart` with `copy` before it, and bare it is the error
cart = Cart(2)
n = attempt { Count(copy cart) } else {
    Crashed c => 0
}
m = attempt { cart.Items } else {
    Crashed c => 0
}
print(n + m)
```

Case: [actors/attempt-capture-assign.kz](../corpus/actors/attempt-capture-assign.kz)
```kurz
// the block takes `tries` by value, as a lambda does, and cannot assign it
mut tries = 0
v = attempt {
    tries = tries + 1
    return tries
} else {
    Crashed c => 0
}
print(v + tries)
```

Case: [actors/attempt-field.kz](../corpus/actors/attempt-field.kz)
```kurz
// `count` is taken by value at the block's start: the call back into `Counter`, which the wait of its `attempt` lets in,
// changes the field and not the block's value
actor Counter {
    mut int count = 5

    pub void Bump() {
        count = count + 1
    }

    pub int Peek() {
        me = this
        v = attempt {
            me.Bump()
            return count
        } else {
            Crashed c => -1
        }

        return v * 10 + count
    }
}

k = spawn Counter()
print(k.Peek())
```

Case: [actors/attempt-pointer-crosses.kz](../corpus/actors/attempt-pointer-crosses.kz)
```kurz
int Look(long* q) => 1

// the block takes the value of `word`, and the pointer `ptr` is the error, as at a message (U12)
mut long word = 5
ptr = &word
n = attempt { word > 1 } else {
    Crashed c => false
}
m = attempt { Look(ptr) } else {
    Crashed c => 0
}
print(n)
print(m)
```

### U48 (assumed, §6) `return` in an `attempt`

`return` in the block ends the block with its value, as `return` in a lambda's body ends the lambda (F7), and a block of
one expression gives that expression's value; `return` in an arm of its `else` leaves the function around it, as the
record's sample returns `ImportFailed(c.Reason)` *(assumed: T20 of comment 6980152255, built at the default while it waits
for the owner)*.

Case: [actors/attempt-return.kz](../corpus/actors/attempt-return.kz)
```kurz
data Bad(string Why)

int F(int n) {
    if n > 0 {
        throw Bad("big")
    }

    return n
}

// `return` in the block gives the block's value, an `int`, and in the arm leaves `Read` with its `string`
string Read(int n) {
    v = attempt {
        if n > 1 {
            return 2
        }

        return F(n)
    } else {
        Crashed c => return "crashed"
    }

    return "{v}"
}

print(Read(1))
print(Read(2))
print(Read(0))
```

### U49 (assumed, §6) The chain, the deadline and the wait of an `attempt`

The child runs a call of its parent's chain: its message carries the chain's ID and the deadline the chain has left
(U37), a `within` block's around the `attempt` among it (U38), and no bound of its own; the parent waits for it as for a
call, letting in the calls of its chain (U17), so that a call back into the parent from the block runs; a call or a wait
of the block past the deadline raises `call-timeout` in the child (U36), which reaches the parent as `Crashed` *(assumed:
T21 of comment 6980152255, built at the default while it waits for the owner)*. In the top-level code the top-level code
supervises the child as the root actor and waits for it in its wait, which runs the scheduler (§6, "Deadlocks")
*(assumed: T22 of comment 6980152255, built at the default while it waits for the owner)*. A message whose deadline
passed at its queueing, or before the child takes it, is dropped (U37), and the `attempt` raises `call-timeout` at its
line in the parent, as a call does, the crash of the parent ending the child (U13) *(assumed: T25 of comment
6982985447, built at the default while it waits for the owner)*. An `attempt` of the top-level code outside a `within`
roots a chain with no deadline, each call of its block bounded on its own (U36) *(assumed: T26 of comment 6982985447,
built at the default while it waits for the owner)*. For layer 2 the block is code of the method that runs the `attempt`, so
that its calls that wait are edges of that method (U56) (the owner, 2026-11-21, #519, comment 7068198309, attempt 1).

Case: [actors/attempt-callback.kz](../corpus/actors/attempt-callback.kz)
```kurz
// the block runs a call of the chain of `Run`, so that its call back into `Store` runs while `Run` waits
actor Store {
    mut int count = 0

    pub int Add() {
        count = count + 1
        return count
    }

    pub int Run() {
        me = this
        v = attempt { me.Add() * 10 } else {
            Crashed c => -1
        }

        return v + count
    }
}

s = spawn Store()
print(s.Run())
print(s.Add())
```

Case: [actors/attempt-top-level.kz](../corpus/actors/attempt-top-level.kz)
```kurz
actor Store {
    pub int Get() => 1
}

// the top-level code supervises the child of each `attempt` and waits for it, as it waits for a call
s = spawn Store()
v = attempt { s.Get() + 1 } else {
    Crashed c => -1
}
print(v)
w = attempt {
    n = s.Get()
    if n > 0 {
        throw "top"
    }

    return n
} else {
    Crashed c => {
        print("crashed at {c.Place.Line}")
        -1
    }
}
print(w)
```

Case: [actors/attempt-deadline.kz](../corpus/actors/attempt-deadline.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Store {
    pub int Get() => 1

    pub void Slow() {
        Hold(150ms)
    }
}

// the block's call past the deadline of the `within` block around the `attempt` raises `call-timeout` in the child,
// which reaches the top-level code as `Crashed`
s = spawn Store()
within 100ms {
    v = attempt {
        Hold(150ms)
        return s.Get()
    } else {
        Crashed c => {
            print(c.Reason.Kind)
            -1
        }
    }

    print(v)
}

// the block's call that waits past the deadline, behind a message that holds its callee, raises `call-timeout` in the
// child, while the code that waits for the `attempt` waits on past that deadline, with none of its own
within 100ms {
    w = attempt {
        send s.Slow()
        return s.Get()
    } else {
        Crashed c => {
            print(c.Reason.Kind)
            -2
        }
    }

    print(w)
}

print("after")
```

Case: [actors/attempt-dropped.kz](../corpus/actors/attempt-dropped.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

actor Holder {
    pub void Busy() {
        Hold(300ms)
    }
}

// `Busy`, queued ahead of the block's message, holds the one thread past the deadline, and the message is dropped
h = spawn Holder()
within 100ms {
    send h.Busy()
    v = attempt { 1 } else {
        Crashed c => 0
    }

    print(v)
}
```

Case: [actors/attempt-dropped-queued.kz](../corpus/actors/attempt-dropped-queued.kz)
```kurz
// the thread held for `d`, with no call and no wait
void Hold(duration d) {
    instant start = instant.Now
    while start.Elapsed < d {
    }
}

// the deadline of the `within` block passed before the `attempt` queues its message, which is dropped unqueued, and the
// `attempt` raises `call-timeout` at its line
within 100ms {
    Hold(150ms)
    v = attempt { 1 } else {
        Crashed c => 0
    }

    print(v)
}
```

### U50 (assumed, §6) The crash an `attempt` holds

A crash of the block writes the one `Error` log line of a crash a supervisor handles (U24) as the case reaches the parent
*(assumed: T23 of comment 6980152255, built at the default while it waits for the owner)*, `attempt#<n> crashed: ...`,
the name `attempt` standing for the actor's type *(assumed: a reading of the build, proposed with this record and not put
to the owner)*. The child has a heap of its own and a pooled stack, both freed at its end and at its crash, the thrown
value copied into the parent's heap before its own is freed, as a block of `on crash` gets it (U30); an `attempt` inside
the block of an `attempt` belongs to the inner child, whose crash the inner one holds, and a crash the inner one does not
hold, a throw of its arms among it, is the outer child's, as under `escalate` (U25) *(assumed: the small defaults of
comment 6980152255, not asked)*.

Case: [actors/attempt-nested.kz](../corpus/actors/attempt-nested.kz)
```kurz
// the inner `attempt` belongs to the outer one's child, and holds the crash of its own
xs = [1]
v = attempt {
    w = attempt { xs[3] } else {
        Crashed c => {
            print("inner {c.Place.Line}")
            5
        }
    }

    return w + xs[2]
} else {
    Crashed c => {
        print("outer {c.Place.Line}")
        -1
    }
}
print(v)
```

Case: [actors/attempt-log.kz](../corpus/actors/attempt-log.kz)
```kurz
// the crash writes its one `Error` line as the case reaches the top-level code, a line the case leaves out
attempt {
    throw "no stock"
} else {
    Crashed c => print("held")
}
```

### U51 (decided, §11, §13) `per node`

`actor Name per node { ... }` declares one actor of the machine, addressed by its name, `Cache.Get(1)`
and `send Cache.Add(1)`, with no `spawn`, no lookup and no registry (§11, "A singleton is an actor
declared `per cluster` or `per node` and addressed by its type name"; §13, "Actors addressed by key or
by name"), the machine one process of the program, as an Erlang node is one virtual machine *(assumed: said back with T27 to T33 of comment 6983903100, taken at the default while it waits for the owner)*. A
call or a `send` through its name is a message as one through a reference is (U4, U6, U9, U12), and
reads the instance it reaches before its arguments, as a call through a reference reads its target
*(assumed: said back with T27 to T33 of comment 6983903100, taken at the default while it waits for the owner)*. The instance is found there or started, its start run whole, before the first argument runs, and a
message whose instance an argument ended, by a stop or past its limit (U54), gets `actor-gone` at the call's line, as one
through a reference whose actor ended does (U6, U20) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. Its name stands only as the target of such a call, inside the actor as outside it (U55):
`spawn Cache()` is `type-mismatch`, as U14 refuses a `spawn` of what it cannot make, and a parameter of
its constructor and its name where a value stands are `syntax` (E4), as a class's name as a value is,
each text naming the `per node` form *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*. Whether its name can be written as a type, `Cache c = this`, a
parameter `Cache c` or `List<Cache>`, is open, put to the owner as T38 of comment 6991862214; the build reads such a type
as any actor's, whose value is a reference to one instance *(assumed: a reading of the build, put to the owner as T38 of comment 6991862214)*. A field named through its name is
`not-visible`, as one named through a reference is (U8) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. A method named through it with no call, and its
name called as a function, `Cache()`, have no rule here, their id put to the owner as T39 of comment 6991862214, and the
build says `not supported` to both *(assumed: a reading of the build, said back in comment 6986934971 and put to the owner as T39 of comment 6991862214)*. `per` with other words than `node` has no rule here
(U1), and the build says `not supported` at the clause: to `per cluster` with a text of its own, "the clause `per cluster`
after an actor's name, one actor in the cluster, which needs the directory of section 7 (U1, U51)" *(assumed: said back with T27 to T33 of comment 6983903100, taken at the default while it waits for the owner)*, and to `per` written
twice, `per node per node`, to `per` with any other words, `per caller` among them, and to a clause beside `per node` that
U1 gives no rule, `per node inbox drop`, with the text the build gives every clause it does not read, which names the
clause's own word, "the clause `per` after an actor's name (U1)" and "the clause `inbox` after an actor's name (U1)" *(assumed: a reading of the build, proposed with this record and not put to the owner)*; `compiler/README.md` says what the build does
with it.

Case: [actors/per-node.kz](../corpus/actors/per-node.kz)
```kurz
actor Counter per node {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

void AddTwo() {
    send Counter.Add(2)
}

actor Worker {
    pub void Work() {
        Counter.Add(3)
    }
}

send Counter.Add(1)
AddTwo()
w = spawn Worker()
w.Work()
print(Counter.Get())
```

Case: [actors/per-node-before-arguments.kz](../corpus/actors/per-node-before-arguments.kz)
```kurz
actor Cache per node {
    int first = Started()

    pub int Get(int n) => first + n
}

int Started() {
    print("started")
    return 1
}

int Argument() {
    print("argument")
    return 2
}

print(Cache.Get(Argument()))
```

Case: [actors/per-node-spawned.kz](../corpus/actors/per-node-spawned.kz)
```kurz
actor Cache per node {
    pub int Get() => 1
}

c = spawn Cache()
print(c.Get())
```

Case: [actors/per-node-parameters.kz](../corpus/actors/per-node-parameters.kz)
```kurz
actor Cache(int Size) per node {
    pub int Get() => Size
}

print(Cache.Get())
```

Case: [actors/per-node-as-value.kz](../corpus/actors/per-node-as-value.kz)
```kurz
actor Cache per node {
    pub int Get() => 1
}

c = Cache
print(c.Get())
```

Case: [actors/per-node-field.kz](../corpus/actors/per-node-field.kz)
```kurz
actor Cache per node {
    mut int hits = 0

    pub int Get() => hits
}

print(Cache.hits)
```

### U52 (assumed, §13) The start at its first message

A `per node` actor starts at the first message that names it, a call or a `send`, from any code, the
top-level code, a function or another actor, and a program that names it nowhere starts none,
as Orleans activates a grain at its first call (not checked at source) and C#'s `Lazy<T>` makes its value
at the first read of its `Value` (§13) *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*; a `send` starts it as a call does *(assumed: said back with T27 to T33 of comment 6983903100, taken at the default while it waits for the owner)*, so that a `send` through its name that starts it waits for the
start and a throw there crashes its sender, the one exception to U6's message that nobody waits for *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*. Its
constructor and its fields' defaults run on
the first caller's stack, as `spawn`'s do (U3), and a throw there is that caller's crash, under the
caller's own policy: the half-built actor ends, the calls queued behind its start, of other actors and of the top-level
code, the root (O6), get `actor-gone`, and the first message after that starts it again, as Rust's `OnceLock::get_or_init`
leaves its cell empty after a panic in its function (checked 2026-11-18: its documentation, "the cell remains uninitialized") *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*; the `send`s queued there go with its inbox, as the
inbox of an actor that ended goes (U25) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. An actor its defaults spawn is its child, which a
crash of it ends, as at a `spawn` (U3, U21) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. The crash names the line of the throw, as a
throw in a constructor at `spawn` does (U3) and as U24 gives a crash's place, where T29's option 1 of comment
6983903100 words it "its throw is the caller's crash at the call's line": the build departs from T29's words, as the
reading said back in comment 6986934971 states *(assumed: a reading of the build, said back in comment 6986934971 and put to the owner as T37 of comment 6991862214)*. Messages that reach it while it is built wait in its
inbox for its start, and the first caller's own message joins that inbox after the call's arguments, behind them
*(assumed: a reading of the build, proposed with this record and not put to the owner)*, and a call that waits on it from the code its own start runs, a default that calls a
function that calls `Cache.Get()`, waits for a start that waits for it: a loop of calls that wait through its type, which
layer 2 refuses at compile time as `call-loop` at that call's line (U56), where U18's `deadlock` at run time read it before
(the owner, 2026-11-21, #519, comment 7068198309, per node 1), and not the `deadlock` at run time at the line of the call
that the start keeps out, which holds for a restart (U22) (the owner, 2026-11-24, #610, comment 7140840988, "per node 1:
call-loop (Recommended)"). A `send` from the code its own start runs waits in the inbox behind the start *(assumed: a reading of the build, proposed with this record and not put to the owner)*.

Case: [actors/per-node-first-message.kz](../corpus/actors/per-node-first-message.kz)
```kurz
actor Clock per node {
    int ticks = Started()

    pub int Get() => ticks
}

actor Unused per node {
    int never = Started()

    pub int Get() => never
}

int Started() {
    print("started")
    return 1
}

print("before")
print(Clock.Get())
```

Case: [actors/per-node-send-starts.kz](../corpus/actors/per-node-send-starts.kz)
```kurz
actor Journal per node {
    mut int count = Started()

    pub void Add() {
        count = count + 1
    }

    pub int Count() => count
}

int Started() {
    print("started")
    return 0
}

send Journal.Add()
print("sent")
print(Journal.Count())
```

Case: [actors/per-node-start-shared.kz](../corpus/actors/per-node-start-shared.kz)
```kurz
// `User`'s `Use` waits in `Cache`'s start for `Slow.Get` while `Probe`'s `Ping` answers the top-level code, whose call then
// waits in `Cache`'s inbox for that start, as one thread runs the ready actors first in, first out (U35)

actor Slow per node {
    pub int Get() => 1
}

actor Cache per node {
    int first = Started(Slow.Get())

    pub int Get() => first
}

int Started(int n) {
    print("started")
    return n
}

actor User {
    pub void Use() {
        print(Cache.Get())
    }

    pub string Done() => "done"
}

actor Probe {
    pub int Ping() => 9
}

u = spawn User()
p = spawn Probe()
send u.Use()
print(p.Ping())
print(Cache.Get())
print(u.Done())
```

Case: [actors/per-node-start-throws.kz](../corpus/actors/per-node-start-throws.kz)

Case: [actors/per-node-queued-gone.kz](../corpus/actors/per-node-queued-gone.kz)

Case: [actors/per-node-reentrant.kz](../corpus/actors/per-node-reentrant.kz)
```kurz
actor Cache per node {
    int size = Measured()

    pub int Size() => size
}

int Measured() {
    return Cache.Size() + 1
}

print(Cache.Size())
```

Case: [actors/per-node-reentrant-in-message.kz](../corpus/actors/per-node-reentrant-in-message.kz)
```kurz
// `User`'s `Use` starts `Cache`, whose default calls `Cache.Size` in the chain of `Use`, a call that would wait in `Cache`'s inbox
// for the start that waits on it: a loop through `Cache`'s type, which layer 2 refuses at that call

actor Cache per node {
    int size = Measured()

    pub int Size() => size
}

int Measured() {
    return Cache.Size() + 1
}

actor User {
    pub int Use() => Cache.Size()
}

u = spawn User()
print(u.Use())
```

Case: [actors/per-node-reentrant-nested.kz](../corpus/actors/per-node-reentrant-nested.kz)
```kurz
// `Outer`'s start, which the top-level code runs, starts `Inner`, whose default waits on `Helper.Ask`, whose call of `Outer.Size`
// would wait in `Outer`'s inbox for the start that waits on it: a loop through `Outer`'s type, which layer 2 refuses at that call

actor Helper {
    pub int Ask() => Outer.Size()
}

actor Inner per node {
    int value = Asked()

    pub int Get() => value
}

actor Outer per node {
    int size = Inner.Get()

    pub int Size() => size
}

int Asked() {
    h = spawn Helper()
    return h.Ask()
}

print(Outer.Size())
```

Case: [actors/per-node-child.kz](../corpus/actors/per-node-child.kz)

### U53 (assumed, §6, §13) Its supervisor is the root

The supervisor of a `per node` actor is its first caller while it is built, whose crash a throw in its start is (U52),
and the root, the top-level code, from the end of its start, as for an actor the top-level code spawns, though its first
caller started it: it outlives that caller, and `on crash escalate` after its
name ends the process as the escalation of a child of the top-level code does (U25) *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*. Unless a throw in its start (U52)
or its policy (U54) ends it first, it ends with the process (§6, "`per` actors end with the process"), its heap freed with every actor's at the end
(U13) *(assumed: said back with T27 to T33 of comment 6983903100, taken at the default while it waits for the owner)*.

Case: [actors/per-node-outlives-caller.kz](../corpus/actors/per-node-outlives-caller.kz)

Case: [actors/per-node-escalate.kz](../corpus/actors/per-node-escalate.kz)

### U54 (decided, §6, §13) Its policy

The words of a policy after a `per node` actor's name are its policy (§6, "Policy at `spawn`", the owner, 2026-11-13, #519,
comment 6847932993, Q15), which runs as at a `spawn` of the top-level code (U53) *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*:
with none, or with `on crash restart`, a crash restarts it in place with fresh state and its inbox kept
(U21, U22), and past the limit of `max N in` it ends and the first message after that starts a fresh
one, its constructor run again, with no `restart-limit`, as the key of a `per` type does past its limit (U26; §6, the owner, 2026-11-17, #561, comment
6945857315, Q2, "Key restarts fresh on next call") *(assumed: the one actor of a `per node` type read as the key of comment 6945857315, Q2; the product pass, comment 6983506072)* *(assumed, §6: the calls queued in it at the crash
that passes the limit raise `actor-gone`)*. `on crash stop` ends it so: the calls queued in it get
`actor-gone`, and the first message after that starts a fresh one *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*. The `send`s queued in it at a crash
that stops it or passes its limit go with its inbox, as the inbox of an actor that ended goes (U25) *(assumed: a reading of the build, proposed with this record and not put to the owner)*. A
fresh start begins the window of its policy again, as §6 gives the key of a `per` type past its limit "no limit across
those starts" (comment 6945857315, Q2) *(assumed: the one actor of a `per node` type read as the key of comment 6945857315, Q2; the product pass, comment 6983506072)*, and the pause of its policy again *(assumed: a reading of the build, proposed with this record and not put to the owner)*. The block
`on crash (c) { ... }` after its name has no rule here (U28), and `compiler/README.md` says what the
build does with it.

Case: [actors/per-node-restart.kz](../corpus/actors/per-node-restart.kz)

Case: [actors/per-node-stop.kz](../corpus/actors/per-node-stop.kz)

Case: [actors/per-node-stop-gone.kz](../corpus/actors/per-node-stop-gone.kz)

Case: [actors/per-node-stop-sends.kz](../corpus/actors/per-node-stop-sends.kz)

Case: [actors/per-node-limit.kz](../corpus/actors/per-node-limit.kz)

### U55 (assumed, §13) Its name inside it

Inside a `per node` actor, a call through its name, `Cache.Get(1)`, is a message, as a call through
`this` is: a call that waits is a call of its own chain, which layer 1 runs (U17), and no edge of layer 2's graph, as a
call through `this` is none (U57) (the owner, 2026-11-21, #519, comment 7068198309, per node 1), and so is one through its
name in a function or a lambda that its own message runs, the actor named passing through the calls as `this` does
(the owner, 2026-11-21, #519, comment 7069702044), and
`send Cache.Add(1)` runs after the current message (U11), against a direct call, as `Get(1)` is (U10)
*(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*. `this` inside it gives a reference to the running instance, which ends with it, while its name
reaches the fresh one that a stop or the limit starts (U54) *(assumed: T27 to T33 of comment 6983903100, built at the default while it waits for the owner)*.

Case: [actors/per-node-self-send.kz](../corpus/actors/per-node-self-send.kz)
```kurz
actor Counter per node {
    mut int count = 0

    pub int Bump() {
        send Counter.Add(1)
        return Counter.Get()
    }

    pub void Add(int n) {
        count = count + n
    }

    pub int Get() => count
}

print(Counter.Bump())
print(Counter.Get())
```

### U56 (decided, §6) A path of calls that wait that comes back to an actor type

A loop is a path of methods that comes back to a type on it: `A.M` waits on `B.N`, whose code waits on any method of `A`
(§6, "Deadlocks", layer 2; the owner, 2026-11-20, #519, comment 7051071630, T40 (c)). The compiler reads, over the whole program, a graph of the
methods of the actor types: a point for each method an actor type declares and for its construction (U3), and an edge from
a point to a method of `B` for each call that waits on it through a reference (U4) that the point's code reaches, with the
blocks of `on crash` at its `spawn`s, which run as its messages (U28), and every function, class method, own method called
directly (U10), constructor and lambda that code runs, as §5's throw sets read it: a call of a function value runs every
lambda of the program whose type fits, wherever it was written, and a call through an interface every method that
implements it; and an edge to the construction of `B` for each `spawn` of `B` that code reaches, whose calls are `B`'s. A
`send` makes no edge (U6), a bound takes none out (U36, U39), and the top-level code, which nothing waits on, is no point of
the graph (T41 (a), T42 (a), T43 (a), T44 (a), T45 (a) of that comment). A method of `A` is on a loop where a method that
it reaches by one edge or more waits on a method of `A`, another instance of `A` among them, so that a loop through one type
counts (T48 (a)), and two chains that cross through two different methods, `Left.Ask` waiting on `Right.Value` and
`Right.Ask` on `Left.Value`, are no loop. The construction of a type is a point and starts a loop as a method does, and a
loop comes back by a call that waits, so that a path that comes back to `A` by a `spawn` of `A` alone reaches a new actor
and is none *(assumed: a reading of T40 (c) of comment 7051071630)*. The graph is read by reachability, never by a list of its paths, so
that its cost stays polynomial in the program (T59 (a)). Each group of loops is the compile error `call-loop`, at the call
of the group that stands first in source order, naming the shortest loop through that call with each hop's method, its line
and the function or lambda it stands in, so that two chains that enter it at once, each actor of the loop busy in one and
waiting on the other, are refused before they can hang (T52 (a)). A group is the methods on loops whose types reach one
another's, a method on a loop of each of the two types reaching a call that waits on a method of the other, so that two
loops through one method of a third type that waits on both are one group, and two that pass through two different methods
of a third type, each waiting on one of them, are two; the calls of a group are those that wait on a loop of it; the
shortest loop through the call starts at the method of the type it comes back to that reaches the call in the fewest
edges, the construction before the methods, and of those the first declared, of two types it comes back to in as few edges
the first declared, and comes back by the call that waits on that type that a walk from the call meets first, taking at each
method its calls in source order *(assumed: a reading of T40 (c) of comment 7051071630)*. The text names a `send` where no result is needed, or a change of the code so that its calls do not come back,
before `vouch` (U58). A call back to the caller is no edge (U57), and neither is a call after `vouch` (U58). The graph is
read on every program that declares an actor, after every body and beside the other errors, a call that reported its own
error making no edge, the walk reading each unit of code once, and a loop with a call in the program's own files is refused
whether any code calls into it or not, as `reference-cycle` is (R2) (the owner, 2026-11-20, #519, comment 7051071630, T52 (a), T53 (b), T54 (a), T55 (a),
T56 (a), T59 (a)). An actor with a part the build does not read, an interface after its name, a type parameter or `per`
with other words than `node`, is `not supported` (U1), and the build does not read its code, so that it is no point of the
graph: neither a call its code makes nor a call through a reference to it is an edge *(assumed: a reading of the build,
proposed with this record and not put to the owner)*: T46 (a) of comment 7051071630, an edge to every implementer, is not
built, since that construct is `not supported` on `main`. T47 (a) is built for the actor declared `per node` (U51): its
methods and its construction are points of the graph, a call through its name is an edge, and `vouch` before it is `syntax`,
since it always reaches its one actor (U58), a call through its own name in the code of its own messages, a function's and
a lambda's they run among it, is a call through `this`, which makes no edge (U55, U57; the owner, 2026-11-21, #519, comment 7069702044), and a message to its name, which can start it (U52), is an edge into its construction, whose code
is that actor's, as a `spawn`'s is, so that a loop through its start comes back by a call that waits in its construction
(the owner, 2026-11-21, #519, comment 7068198309, per node 1). The block of an `attempt` is code of the method that runs it, which waits on it as on a call
(U49), so that a call that waits in the block is an edge of that method, a call back to it through a local that holds
`this` none (U57), and `vouch` there is read as anywhere (U58) (the owner, 2026-11-21, #519, comment 7068198309, attempt 1). No parallel form (§6, "Parallel work") is
built, so that the graph reads none, and the body of one, where one exists, is code of its caller's method, as Q9 of issue
525, comment 6850177383, reads it for layer 2 *(assumed: a reading of the build, proposed with this record and not put to
the owner)*.

Case: [actors/call-loop.kz](../corpus/actors/call-loop.kz)
```kurz
actor Left {
    pub int Ask(Right other, Left back) => other.Ask(back) + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(r, l))
```

Case: [actors/call-loop-crossing.kz](../corpus/actors/call-loop-crossing.kz)
```kurz
actor Left {
    pub int Ask(Right other) => other.Value() + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2

    pub int Value() => 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(r))
print(r.Ask(l))
```

Case: [actors/call-loop-field-other.kz](../corpus/actors/call-loop-field-other.kz)
```kurz
actor A(B Other) {
    pub void Join() {
        Other.Keep(this)
    }

    pub int Total() => Other.Sum() + 1

    pub int Load() => 7
}

actor B {
    mut List<A> kept = []

    pub void Keep(A x) {
        kept.Add(x)
    }

    pub int Sum() {
        mut total = 0
        for k in kept {
            total = total + k.Load()
        }

        return total
    }
}

b = spawn B()
a = spawn A(b)
a.Join()
b.Keep(spawn A(b))
print(a.Total())
```

Case: [actors/call-loop-one-type.kz](../corpus/actors/call-loop-one-type.kz)
```kurz
actor Node {
    mut List<Node> children = []

    pub void Add(Node child) {
        children.Add(child)
    }

    pub int Size() {
        mut n = 1
        for c in children {
            n = n + c.Size()
        }

        return n
    }
}

root = spawn Node()
leaf = spawn Node()
root.Add(leaf)
print(root.Size())
```

Case: [actors/call-loop-function.kz](../corpus/actors/call-loop-function.kz)
```kurz
int AskOf(Right right, Left back) => right.Ask(back)

actor Left {
    pub int Ask(Right other, Left back) => AskOf(other, back) + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(r, l))
```

Case: [actors/call-loop-lambda.kz](../corpus/actors/call-loop-lambda.kz)
```kurz
actor Left {
    pub int Ask(Right other, Left back) {
        get = () => other.Ask(back)
        return get() + 1
    }

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(r, l))
```

Case: [actors/call-loop-function-value.kz](../corpus/actors/call-loop-function-value.kz)
```kurz
int Call(() => int f) => f()

actor Left {
    pub int Ask(() => int f) => Call(f) + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(() => r.Ask(l)))
```

Case: [actors/call-loop-interface.kz](../corpus/actors/call-loop-interface.kz)
```kurz
interface Getter {
    int Get()
}

class Via(Right Peer, Left Back) : Getter {
    pub int Get() => Peer.Ask(Back)
}

int Read(Getter g) => g.Get()

actor Left {
    pub int Ask(Right other, Left back) => Read(Via(other, back)) + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

l = spawn Left()
r = spawn Right()
print(l.Ask(r, l))
```

Case: [actors/call-loop-spawn.kz](../corpus/actors/call-loop-spawn.kz)
```kurz
actor Child(Parent Up) {
    int seen = Up.Count()

    pub int Seen() => seen
}

actor Parent {
    mut List<Child> kids = []

    pub int Count() => 3

    pub void Make(Parent other) {
        kids.Add(spawn Child(other))
    }
}

p = spawn Parent()
q = spawn Parent()
p.Make(q)
print(q.Count())
```

Case: [actors/call-loop-crash-block.kz](../corpus/actors/call-loop-crash-block.kz)
```kurz
actor Child {
    pub int Work(int n) {
        if n == 0 {
            throw "zero"
        }

        return n
    }
}

actor Boss(Peer P) {
    pub int Run(int n) {
        c = spawn Child() on crash (crash) {
            print("seen {P.Seen()}")
            restart
        }

        return c.Work(n)
    }
}

actor Peer {
    pub int Ask(Boss boss) => boss.Run(1)

    pub int Seen() => 2
}

p = spawn Peer()
b = spawn Boss(p)
print(b.Run(1))
print(p.Ask(b))
```

Case: [actors/call-loop-within.kz](../corpus/actors/call-loop-within.kz)
```kurz
actor Node {
    mut List<Node> children = []

    pub void Add(Node child) {
        children.Add(child)
    }

    pub int Size() {
        mut n = 1
        for c in children {
            n = n + c.Size() within 1s
        }

        return n
    }
}

root = spawn Node()
print(root.Size())
```

Case: [actors/call-loop-unreached.kz](../corpus/actors/call-loop-unreached.kz)
```kurz
actor Left {
    pub int Ask(Right other, Left back) => other.Ask(back) + 1

    pub int Value() => 1
}

actor Right {
    pub int Ask(Left other) => other.Value() + 2
}

print(3)
```

Case: [actors/call-loop-send.kz](../corpus/actors/call-loop-send.kz)
```kurz
actor Left {
    pub void Tell(Right other) {
        send other.Note(1)
    }

    pub int Value() => 1
}

actor Right(Left Up) {
    mut int noted = Up.Value()

    pub void Note(int n) {
        noted = Up.Value() + n
    }

    pub int Ask() => noted
}

l = spawn Left()
r = spawn Right(l)
l.Tell(r)
print(r.Ask())
```

Case: [actors/call-loop-spawns-alone.kz](../corpus/actors/call-loop-spawns-alone.kz)
```kurz
actor Counter(int Left) {
    mut List<Counter> made = []

    pub int Start() {
        if Left > 0 {
            made.Add(spawn Counter(Left - 1))
        }

        return Left
    }
}

c = spawn Counter(2)
print(c.Start())
```

Case: [actors/per-node-reentrant.kz](../corpus/actors/per-node-reentrant.kz)

Case: [actors/per-node-reentrant-in-message.kz](../corpus/actors/per-node-reentrant-in-message.kz)

Case: [actors/per-node-reentrant-nested.kz](../corpus/actors/per-node-reentrant-nested.kz)

### U57 (decided, §6) A call back to the caller that waits

A call through a reference that can only be the actor whose message makes it is no edge of U56's graph: `this` as a value
outside a lambda, or a local without `mut` that holds it (the owner, 2026-11-20, #519, comment 7051071630, T49 (c), T61 (a)). Neither is a call through a
parameter of an actor's method that is a callback. A parameter is a callback where calls through a reference reach the
method, every one of them a call that waits, and each passes there `this`, as the call writes it, or a parameter of the
method whose message makes the call that is a callback itself, read to a fixed point (the owner, 2026-11-20, #519, comment 7051071630, T60 (b)). The
parameter is followed through locals without `mut` and through the arguments of the functions and methods the message calls
(T49 (c)). A parameter of a method that no call through a reference reaches, and that the actor's own code calls directly
(U10), is read through its callers, each of them reading the method's calls with what it passes there, and a call's
arguments reach the method it picks and each of its overrides and of the methods that implement it, a data type's method
taking the value it is called on first (D16) *(assumed: a reading of T49 (c) of comment 7051071630)*. The callbacks are the
least fixed point: every parameter starts as none, and each is read again as long as one changes, so that a reference handed
on through a chain of calls that wait is one (T60 (b)), and only a parameter that the rounds reach from a `this` without
passing through itself is one, so that a cycle of parameters that no `this` fills is none, and so is a cycle that a `this`
enters, a recursion through two actor types that hands the reference back and forth among them (the owner, 2026-11-20, #519, comment 7051071630, T62 (a)). Such a call reaches the caller that waits, which takes it as a call of its chain (U17), as layer 1 runs `A.Load`
inside `A.Save` (§6, "Deadlocks", layer 2, "Exempt without override: a callback through a reference the caller passed in
that same call"). So is a call through the name of a `per node` actor in the code of its own messages, inside its own
method and in a function or a lambda that runs there, which U55 reads as a call through `this` (the owner, 2026-11-21, #519, comment 7069702044), and a call in the block of an `attempt` through a local of the method that runs it that holds `this`, a call
back into the actor that waits on the block (U49) (the owner, 2026-11-21, #519, comment 7068198309, per node 1, attempt 1).

A field set from a parameter that is a callback is one, and so is a list of them (the owner, 2026-11-20, #519, comment 7051071630, T49 (c)); a field
outlives the message that set it, so that another message's call through it can reach an actor the caller's chain does not
hold, as T49 (c) of #519, comment 7007991607, says of it. A field of an actor is one where every write of it takes a callback,
and so is each item of a list field whose every item written takes one, read in the least fixed point with the parameters,
so that a call through the field, or through an item of the list, read by an index or as the variable of a `for` over it, is
no edge, and any other write makes the field none *(assumed: a reading of T49 (c) of comment 7051071630)*. A write is an
assignment of a field of the actor's body in any method, by its name or through `this`, and its default, and of a list every
item that `Add` or `Insert` puts in, every item assigned and each item of a list literal assigned; `null` and an empty list
write nothing, and a field with no other write is a callback; a list that `AddRange` puts in, the field passed as `mut` and a
write in a lambda write another reference, and a write in a lambda that no code runs makes the field none *(assumed: a
reading of T49 (c) of comment 7051071630)*.

A field of the actor's primary constructor that every `spawn` fills with `this` of a caller that waits is a callback, as
T49 (b), T60 (b) and T62 (a) read one: the walk reads every `spawn` of the type, as it reads every write of a field, so that
`spawn Worker(this)` that calls back through the field gets no `call-loop`, as the same field set by a method does, and a
`spawn` that fills it with another reference makes it none (the owner, 2026-11-21, #519, comment 7060155980, T75 1). A
`spawn` fills the field as a caller that waits fills a parameter, `this` held in a local without `mut` among what it passes,
and gives none in the code outside the actors' messages; any other write of the field makes it none, as of a field of the
body *(assumed: a reading of T75 1 of comment 7060155980)*. A field of an actor that no `spawn` makes is none, as a
method that nothing calls has no caller (§6, the owner, 2026-11-21, #519, comment 7067131456).

No callback comes from a reference that a `send` passes, or that another caller that waits passes as anything but `this` or
a callback (T60 (b)), nor from an item of a list, but for a list field of callbacks, or from a capture in a lambda, a local
that holds `this` among them (T49 (c)); an item of a collection that is no list gives none either *(assumed: a reading of
T60 (b) of comment 7051071630)*. `this` inside a lambda gives none, since a lambda can be handed to another actor's message
(U12), where `this` names an actor outside the chain, and so does `this` in a lambda that runs in place, a key of `SortBy`
among them, which T61 (b) of #519, comment 7009683539, would have made a callback (the owner, 2026-11-20, #519, comment 7051071630, T61 (a)). Nor
does a call that code outside the actors' messages runs make one: the outside is what the top-level code and the code no
actor's message reaches run, not every `pub` method, so that a `this` handed on through a method of a class, an interface or
a data type is a callback where every caller of that method is an actor's message (the owner, 2026-11-20, #519, comment 7051071630, T74 (b)). The code
no actor's message reaches is read from the `pub` functions and the `pub` methods of the classes and the data types, which a
caller outside the program could call, through every function, method and lambda they run, and a `static` method of an actor
is no message of it, so that the code only such a method reaches is outside *(assumed: a reading of T74 (b) of comment
7051071630)*. The walk keeps no context per call, so that one caller that passes another reference makes the parameter's
calls edges for every caller (T49 (c)). A method that nothing calls has no caller to call back, so that a loop through its
parameters is refused, as U56 refuses a loop no code calls into *(assumed: a reading of the build, proposed with this record
and not put to the owner)*.

Case: [actors/callback.kz](../corpus/actors/callback.kz)

Case: [actors/chain-send-waits.kz](../corpus/actors/chain-send-waits.kz)

Case: [actors/call-self-through-this.kz](../corpus/actors/call-self-through-this.kz)

Case: [actors/call-loop-callback-helper.kz](../corpus/actors/call-loop-callback-helper.kz)
```kurz
int LoadOf(int k, A x) => x.Load() + k

actor A(B Other) {
    mut int saved = 0

    pub int Save(int n) {
        saved = n
        return Other.Check(this) + 1
    }

    pub int Load() => saved
}

actor B {
    pub int Check(A x) => LoadOf(1, x) * 10
}

b = spawn B()
a = spawn A(b)
print(a.Save(4))
```

Case: [actors/callback-chain.kz](../corpus/actors/callback-chain.kz)

Case: [actors/call-loop-passed-other.kz](../corpus/actors/call-loop-passed-other.kz)
```kurz
actor A(B Other) {
    pub int Save(A other) => Other.Check(other) + 1

    pub int Load() => 7
}

actor B {
    pub int Check(A x) => x.Load() * 10
}

b = spawn B()
a = spawn A(b)
peer = spawn A(b)
print(a.Save(peer))
```

Case: [actors/call-loop-two-callers.kz](../corpus/actors/call-loop-two-callers.kz)
```kurz
actor A(B Other) {
    pub int Save() => Other.Check(this) + 1

    pub int Load() => 7
}

actor B {
    pub int Check(A x) => x.Load() * 10
}

b = spawn B()
a = spawn A(b)
print(a.Save())
print(b.Check(a))
```

Case: [actors/call-loop-chain-other.kz](../corpus/actors/call-loop-chain-other.kz)
```kurz
actor Store(Checker Check) {
    pub int Save() => Check.Valid(this) + 1

    pub int Audit(Store other) => Check.Valid(other) + 2

    pub int Load() => 7
}

actor Checker(Verifier V) {
    pub int Valid(Store store) => V.Verify(store) * 10
}

actor Verifier {
    pub int Verify(Store store) => store.Load() * 10
}

v = spawn Verifier()
c = spawn Checker(v)
s = spawn Store(c)
peer = spawn Store(c)
print(s.Save())
print(s.Audit(peer))
```

Case: [actors/call-loop-send-this.kz](../corpus/actors/call-loop-send-this.kz)
```kurz
actor A(B Other) {
    pub void Save() {
        Other.Check(this)
    }

    pub void Tell() {
        send Other.Check(this)
    }

    pub int Load() => 7
}

actor B {
    pub void Check(A x) {
        print(x.Load())
    }
}

b = spawn B()
a = spawn A(b)
a.Save()
a.Tell()
```

Case: [actors/call-loop-callback-field.kz](../corpus/actors/call-loop-callback-field.kz)
```kurz
actor A(B Other) {
    pub void Join() {
        Other.Keep(this)
    }

    pub int Total() => Other.Sum() + 1

    pub int Load() => 7
}

actor B {
    mut List<A> kept = []

    pub void Keep(A x) {
        kept.Add(x)
    }

    pub int Sum() {
        mut total = 0
        for k in kept {
            total = total + k.Load()
        }

        return total
    }
}

b = spawn B()
a = spawn A(b)
a.Join()
print(a.Total())
```

Case: [actors/call-loop-field-other.kz](../corpus/actors/call-loop-field-other.kz)

Case: [actors/call-loop-lambda-capture.kz](../corpus/actors/call-loop-lambda-capture.kz)
```kurz
actor A(B Other) {
    pub int Save() {
        get = () => Other.Check(this)
        return get() + 1
    }

    pub int Load() => 7
}

actor B {
    pub int Check(A x) => x.Load() * 10
}

b = spawn B()
a = spawn A(b)
print(a.Save())
```

Case: [actors/call-loop-lambda-local.kz](../corpus/actors/call-loop-lambda-local.kz)
```kurz
actor A(B Other) {
    pub int Save() {
        me = this
        get = () => Other.Check(me)
        return get() + 1
    }

    pub int Load() => 7
}

actor B {
    pub int Check(A x) => x.Load() * 10
}

b = spawn B()
a = spawn A(b)
print(a.Save())
```

Case: [actors/call-loop-callback-interface.kz](../corpus/actors/call-loop-callback-interface.kz)
```kurz
interface Checker {
    int Run(A x)
}

class Via(B Peer) : Checker {
    pub int Run(A x) => Peer.Check(x)
}

int Ask(Checker k, A s) => k.Run(s)

actor A(B Other) {
    pub int Save() => Ask(Via(Other), this) + 1

    pub int Load() => 7
}

actor B {
    pub int Check(A x) => x.Load() * 10
}

b = spawn B()
a = spawn A(b)
print(a.Save())
```

Case: [actors/call-loop-unreached.kz](../corpus/actors/call-loop-unreached.kz)

### U58 (decided, §6) `vouch`

`vouch` before a call that waits on an actor's method through a reference (U4), `vouch c.Size()`, takes that call out of
U56's graph, and with it every loop through it, and says that the call reaches another actor than the one of the loop that
waits (§6, "Deadlocks", layer 2, "`vouch` on the call overrides it and means 'different instance, I vouch'"), which nothing
checks at run time, U18's `deadlock` and `call-timeout` being the net for a false promise (the owner, 2026-11-20, #519, comment 7051071630, T57 (a)). Where it does not hold, the call runs as it would without the word: a call that reaches an actor busy in
another chain waits on it, so that the two chains wait on each other and the waiter whose wait closes their loop gets
`deadlock` (U62, U65), and a call that reaches an actor waiting in the
call's own chain gets in as a call of that chain (U17), so that a recursion of one chain through a `vouch` that does not hold
runs to its result *(assumed: a reading of T57 (a) of comment 7051071630)*.
It binds to the call after it, so that `n + vouch c.Size()` adds the call's result to `n`, and the call's clause and its
`else` stand after the call, `vouch c.Size() within 1s` (U39); of a chain of calls after it, it takes the last, so that
`vouch c.Child().Size()` takes the call of `Size` out and leaves `c.Child()` a call as any other *(assumed: a reading of the
build, proposed with this record and not put to the owner)*. Before anything else, a function's call, a class's method, a
direct call of the actor's own method (U10), `vouch this.Size()` among them, since a call through `this` is one (U10), a
`spawn` (U2), what is no call, or inside a `send` (U14), it is `syntax` (E4), as `within` on such a call is (U41), and so it is
before a call through the name of a `per node` actor, `vouch Cache.Get()`, which always reaches its one actor (U51)
(the owner, 2026-11-21, #519, comment 7068198309, per node 1); a call
through a local that holds `this` is a call through a reference, which U57 takes out, so that `vouch` before it is U59's
warning *(assumed: a reading of the build, proposed with this record and not put to the owner)*. `vouch` is a word only
where it stands, before a name, `this` or `spawn` on its line (§6, the owner, 2026-11-13, #519, comment 6847932993, Q10), so
that `vouch` names a variable and `vouch(3)` calls a function (the owner, 2026-11-20, #519, comment 7051071630, T50 (a)). A statement is read as a declaration first, and `vouch` is the word only where none
parses, so that `vouch c = vouch(2)` declares `c` of a type named `vouch` *(assumed: a reading of the build, proposed with
this record and not put to the owner)*.

Case: [actors/vouch-tree.kz](../corpus/actors/vouch-tree.kz)
```kurz
actor Node {
    mut List<Node> children = []

    pub void Add(Node child) {
        children.Add(child)
    }

    pub int Size() {
        mut n = 1
        for c in children {
            n = n + vouch c.Size()
        }

        return n
    }
}

root = spawn Node()
a = spawn Node()
b = spawn Node()
root.Add(a)
a.Add(b)
print(root.Size())
```

Case: [actors/vouch-within.kz](../corpus/actors/vouch-within.kz)
```kurz
actor Node {
    mut List<Node> children = []

    pub void Add(Node child) {
        children.Add(child)
    }

    pub int Size() {
        mut n = 1
        for c in children {
            s = vouch c.Size() within 1s
            n = n + s
        }

        return n
    }
}

root = spawn Node()
leaf = spawn Node()
root.Add(leaf)
print(root.Size())
```

Case: [actors/vouch-other-loop.kz](../corpus/actors/vouch-other-loop.kz)
```kurz
actor Node {
    mut List<Node> children = []

    pub void Add(Node child) {
        children.Add(child)
    }

    pub int Size() {
        mut n = 1
        for c in children {
            n = n + vouch c.Size()
        }

        return n
    }

    pub int Depth() {
        mut most = 0
        for c in children {
            d = c.Depth()
            if d > most {
                most = d
            }
        }

        return most + 1
    }
}

root = spawn Node()
leaf = spawn Node()
root.Add(leaf)
print(root.Size() + root.Depth())
```

Case: [actors/vouch-not-actor-call.kz](../corpus/actors/vouch-not-actor-call.kz)
```kurz
int Twice(int n) => n * 2

print(vouch Twice(3))
```

Case: [actors/vouch-send.kz](../corpus/actors/vouch-send.kz)
```kurz
actor Counter {
    mut int count = 0

    pub void Add(int n) {
        count = count + n
    }
}

c = spawn Counter()
send vouch c.Add(1)
```

Case: [actors/vouch-name.kz](../corpus/actors/vouch-name.kz)
```kurz
vouch = 2
print(vouch + 1)
```

Case: [actors/vouch-declaration.kz](../corpus/actors/vouch-declaration.kz)
```kurz
data vouch(int N)

actor A {
    pub int M() {
        vouch c = vouch(2)
        return c.N
    }
}

a = spawn A()
print(a.M())
```

Case: [actors/vouch-same-chain.kz](../corpus/actors/vouch-same-chain.kz)

Case: [actors/vouch-own-method.kz](../corpus/actors/vouch-own-method.kz)

### U59 (decided, §6, §12) A `vouch` that covers no loop

A `vouch` whose call is on no loop of U56's graph read with every `vouch` left out, so that two on one loop keep each other,
or whose call U57 takes out, is the lint `vouch-unneeded`: a warning at the word, in the program's own files, which stops
no build, written as H5 writes `md5-insecure`, `app.kz:3: warning: vouch-unneeded: <text>`, as rustc warns of an
`unsafe` block that holds no unsafe operation; nothing silences it, as H5's lint, it stands beside the program's other
errors, and a program with a `not supported` gets none, since an unread construct can hide an edge (the owner, 2026-11-20, #519, comment 7051071630, T51 (b), T54 (a), T55 (a)).

No case: a warning changes neither what a program prints nor whether it compiles, and a corpus header cannot expect one (V12).

### U60 (decided, §6) The wait line

A call that waits, made in an actor's message, makes an edge of the wait line from its activation to the actor it calls,
unless it got in by its chain (U17) and no activation of that actor runs it yet, the waiter's own call that gets in making
no edge; from that actor the line goes on through the wait of its top activation, and so through the activation that runs
a call that got in by its chain, where that activation is newer in the chain than the hop's (the owner, 2026-11-21, #519, comment 7060155980, T86 2)
*(assumed: a reading of the build of T86 2, comment 7059356106: "newer in the chain than the hop's" read as an activation of
that actor, at or under its top activation, that runs the very call the hop's waiter waits on)*, and from a
child whose block of `on crash` waits (U34) to its supervisor, an actor, through the wait of the supervisor's top activation,
whose block runs only after the supervisor's current message (U28) (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T63 2). The line ends at
an actor that is idle, that runs, or whose top activation can go on, its reply set or its deadline passed, at an actor that
waits for its restart (U23) or ended, at a call that got in that no activation runs yet, and at a child whose block runs in
the top-level code's wait
(U33) *(assumed: a reading of T63 2 of that comment)*. The top-level code is no point of the line, and its waits follow none
(T64 1 of that comment); no call reaches an actor under construction (U3), whose reference reaches its spawner once `spawn`
returns and whose defaults read no `this` (K7), so that no edge goes to one *(assumed: as the question of comment 7012581664,
T63, read it)*.

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)

Case: [actors/waitline-pending-block.kz](../corpus/actors/waitline-pending-block.kz)

Case: [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz)

Case: [actors/waitline-got-in.kz](../corpus/actors/waitline-got-in.kz)

### U61 (decided, §6) The line is followed at every wait of an actor

Before the activation of a call that waits parks, the runtime follows the line from the call's edge (U60), and where the line
leads back to the waiting actor, the wait closes a loop (U62) (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T65 1). The call's bound, its
clause's, its block's and its chain's deadline change nothing of it (U36 to U44), so that a loop under a bound of 2 s raises
`deadlock` at once and never `call-timeout` (T66 1 of that comment). A line that runs into a loop its waiting actor is not in
ends there, and the wait parks, raising nothing; each hop of the line is a different actor, up to its waiter or such
a loop, which a check of Brent's kind stops, so that a walk is bounded by the actors that wait (T65 1 of that comment); and a
call that gets in, a call back through `this` among them (U10, U17), ends the line at its first hop.

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)

Case: [actors/waitline-within.kz](../corpus/actors/waitline-within.kz)

Case: [actors/waitline-into-loop.kz](../corpus/actors/waitline-into-loop.kz)

Case: [actors/callback.kz](../corpus/actors/callback.kz)

Case: [actors/callback-chain.kz](../corpus/actors/callback-chain.kz)

Case: [actors/call-self-through-this.kz](../corpus/actors/call-self-through-this.kz)

### U62 (decided, §6) The waiter that closes a loop gets `deadlock`

The waiter whose wait closes a loop of the line raises `deadlock` at the line of its call, as `call-timeout` is raised there
(U36) (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T67 1), and its raise is its actor's crash (U19) under its policy (U25 to U29): by
default its supervisor restarts it (U22), and the calls of the loop's other chains that wait in its kept inbox (U21) run in
the new instance, or raise `actor-gone` where its policy stops it (U25); a caller that waits on the message that crashed gets
`callee-crashed` (U20), and so up its chain to the top-level code, whose call then ends the process with `callee-crashed` at
its own line, the `deadlock` standing in the crashed actor's log line (U24) (T69 1 of that comment).

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)

Case: [actors/waitline-stop.kz](../corpus/actors/waitline-stop.kz)

Case: [actors/waitline-root.kz](../corpus/actors/waitline-root.kz)

Case: [actors/waitline-escalate.kz](../corpus/actors/waitline-escalate.kz)

Case: [actors/waitline-vouch.kz](../corpus/actors/waitline-vouch.kz)

Case: [actors/waitline-got-in.kz](../corpus/actors/waitline-got-in.kz)

### U63 (decided, §6) The text of a `deadlock`

The error line of a `deadlock` that U62 raises ends with a colon, and the lines under it name the loop, one for each wait in
it, from the wait that raised, as a `throw`'s text stands under its error line (O6): `<Type>#<n> waits at <file>:<line> on
<Type>#<m>` for a call, and `<Type>#<n> waits for the decision of <Type>#<m> on its crash` for a child whose block waits
(U60), each actor by the name of its type and its number (U2), as its log line names it (U24), and the IDs of the chains
(§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T68 3): each line ends with ` in chain ` and a chain's ID, 32 lowercase hexadecimal digits
as a block's `Crashed.Chain` reads one (U30), for a call the chain of the activation that waits, for a child the chain of
the message whose crash its block decides on (the owner, 2026-11-21, #519, comment 7060155980, T84 1); a child's line whose
block's message stands neither on its supervisor's queue of blocks, nor in its inbox, nor under one of its activations ends
with no chain ID *(assumed: a reading of the build of T68 3 and T84 1, comment 7058660596)*. The crash's log line
holds the text in double quotes (U24), the IDs inside it while P8, the place of a chain ID among the line's own fields, stays
open *(assumed: T68 3 read with option 1's cost, comment 7058660596)*, and a block of `on crash` reads it as the `Detail` of
the `RuntimeError` in its `Reason`, whose `Kind` is `RuntimeKind.Deadlock` (U30); where a policy escalates the crash to the
top-level code (U25), the process ends with that error line and the text under it.

Case: [actors/waitline-text.kz](../corpus/actors/waitline-text.kz)

Case: [actors/waitline-escalate.kz](../corpus/actors/waitline-escalate.kz)

Case: [actors/waitline-vouch.kz](../corpus/actors/waitline-vouch.kz)

### U64 (decided, §6) U18 behind layer 3

U18's end of the process stays for what layer 3 does not see: on one thread, a loop that a crash closes, where a child
crashes while its supervisor's call already waits in its inbox and the child's block of `on crash` waits behind that call's
message (U34), so that no wait closes the loop and the top-level code's wait on that message finds nothing left to run, its
deadline standing (U42) (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T63 2 and T70 1, which replaces the part of Q4 of comment
6892812780 that kept layer 3 off one thread). That `deadlock` names the loop: one walk at U18's end follows the line from
the top-level code's call (U60), and where it runs into a loop, the error line ends with a colon and the loop's waits stand
below it in U63's form, one line for each, each with its chain's ID where U63 gives one, from the first wait of the loop the walk meets; where
the line ends before any loop, the error line stands alone (the owner, 2026-11-21, #519, comment 7060155980, T85 2).

Case: [actors/deadlock-pending-crash.kz](../corpus/actors/deadlock-pending-crash.kz)

Case: [actors/waitline-into-loop.kz](../corpus/actors/waitline-into-loop.kz)

### U65 (decided, §6) `vouch` and layer 3

A loop through a call written with `vouch` (U58) is a loop of the line as any other: nothing at run time reads the word, and
the text of its `deadlock` (U63) names the call's line, where the word stands (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T71 1).

Case: [actors/waitline-vouch.kz](../corpus/actors/waitline-vouch.kz)

### U66 (decided, §6) Worker threads and other machines

On worker threads a walk with no lock that finds a loop confirms it under a lock of the process before it raises, and takes
the raiser's edge off under that lock, so that one loop gives one exception (§6, "Deadlocks", layer 3; the owner, 2026-11-20, #519, comment 7051071630, T72 2); a call
to an actor of another machine ends the line, and a loop across machines falls to the bound (§6, "Deadlocks", "Across
machines"; T73 1 of that comment).

No case: a case runs on one thread when its output follows one thread's order (U35) and on one machine, and the build runs
no worker thread and no second machine.
