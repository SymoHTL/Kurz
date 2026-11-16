# 19. Actors

An actor is an object with private state and an inbox, which handles one message at a time; a call
of its method through its reference is a message (§6, "Actors"). This chapter holds the actors of
one machine and what a program writes of them: an actor's declaration, `spawn`, the call that waits
for its result, `send`, and what crosses into a message. The scheduler, the stacks, crashes and
supervision, deadlocks but the calls a waiting actor lets in (U17) and the wait of the top-level code
that nothing can end (U18), the clauses after an actor's name but the stack's (U1) and the actors of
other machines (§6, §7) have no rule here, and `compiler/README.md` says what the compiler builds of the rules here.

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
actor's messages and a call through an interface as a message are built stand open (§14).

The stack's clause is `stack` and a literal of bytes, `kb`, `mb` or `gb` (L14), `actor Parser stack 8mb
{ ... }`: it sets the size of the stack each message of the actor runs on, which is 2 MiB without it,
as a thread that Rust's standard library spawns has (§6, the owner, 2026-11-13, #519, comment
6847932993, Q4, "2 MiB + clause", and the owner, 2026-11-15, #519, comment 6892812780, Q3, "Build it now"). The size
is read as a `long`, so that a literal that is no whole number of bytes is `inexact-literal` (L14)
and one past the largest `long` is `constant-overflow`, as a literal that does not fit the type it
takes (L10); the runtime rounds it up to a whole page and raises it to its floor, 64 KiB, with no
message, so that the clause sets at least what it says (§6, the owner, 2026-11-15, #519, comment 6892835500, Q7,
"Rust/C#: round up silently") *(assumed: the floor, as §6 marks it)*, and a stack the system refuses
ends the process with `allocation-failed`, naming the size so rounded (O11) *(assumed: the size, as §6
marks it)*. `stack` with no literal of bytes after it, a number with no unit or with another unit
among them, and `stack` written twice are `syntax` (E4) *(assumed: as §6 marks it)*. On a target where
no actor has a stack of its own the clause builds and sets nothing (§6, the owner, 2026-11-13, #519,
comment 6851105334, "Accept, set nothing (Recommended)").

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
and its text is its actor's type and number, `Counter#1`, the number counted from 1 in the order in
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

`spawn` stands wherever an expression does, in a function, a class's method, a lambda and a field's
default among them, as a call that waits does (U4).

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

### U6 (decided, §6) `send`

