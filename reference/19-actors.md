# 19. Actors

An actor is an object with private state and an inbox, which handles one message at a time; a call
of its method through its reference is a message (§6, "Actors"). This chapter holds the actors of
one machine and what a program writes of them: an actor's declaration, `spawn`, the call that waits
for its result, `send`, and what crosses into a message. The scheduler, the stacks, crashes and
supervision, deadlocks, the clauses after an actor's name and the actors of other machines (§6, §7)
have no rule here, and `compiler/README.md` says what the compiler builds of the rules here.

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
an actor, or a `static` field whose type is or holds a class, is `static-state`, as K18 makes a
class's, since a class instance, mutable or not, lives in one actor's heap (K18), and an immutable
`static` field of another type is no `static` state in K18's sense and stands as a class's does
*(assumed: K18's reading of `static` state, which the answer did not name; a class's immutable
`static` field of such a type is reached from every actor, so that one of an actor shares nothing
more, as a Rust `static` that is `Sync` stands)*. How a generic
actor's messages and a call through an interface as a message are built stand open (§14).

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
`spawn` takes the name, its type arguments where the actor has type parameters, written or
inferred as a generic class's construction takes them (T19, T27), and its argument list, and what
follows applies to the reference, so that
`spawn Counter().Get()` calls `Get` on the new actor, as C#'s `new Counter().Get()` binds (§6, the
owner, 2026-11-14, #519, comment 6882624606, Q25, "All six leans (Recommended)"). `equal by` on an
actor and a constructor in its body are `syntax`, since this rule gives the actor's `==` and its
constructor (§6, comment 6882624606, Q26).

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

### U3 (decided, §6) The constructor runs while the spawner waits

An actor's constructor, its fields' defaults with it, runs in the new actor while the code that
spawned it waits, as Elixir's `GenServer.start_link` waits for `init/1`, so that on one machine what
the constructor prints comes before what the spawner prints after its `spawn`, and a throw in it at
the `spawn` is the spawner's crash (§6, the owner, 2026-11-14, #519, comment 6880404917, Q2, "All nine leans (Recommended)"),
while a throw in its run at a restart is the actor's own crash under its policy (§6, the owner,
2026-11-14, #519, comment 6882624606, Q28, "All six leans (Recommended)").

Case: [actors/constructor-arguments.kz](../corpus/actors/constructor-arguments.kz)

### U4 (decided, §5, §6) A call through a reference waits

A call of an actor's `pub` method through its reference, `c.Get()`, is a message, and its caller
waits for its result (§6, "A call waits for its result by default"). The result is read as any
call's: its success is the value, and its other cases leave the calling code or are handled, as O2
to O5 say (§5).

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
2026-11-13, #519, comment 6847932993, Q12, "send-result error (Recommended)").

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

What crosses between actors is a value (`data`, a collection, a string, a number), an immutable
class instance and an actor's reference; a mutable class instance crosses with `copy` or `move`
alone (§6, "What may cross between actors"). A message's arguments and its result cross so, and so
do the arguments of an actor's constructor, which cross as a call's do (§6, the owner,
2026-11-13, #519, comment 6847932993, Q13). A mutable class instance among a message's arguments
or a constructor's with neither word is the compile error `send-needs-word`, at its argument
*(assumed: that place, as Q24 gives a function value's)*, which stands in this
rule and not in the table of errors, as §6 keeps it (§6, the owner, 2026-11-13, #519, comment
6847932993, Q8, "These ids (Recommended)"), and so is a function value there whose captures do not
cross, at its argument, as rustc gives one error, E0277, for a closure as for any type that is not
`Send` (§6, the owner, 2026-11-14, #519, comment 6882624606, Q24, "All six leans (Recommended)").
No word at the argument makes such a function value cross, `copy f` and `move f` among them, the
fix being in what it captures, as Q24's cost reads, "the error stands at the argument, where the
fix is in the capture", and as rustc reads a closure's `Send` from the types of what it captures,
written `move` or not (§6).
An immutable class is one through which nothing
reachable has a `mut` field, and a value crosses where every value it reaches crosses, a
collection's items and a `data` value's fields among them, as Rust's `Send` is structural
*(assumed: the reading of Q14 (i) said back in #519, comment 6882879015, reading 4)*; a
function value crosses where every value it captures crosses, and a
mutable class instance given as a call's result, where no word can stand, is a compile error whose
id stands open (§6, §14, the owner, 2026-11-14, #519, comment 6880404917, Q14, "All eight leans (Recommended)").
A collection or a `data` value that reaches a mutable class instance is `send-needs-word` at its
argument with neither word, and crosses with `copy`, a deep copy of all it reaches, or with
`move`, every path into it ending, `move-reachable` where one cannot *(assumed: that reading of
§6's `copy` and `move`, carried from a mutable class instance to a value that holds one, where
rustc reads `Send` through a struct's fields whatever a call writes)*.

Case: [actors/relay.kz](../corpus/actors/relay.kz)

Case: [actors/union-result.kz](../corpus/actors/union-result.kz)

Case: [actors/this-reference.kz](../corpus/actors/this-reference.kz)

### U13 (decided, §6) The end of the program drops what is queued

When the top-level code ends, a message that runs in an actor it spawned runs to its end, and the
messages still queued behind it are dropped, since the end of the root actor stops its children
after their current message (§6, the owner, 2026-11-13, #519, comment 6847932993, Q16, and the owner, 2026-11-14, #519, comment 6880404917,
Q8, "All nine leans (Recommended)"). The end of any actor stops the actors it spawned so, after
their current message, their queued messages dropped, so that the end of the top-level code reaches
every actor of the machine (§6, the owner, 2026-10-30, "With its supervisor", and the owner,
2026-11-13, #519, comment 6847932993, Q16).

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
`spawn` that is no name, its type arguments where the actor has type parameters (U2), and its
argument list, `spawn Box`, `spawn x.Make()` or `spawn 3`, is `syntax` (E4), as U2's form gives,
`spawn Box` of a class among them, its form read before its name (U16).

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
owner, 2026-11-14, #519, comment 6884160133, item 6, option (a), "Rust's order"). A declaration
in an actor that breaks two rules of this chapter is the error of the word read first, so that
`pub static mut int n = 0` is `actor-field-public` (U8) and not `static-state` (U1), as Q27's
order of reading holds of a declaration as of a call (§6, the owner, 2026-11-14, #519, comment
6884160133, item 7, option (a), "word order").

Case: [actors/first-error-send-as-value.kz](../corpus/actors/first-error-send-as-value.kz)

Case: [actors/first-error-spawn-without-arguments.kz](../corpus/actors/first-error-spawn-without-arguments.kz)

Case: [actors/first-error-spawn-required.kz](../corpus/actors/first-error-spawn-required.kz)

Case: [actors/first-error-mut-in-message.kz](../corpus/actors/first-error-mut-in-message.kz)
