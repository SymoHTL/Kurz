# 19. Actors

An actor is an object with private state and an inbox, which handles one message at a time; a call
of its method through its reference is a message (§6, "Actors"). This chapter holds the actors of
one machine and what a program writes of them: an actor's declaration, `spawn`, the call that waits
for its result, `send`, and what crosses into a message, the crash of an actor, its restart by
the default policy and its log line (U19 to U24), the words of a policy (U25 to U27), the block of a policy (U28
to U34) and the threads that run the actors (U35). The scheduler but its threads, the stacks, deadlocks but the
calls a waiting actor lets in (U17)
and the wait of the top-level code that nothing can end (U18), the clauses after an actor's name but
the stack's and the policy's (U1, U27) and the actors of other machines (§6, §7) have no rule here, and `compiler/README.md` says what the compiler builds of the rules here.

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
immutable class instance where every value it reaches crosses, a lambda the sending function shows
where every value it captures crosses (§6, Q5), and an actor's reference; a mutable class instance, and a collection or a `data` value that reaches one, crosses with `copy`
or `move` alone, where every value it reaches crosses or is a mutable class instance, and with
`move` only where every path into it ends (§6, "What may cross between actors"). A message's arguments and its result cross so, and so
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
structural and read from types *(assumed: the reading of Q14 (i) said back in #519, comment
6882879015, reading 4, read by types, as a crossing copies by the parameter's type)*; a lambda
the sending function shows crosses where every value it captures crosses (§6, Q5), and a
mutable class instance given as a call's result, where no word can stand, is a compile error (§6,
the owner, 2026-11-14, #519, comment 6880404917, Q14, "All eight leans (Recommended)"), and that
error is `send-needs-word`, as rustc gives the same error to a task's output as to its input
(`tokio::spawn` asks `F::Output: Send`) (§6, the owner, 2026-11-15, #519, comment 6896192351, Q4,
"Rust: send-needs-word"), at the line of the call *(assumed: that place, since no argument stands
for a result)*, and so is a function value and a value that reaches a value no word makes cross, or
a mutable class instance, given as a result *(assumed, §6: as rustc asks `Send` of a task's output by
its type, which Q4 named for a mutable class instance and an interface's value alone)*.
A collection or a `data` value that reaches a mutable class instance is `send-needs-word` at its
argument with neither word, and crosses with `copy`, a deep copy of all it reaches up to an actor's
reference, since one that reaches a function value does not cross (§6, Q5), or with `move`, every path into it ending, `move-reachable` where
one cannot *(assumed: that reading of §6's `copy` and `move`, carried from a mutable class instance
to a value that holds one, where rustc reads `Send` through a struct's fields whatever a call
writes)*. A collection, a `data` value or a class instance that reaches a function value is
`send-needs-word` at its argument with a word or with none, as that function value is, one
reached through a field or an item being read by its written type (§6, Q5), the paths `move` ends stopping at a function value it reaches *(assumed: Q24's
cost read for a word at the argument as for that function value, as rustc reads `Send` through a
struct's fields and a closure's through its captures)*. A reference that crosses names the same
actor, so that `==` holds across a crossing (§6, the owner, 2026-11-14, #519, comment 6880404917,
Q10). The deep copy of `copy` adds nothing at a function value it reaches, which the receiver gets as it
would with no word, copied into its heap with the values it captures where the call crosses (§6,
the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big unbuilt"), there only
the argument itself, a lambda the sending function shows, since one reached through a field or an
item does not cross (§6, Q5), an instance it captures arriving as the copy that an argument
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
`move-at-call`'s do, since cases of this rule expect them (§6). Two paths to one block of a value that crosses, or of a
value after `copy`, arrive as two paths to one copy, as Rust's clone of an `Rc` graph keeps its
sharing, so that a copy is never larger than its source and a mutable graph keeps its shape (§6,
the owner, 2026-11-15, #519, comment 6896192351, Q2, "Sharing kept"). The copies one call makes,
of a message's arguments, of a `spawn`'s or of the arguments after `copy` at any call, share one
memo, so that two arguments that reach one block arrive as two paths to one copy, and a `weak`
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
memo holds is not copied again (Q2), so that an argument's copy of it shows the block as the first
copy found it *(assumed, §6: the memo of the call and the order of its copies)*. The sender makes the copy,
at the send; every class instance a crossing reaches is copied, so that identity does not survive
a crossing; the copy walks a work list and never recurses, so that a chain of a million links crosses on a
stack of 2 MiB; and a copy runs no program code, no `Text()` and no `equal by` *(assumed, §6: the
points comment 6895076937 takes as following from §6, which the answers left standing)*, but for the
hash of each key of a map whose keys reach a class instance, by the fields its class compares,
`equal by`'s among them (§6, the owner, 2026-11-16, #519, comment 6923513534), which runs no program
code, since a class declares no `Equals` or `GetHashCode` (K5). Each actor
having its own heap, a crossing, a `move` among them, gives the receiver a value that no program
can tell from a copy in its heap, a big one shared by pointer under §6's share of a big value, which
stays decided and unbuilt (§6, the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share
big unbuilt"); a `copy` at a call that crosses nothing copies every block on a path to a class
instance, the instance among them, and shares by its count only a block that reaches none
*(assumed, §6: the answer read for a call inside one heap)*. The copy of a map whose keys reach a
class instance, where it crosses or stands after `copy`, hashes each key again as it is copied, by
identity or by content as its class compares (K4, K5), so that the copied map finds what the source
found; the cost, the key's hash and its `equal by` running inside the copy, falls on such maps alone
(§6, the owner, 2026-11-16, #519, comment 6923513534, "Rehash each key (Recommended)"), as a Rust
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
`f(move Cart(1))`, stands where the word fits the parameter, and ends nothing, the call taking the value it made *(assumed, §6: the
readings of the top-level code and of a value with no place, which no answer named)*. A call merges the regions of all its arguments, written `mut` or not, and its result's, as
SE-0414 does, since `F(a, b)` can store `b` in a `mut` field of `a`, and an argument that crosses
with no word merges nothing (§6, 2026-11-15, Q2) *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A lambda joins the regions of what it
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
by pointer under §6's share of a big value, which no program tells from a copy, and lets go of the
original, which frees it, and into a `spawn` whose policy can restart the supervisor keeps a second
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

string Look(Box a, Link l, Box b) {
    b.N = 2
    t = l.To
    if t != null {
        return "{a.N} {t.N} {a == b} {t == b}"
    }

    return "gone"
}

box = Box(1)
print(Look(copy box, copy Link(box), copy box))
f = (Box a, Link l, Box b) => Look(a, l, b)
print(f(copy box, copy Link(box), copy box))
print(box.N)
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
first argument the call writes that fills a `mut` parameter, and on one argument the two never meet,
an argument written with `move` that fills a `mut` parameter being `mut-at-call` (M7), its own error
*(assumed: as §6 marks it)*. An argument's own error
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
too *(assumed in §6)*. An actor under construction takes no
message until its `spawn` returns, one of its chain neither *(assumed in §6)*. The chain ID is 16 bytes,
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
wait"). A call that waits in a function the top-level code called is the top-level code's, at its
own line. A loop of actors the top-level code never waits on stays until the end of the program,
where U13 drops it.

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)
```kurz
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

### U19 (decided, §6) A crash kills the actor it happens in

An exception raised in a message, a `throw`, a check of the run-time table (chapter 12) and
`print-failed` among them, and on Linux a stack overflow in the actor's own stack, is the crash of
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
2026-11-15, #519, comment 6902210646, Q1, "Erlang: callee-crashed"). That raise is the crash of the
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
current message, their queued messages dropped (U13), and none of them restarts.

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

Case: [actors/crash-child-gone.kz](../corpus/actors/crash-child-gone.kz)

Case: [actors/send-gone.kz](../corpus/actors/send-gone.kz)

### U22 (decided, §6) The supervisor restarts the actor

The default policy, `on crash restart`, runs the actor's constructor again, after the pause of U23,
on a copy of the arguments its `spawn` was given, their defaults among them, which the spawner's
heap keeps, every block they reach copied, a `move` argument's too (§6, the owner, 2026-11-14, #519,
comment 6880404917, Q13, "All eight leans (Recommended)"; the taken points of comment 6899225864).
The runtime runs the restart before any message of the inbox, and runs it in no message of the
supervisor *(assumed, §6)*. The new instance has its state from the constructor and its body's
defaults alone, as a new actor has it; a throw in the constructor at a restart is the actor's own
crash, which restarts it again (U3).

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
marks it, the trace of the stack that raised, as at a raise of the top-level code)*.

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
the calls queued in it at that crash raise `actor-gone`, and that crash writes its log line as any
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

### U27 (decided, §6) The type's default

The words of a policy after the name of an actor that is not `per` (U1) are the type's default,
which holds at every `spawn` of it that writes none, and a policy at `spawn` wins over it, as a call's
`within` wins over the declaration's (§6, "Policy at `spawn`"; 2026-11-13, Q11; the owner,
2026-11-15, #519, comment 6902210646, Q3, "Elixir: type's default"), whole, its knobs with it
*(assumed: as §6 marks it, as a child spec's `restart` replaces the module's default)*. The same
words after a `per` actor's name are its policy (§6, the owner, 2026-11-13, #519, comment 6847932993,
Q15); `per` has no rule here (U1), and `compiler/README.md` says what the build does with both.

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

### U28 (decided, §6) The block of a policy

In the place of the words of a policy, after `spawn`'s arguments and after an actor's name, a block
can decide what the supervisor does at the actor's crash: `on crash (c) { ... }`, its one parameter
in parentheses, which names the crash (U30), and its block, opened on that line (§6, "Policy at
`spawn`"). The block runs as a message of the supervisor, after its current message, with the calls
of the chain that message lets in (U17), and before its queued ones, and its decision (U29) is what
the supervisor does (§6, the owner, 2026-11-15, #519, comment 6902210646, Q2, "Erlang/Akka:
supervisor msg"); at a `spawn` of the top-level code it runs in that code's wait (U33). The block
ends the expression at `spawn`, as the words of a policy do (U2), and after an actor's name it is the
type's default (U27), the braces after it the actor's body; no name in the parentheses, a second
one, and a block that does not open on the line of its parameter are `syntax` (E4) *(assumed: as §6
marks it)*. The blocks `attempt { ... }` and `retry { ... }` of §6, which keep a crash to a piece of
code, have no rule here, and `compiler/README.md` says what the build does with them.

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

        send this.Queued()
        send first.Fail()
        send second.Fail()
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

### U29 (decided, §6) The decisions

`restart`, `stop` and `escalate` are the statements of the block that decide, and each does what the
same word does after `on crash` (U22, U23, U25): `restart` restarts the actor after the growing pause,
`stop` leaves it stopped, so that a call that waits in its inbox or comes after raises `actor-gone`,
and `escalate` passes its crash up whole, which at the top level ends the process with the child's
error line, the text below it, the trace of the child's stack and the exit code 1 (§6, "Policy at
`spawn`"). A decision ends its path, as `return` ends a function's, and each path of the block ends
with one or with a `throw` (U32); a path that decides nothing is `missing-return`, as a lambda's
path that ends with no value is (F3) (§6, the owner, 2026-11-16, #519, comment 6919351399,
"missing-return error"). `restart` there takes no knobs, the default's pause and no limit, as Akka's
`Restart` directive carries none, and a knob after it, a type before the parameter and `return` in
the block are `syntax` (E4); the three words are statements in the block alone, and names elsewhere,
before `=` in the block among it, as `send` is (U6), and a lambda inside the block decides nothing,
its words names there *(assumed: as §6 marks it)*. A block can restart, so that its `spawn` keeps the
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
```kurz
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

### U30 (decided, §5, §6) The crash the block reads

The block's parameter is a value of the standard library's `Crashed<R>`, whose fields are `Reason`,
what the child threw, of the type `R`, `Place`, the place that raised it, a `Place(string File, int
Line)`, and `Chain`, the ID of the chain of the message that crashed (§5, the place and the chain ID
fields of the `Crashed` case beside `Reason`), its 32 hexadecimal digits *(assumed: as §6 marks it,
the names, the place's type, as Rust's `panic::Location`, and the digits, as OpenTelemetry writes a
trace-id)*. `R` is the union of every type the child's code can throw, found over the whole program,
and of the runtime's error type, `RuntimeError(RuntimeKind Kind, string Detail)`, a member of every
`Reason` (O7; §5, the owner, 2026-10-03), so that a run-time error of chapter 12 reaches the block as
a `RuntimeError` of its kind, with the detail its error line names; the child's code is its
construction and its methods, what they run, the construction of each actor they spawn, and the code
of each actor they spawn under `escalate` or a block, whose crash can pass up to the child, with
what that block throws (U32) *(assumed: as §5 marks it, the type's name, its kinds and its place,
and the order of `R`'s cases, the program's own first and the runtime's last)*. `RuntimeError` and
`RuntimeKind` are names every file sees, as the standard library's `HttpError` is (W1) *(assumed: as
§6 marks it)*. The thrown value is moved out of the child's heap into the supervisor's, with
everything it reaches, before the child's heap is freed (O7; §5, the owner, 2026-10-04), and the
`Crashed` value goes when the block's message ends.

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
top-level function reads, and a block in the top-level code reads what it took (§6, assumed there,
as Akka Typed's `Behaviors.supervise` takes no state of the actor that spawns).

Case: [actors/block-supervisor-state.kz](../corpus/actors/block-supervisor-state.kz)
```kurz
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

### U32 (decided, §6) A throw in the block

A throw inside the block is the supervisor's own crash, and the child stays stopped (§6, the owner,
2026-11-15, #519, comment 6902210646, Q2): the child is ended with the supervisor's other children
(U21), so that a call that waits in its inbox or comes after raises `actor-gone`, and the
supervisor's crash goes as any crash goes (U19 to U25). The child's crash writes its log line as at
`stop` before the supervisor's crash writes its own (U24) *(assumed: as §6 marks it)*.

Case: [actors/block-throw.kz](../corpus/actors/block-throw.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "worker fails"
    }

    pub int Ping() => 1
}

actor Boss {
    pub Worker Go() {
        kid = spawn Worker() on crash (c) {
            throw "block fails"
        }

        send kid.Fail()
        return kid
    }
}

b = spawn Boss()
w = b.Go()
print(w.Ping())
```

### U33 (decided, §6) The block of the top-level code

A block at a `spawn` in the top-level code runs in that code's wait: the wait runs the scheduler in
place (U18), and it runs a pending block before it goes on, and the end of the top-level code drops a
pending block and ends its child (U13) (§6, the owner, 2026-11-16, #519, comment 6919351399, "Root's
wait runs it"). A wait on the call that crashed the child therefore ends with `callee-crashed` (U20)
before the block runs, and the process ends with that error, the block dropped with no line.

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

### U34 (assumed, §6) Pending blocks and the log line

The blocks pending in one supervisor run in the order of the crashes, and a block still pending at
the supervisor's crash or end is dropped with no line, its child ended with the supervisor's other
children (U21, U13); while its block is pending, the child takes no message, so that a call to it
waits in its kept inbox, and a call that waited for it goes on waiting, until the decision (§6, the
owner, 2026-11-16, #519, comment 6926540546, "Keep: it waits"); and the
block's own calls run in a chain of their own, as a restart's construction does (U22). The log line
of a crash that a block decides (U24) is written at the decision, at `restart` and at `stop`, and
none at `escalate` (U25) (§6, assumed there, as Erlang's supervisor reports a child's exit as it
acts on it).

Case: [actors/block-order.kz](../corpus/actors/block-order.kz)

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
sleeps while it waits (U18; §6, Q3). A message parked in a wait goes on on the thread that parked it,
and an actor between messages moves to any thread (§6, the owner, 2026-11-17, #519, comment
6946657505, Q2, "Stays on its thread (Recommended)"). A message that runs on another thread when the
top-level code ends runs to its end or its wait, a wait then dropped as a parked message is (U13), and
the process ends after it; a supervisor that is running acts on its child's crash once its own message
ends or waits (U22) *(assumed in §6)*.

No case: a case is a single file with no `project.kz` (chapter 0) and gives its program no
environment, as `log-filter`'s place in chapter 12 says, and cannot make its system refuse a worker
thread, as `thread-refused`'s place there says, the thread that runs a message shows in
no output, and the two outcomes a program can see, a message that runs on another thread when the
top-level code ends and the moment a running supervisor acts on a crash, depend on the timing between
threads, which a case's output cannot fix.