`send` before a call of an actor's method, `send c.Add(1)`, makes the call a message that nobody
waits for (§6, "`send` makes a call fire-and-forget"). `send <call>` stands as a statement and gives
no value, so that a `send` where a value stands, `print(send c.Add(1))`, is `syntax`, as is a call
whose success is `void` used as a value (O10). A `send` of a method that gives a result is
the compile error `send-result`, since its result and its cases would reach nobody (§6, the owner,
2026-11-13, #519, comment 6847932993, Q12, "send-result error (Recommended)"). `send` stands
wherever a call does, in a function, a class's method and a lambda among them, as a call that
waits does (U4).

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
immutable class instance where every value it reaches crosses, a function value where every value
it captures crosses, and an actor's reference; a mutable class instance crosses with `copy` or
`move` alone, and so does a collection or a `data` value that reaches one, every value it reaches
crossing or being a mutable class instance (§6, "What may cross between actors"). A message's arguments and its result cross so, and so
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
fields)*. A value of an interface's type given as a message's result is, by its written type, the
compile error of a mutable class instance given as a call's result (§6, the owner, 2026-11-15,
issue 519, comment 6892275110, item 3, option (a), "Same error, by written type").
An immutable class is one through which nothing
reachable has a `mut` field, and a value crosses where every value it reaches crosses, a
collection's items and a `data` value's fields among them, as Rust's `Send` is structural
*(assumed: the reading of Q14 (i) said back in #519, comment 6882879015, reading 4)*; a
function value crosses where every value it captures crosses, and a
mutable class instance given as a call's result, where no word can stand, is a compile error (§6,
the owner, 2026-11-14, #519, comment 6880404917, Q14, "All eight leans (Recommended)"), and that
error is `send-needs-word`, as rustc gives the same error to a task's output as to its input
(`tokio::spawn` asks `F::Output: Send`) (§6, the owner, 2026-11-15, #519, comment 6896192351, Q4,
"Rust: send-needs-word"), at the line of the call *(assumed: that place, since no argument stands
for a result)*.
A collection or a `data` value that reaches a mutable class instance is `send-needs-word` at its
argument with neither word, and crosses with `copy`, a deep copy of all it reaches up to a function
value or an actor's reference, or with `move`, every path into it ending, `move-reachable` where
one cannot *(assumed: that reading of §6's `copy` and `move`, carried from a mutable class instance
to a value that holds one, where rustc reads `Send` through a struct's fields whatever a call
writes)*. A collection, a `data` value or a class instance that reaches a function value whose
captures do not cross is `send-needs-word` at its argument with a word or with none, as that
function value is, the paths `move` ends stopping at a function value it reaches *(assumed: Q24's
cost read for a word at the argument as for that function value, as rustc reads `Send` through a
struct's fields and a closure's through its captures)*. A reference that crosses names the same
actor, so that `==` holds across a crossing (§6, the owner, 2026-11-14, #519, comment 6880404917,
Q10). The deep copy of `copy` stops at a function value it reaches, which the receiver gets as it
would with no word, shared by its count with the values it captures, `copy f` of one whose
captures cross among them, and at an actor's reference, which the receiver gets naming the same
actor, as a reference that crosses names it *(assumed, §6: the stop at a function value, Q24's cost read for a word at the argument, as rustc reads a closure's
`Send` through its captures; the stop at an actor's reference, as Rust's clone of a channel's
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
*(assumed: that a lambda the function shows is one written at the argument or the one an immutable
local variable is declared with, and that the named function the answer names stands for no case,
since F11 gives a function value through a lambda only)*. `copy` before an argument is a deep copy wherever it is
written, a call that crosses nothing included, as Rust's `.clone()` stands anywhere (§6, the owner,
2026-11-15, #519, comment 6896192351, Q6, "Rust: deep copy anywhere"). The sender makes the copy,
at the send; every class instance a crossing reaches is copied, so that identity does not survive
a crossing, and every other block is shared by its count, which no program can tell from a copy;
the copy walks a work list and never recurses, so that a chain of a million links crosses on a
stack of 2 MiB; and a copy runs no program code, no `Text()` and no `equal by` *(assumed, §6: the
points comment 6895076937 takes as following from §6, which the answers left standing)*. `copy`
and `move` are words before an argument alone, as `mut` is there (L12), `move` before a parameter's
type and inside a function type too, as `mut` is before a parameter's (§6, the owner, 2026-11-05,
issue 156, comment 6657251688), and names everywhere else,
so that `copy(x)` is the word before `(x)` and a variable named `copy` is called as `(copy)(x)` (§6,
the owner, 2026-11-13, #519, comment 6847932993, Q10, "Any order + contextual (Recommended)"); a
`copy` or a `move` followed by `[`, `-`, `*`, `&` or `with`, which go on from a name, is the name,
so that `copy[0]` reads an item of a list named `copy` *(assumed, §6: as a name before what goes
on from a name is read, which the answers did not give)*.

A `move` ends every path into the value it sends: the checker tracks, in each function, every
variable that reaches into the value, all of them dead after the `move`, and refuses the `move`
with `move-reachable` where the value may be reached from outside the function (§6, the owner,
2026-11-03, issue 156, comment 6603205575). A function's values form regions, as in Swift's
SE-0414: a parameter written `move` starts a region of its own, and every other parameter, `this`
with its fields, the captured variables and the statics form one region outside the function
*(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A call merges the regions of all its arguments, written `mut` or not, and its result's, as
SE-0414 does, since `F(a, b)` can store `b` in a `mut` field of `a`, and an argument that crosses
with no word merges nothing (§6, 2026-11-15, Q2) *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A lambda joins the regions of what it
captures, so that `f = () => m.N; send a.T(move m); f()` is `used-after-move` at `f()`, and a
`move` of a captured variable inside a lambda is `move-reachable` *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. A `match` binding joins its
subject's region, as a field read does, and each arm is a branch *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. `p with { A = q }` is read
as a call that takes `p` and `q`, its result joining both regions *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. Branches unite what they
moved: after an `if`, a `match`, `&&` or `||`, a use is `used-after-move` where any path moved the
value, and the error names every `move` line that reaches the use *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. In a loop, a `move x` in a
body that does not assign `x` again before the round ends is `used-after-move` at that `move`,
moved there in an earlier round; an assignment makes a variable live again, and a variable
declared in the body is new each round *(assumed, §6: the point of comment 6897444564 that the answers left standing)*. `move p.A` and `move xs[0]` stand and end `p` or `xs`
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
"Rust: loop takes list"). A parameter written `move` takes an argument written `move`, or one
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
not written takes `move` from the function type the lambda is given for, a lambda whose parameter
is written without `move` fits such a type and holds the parameter as its caller's, and one written
`move` where that type has none is `type-mismatch` (§6, the owner, 2026-11-05, issue 156, comment
6657251688, Q4). A parameter takes one of `mut` and `move`, `move` standing before its type, a type
named `move` before a name is read as the word, and a parameter of a C function's type (R17) takes no
`move`, which is `syntax`, since C code keeps no count *(assumed, §6: as `mut` and `move` are words
before an argument, which the round did not ask)*. On one thread `move` copies nothing and changes
no count: the moved place's count becomes the message's or the parameter's, and a variable moved on
some paths alone is let go of through a null test at its end, as rustc keeps a drop flag (§6, the
owner, 2026-11-15, #519, comment 6895076937) *(assumed, §6: the point of comment 6897444564 that the answers left standing)*.

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
mut names = Map<Counter, string>()
names[a] = "a"
print(names[a] ?? "none")
```

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

### U13 (decided, §6) The end of the program drops what is queued

When the top-level code ends, a message that runs in an actor it spawned runs to its end, and the
messages still queued behind it are dropped, since the end of the root actor stops its children
after their current message (§6, the owner, 2026-11-13, #519, comment 6847932993, Q16, and the owner, 2026-11-14, #519, comment 6880404917,
Q8, "All nine leans (Recommended)"). The end of any actor stops the actors it spawned so, after
their current message, their queued messages dropped, so that the end of the top-level code reaches
every actor of the machine (§6, the owner, 2026-10-30, "With its supervisor", and the owner,
2026-11-13, #519, comment 6847932993, Q16). A message parked in a wait when the top-level code ends
is dropped with the process, as a queued one is, its lines after the wait never running, as tokio
drops its tasks when its runtime shuts down (§6, the owner, 2026-11-15, #519, comment 6892835500, Q5, "Rust:
dropped").

No case: which message runs when the top-level code ends is the scheduler's to say where actors run
on more than one thread, so that a case's output would hang on it.

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
print(s.Save(4))
print(s.Load())
```

Case: [actors/send-own-method.kz](../corpus/actors/send-own-method.kz)

Case: [actors/this-reference.kz](../corpus/actors/this-reference.kz)

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
`move-at-call` of an argument (§6, the owner, 2026-11-15, #519, comment 6897778812, Q3) and
`used-after-move` of an argument after a `move` in the same call *(assumed, §6: that place, which the
round of comment 6897444564 did not ask)*, and between them and `mut-in-message` the error of the
argument written first comes first, as rustc's borrow check,
whose errors both are, sorts its errors by their place (§6, the owner, 2026-11-15, issue 519,
comment 6891937666, item 4, option (a), "Rust: by argument order"). An argument's own error
stands alone before U14's `cannot-infer` of `spawn`'s type arguments, which is said only where every
argument reads clean (§6, the owner, 2026-11-15, issue 519, comment 6892275110, item 1, option (a),
"Argument's error only"). A declaration
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
2026-11-15, #519, comment 6892835500, Q6, "Own pooled stack"). An actor under construction takes no
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

### U18 (decided, §6) A wait of the top-level code that nothing can end

The top-level code's call that waits runs the actors' messages in place, on the main thread's stack,
until its reply is set, and never parks, as tokio's `block_on` runs its runtime on the thread that
calls it *(assumed in §6)*. A wait of it that finds nothing left to run ends the process with
`error: deadlock at <file>:<n>`, the line of that call, and the exit code 1, so that two actors that
call each other from different chains and wait for each other, on one thread, do not hang silently
(§6, "Deadlocks"; the owner, 2026-11-15, #519, comment 6892812780, Q4, "deadlock at top-level
wait"). A call that waits in a function the top-level code called is the top-level code's, at its
own line. A loop of actors the top-level code never waits on stays until the end of the program,
where U13 drops it.

Case: [actors/deadlock.kz](../corpus/actors/deadlock.kz)
```kurz
actor Left {
    pub void Ask(Right other) {
        print("left asks")
        print("left got {other.Value()}")
    }

    pub int Value() => 1
}

actor Right {
    pub void Ask(Left other) {
        print("right asks")
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
