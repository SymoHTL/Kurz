using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions on lists and strings that no corpus case pins (M9; design record, section 3; the owner, 2026-10-24, "Full
/// counting now" and "Items after the Add"): a list is a value whose storage is shared until one side writes (M3), the counting frees
/// a block nothing holds, which the allocator gives out again, and a lambda's environment as it frees a list's block (the owner,
/// 2026-10-25, "Pair + heap env"), and a string's block, which a literal does not have (the owner, 2026-10-29, "Bit in the count"
/// and "string.Allocate, take over"), no block is read after it is freed, the number `allocation-failed`
/// reports past the limit and for a refused growth, and what the emitter says `not supported` to; what the checker refuses of a
/// read beside a `mut` call (M7) the corpus pins.
/// </summary>
public sealed class ListTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    // nested lists, a list in a data value and in `with`, a path assignment, a list of strings, a loop's `continue`, a list in a
    // value laid out as a block, and the texts
    private const string Values = """
        data Cart(string Owner, List<int> Items)
        data Leaf
        data Tree(List<int> Items, Tree | Leaf Next)

        List<int> Range(int n) {
            mut xs = List<int>()
            mut i = 0
            while i < n {
                xs.Add(i)
                i = i + 1
            }
            return xs
        }

        int Sum(List<int> xs) {
            mut total = 0
            for x in xs {
                total = total + x
            }
            return total
        }

        mut grid = List<List<int>>()
        grid.Add(Range(3))
        grid.Add(Range(0))
        grid.Add(Range(2))
        print(grid)
        mut row = grid[0]
        row.Add(9)
        print(row)
        print(grid)
        c = Cart("Ann", Range(4))
        print(c)
        d = c with { Owner = "Bea" }
        print(d.Items.Count)
        print(Sum(d.Items))
        mut e = c
        e.Items = Range(1)
        print(c)
        print(e)
        mut names = List<string>()
        names.Add("a")
        names.Add("b")
        print(names)
        for r in grid {
            if r.Count == 0 {
                continue
            }
            print(Sum(r))
        }
        t = Tree(Range(2), Leaf)
        mut ts = t.Items
        ts.Add(9)
        print(t)
        print(ts)
        """;

    private static readonly string[] ValuesPrint =
    [
        "[[0, 1, 2], [], [0, 1]]",
        "[0, 1, 2, 9]", // the row's own value: the grid keeps its list (M3)
        "[[0, 1, 2], [], [0, 1]]",
        "Cart { Owner = Ann, Items = [0, 1, 2, 3] }",
        "4",
        "6",
        "Cart { Owner = Ann, Items = [0, 1, 2, 3] }",
        "Cart { Owner = Ann, Items = [0] }",
        "[a, b]",
        "3",
        "1",
        "Tree { Items = [0, 1], Next = Leaf }", // a list a block holds: the block keeps it (M3)
        "[0, 1, 9]",
    ];

    // a `mut` parameter beside a parameter given a copy of the same list, a loop over a list that the loop adds to, the name an arm
    // binds while the arm changes the subject's variable, many short lists, a function value and a data value that holds one read
    // beside a call that changes their variables, a lambda's list whose variable changed, and the items `Where` keeps
    private const string Sharing = """
        data Empty
        data Row(List<int> Cells)
        data Rule(() => int Step, int Limit)

        void Fill(mut List<int> xs, List<int> seen) {
            xs.Add(7)
            print(seen.Count)
        }

        int Keep(() => int f, int ignored) => f()

        int Swap(mut () => int f) {
            f = () => 0
            return 0
        }

        int SwapStep(mut (int) => int f) {
            f = delta => 0
            return 0
        }

        int KeepRule(Rule r, int ignored) => r.Step()

        int SwapRule(mut Rule r) {
            r = Rule(() => 0, 0)
            return 0
        }

        List<int> | Empty Pair() {
            mut xs = List<int>()
            xs.Add(1)
            xs.Add(2)
            return xs
        }

        mut a = List<int>()
        a.Add(1)
        before = a
        Fill(mut a, before)
        print(a.Count)
        mut b = a
        for x in b {
            b.Add(x)
        }
        print(a)
        print(b)
        mut List<int> | Empty u = Pair()
        match u {
            List<int> l => {
                u = Empty
                mut ys = List<int>()
                ys.Add(7)
                ys.Add(8)
                print(l)
            }
            Empty => print(0)
        }
        mut i = 0
        while i < 1000 {
            mut t = List<int>()
            t.Add(i)
            t.Add(i)
            i = i + 1
        }
        print(i)
        mut rows = List<List<int>>()
        mut row = List<int>()
        row.Add(3)
        rows.Add(row)
        row = List<int>()
        mut k = 0
        while k < 8 {
            rows.Add(rows[0])
            k = k + 1
        }
        print(rows[8])
        mut cells = List<Row>()
        mut kept = List<int>()
        kept.Add(4)
        cells.Add(Row(kept))
        kept = List<int>()
        mut c = 0
        while c < 8 {
            cells.Add(cells[0])
            c = c + 1
        }
        print(cells[8].Cells)
        mut held = List<int>()
        held.Add(5)
        held.Add(6)
        mut read = () => held.Count
        held = List<int>()
        print(Keep(read, Swap(mut read)))
        mut steps = List<int>()
        steps.Add(1)
        mut rule = Rule(() => steps.Count, 1)
        steps = List<int>()
        print(KeepRule(rule, SwapRule(mut rule)))
        mut nested = List<List<int>>()
        mut one = List<int>()
        one.Add(9)
        nested.Add(one)
        nested.Add(List<int>())
        one = List<int>()
        survivors = nested.Where(inner => inner.Count > 0)
        nested = List<List<int>>()
        print(survivors)
        mut counted = List<int>()
        counted.Add(3)
        counted.Add(4)
        counted.Add(5)
        mut step = (int delta) => delta + counted.Count
        counted = List<int>()
        print(step(SwapStep(mut step)))
        """;

    private static readonly string[] SharingPrint =
    [
        "1", // the parameter keeps the value it was given (M3, M7)
        "2",
        "[1, 7]",
        "[1, 7, 1, 7]", // the loop walks the list as it was when it started (C6)
        "[1, 2]", // the name keeps the value it was bound to (M3), and the freed block the next lists take is not its own
        "1000",
        "[3]", // an item read for its own list's `Add` across two growths, held by the list's slot alone (M7)
        "[4]", // the same for an item that holds a list
        "2", // the value a call is given beside a `mut` call that replaces it, a function value whose environment it alone held (M7, "Copied")
        "1", // the same for a data value that holds one ("Copied too"), and a lambda's list that only its environment holds (F15)
        "[[9]]", // an item `Where` keeps, which only the source list held before the source was let go of
        "3", // the function value a call calls, beside a `mut` call that replaces it, whose environment it alone held (M7, "Copied")
    ];

    // every way out of a block a list is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, a copy written to, and a lambda's environment in a variable and
    // in a list
    private const string Rounds = """
        data Pair(List<int> A, int B)

        List<int> Make(int n) {
            mut xs = List<int>()
            mut i = 0
            while i < n {
                xs.Add(i)
                i = i + 1
            }
            return xs
        }

        int Early(int n) {
            xs = Make(n)
            if n > 0 {
                return xs.Count
            }
            return 0
        }

        void Drop(int n) => Make(n)

        mut round = 0
        mut total = 0
        while round < 3000 {
            round = round + 1
            a = Make(1000)
            mut b = a
            b.Add(1)
            p = Pair(Make(1000), 1)
            q = p with { B = 2 }
            for x in Make(1000) {
                if x == 3 {
                    break
                }
            }
            for j in 0..<3 {
                c = Make(1000)
                if j == 1 {
                    continue
                }
                total = total + c.Count
            }
            Make(1000)
            Drop(1000)
            l = Make(1000)
            h = () => l.Count
            mut hs = List<() => int>()
            hs.Add(h)
            hs.Add(() => Make(1000).Count)
            total = total + Early(1000) + q.A.Count + b.Count - a.Count + hs[0]() + hs[1]() - 2000
        }
        print(round)
        print(total)
        """;

    // strings in variables, fields, a `with`, a path assignment, an arm's name while the arm changes the subject's variable, a
    // lambda's environment, a list that copies and grows, the items `Where` keeps, a loop over a list its variable lets go of, the
    // parameters of a call, an interpolation, the text of a value laid out as a block, literals, a string made in a `raw` block,
    // an interpolation of one hole beside a call that changes the place the hole read, the values `??` and `?.` give, a read of a
    // nullable variable known not null, an `Add` through the place of one, a `match` with a `null` arm on one whose arm changes it, and
    // a nullable parameter read in the arm of its case and after a `match` whose `null` arm leaves
    private const string Strings = """
        data Person(string Name, int Age)
        data Tag(string Label)
        data Leaf
        data Node(string Label, Node | Leaf Next)
        data Contact(string? Email)

        string Same(string s) => s

        string Made(int n) => "n{n}"

        string Shown(string? s) {
            match s {
                null => return "none"
                string inner => print(s)
            }
            return s
        }

        string Raw(int n) {
            mut string s = ""
            raw {
                byte* p = string.Allocate(3)
                p[0] = byte(48 + n)
                p[1] = byte(33)
                s = string.FromRaw(p, 2)
            }
            return s
        }

        void Show(string a, string b) {
            print(a)
            print(b)
        }

        string Rename(mut string t) {
            t = Made(21)
            return Made(22)
        }

        string First(string a, string b) => a

        string | InvalidUtf8 Wrapped(Bytes b) {
            inner = string.FromUtf8(b)
            return "<{inner}>"
        }

        mut first = Made(1)
        second = first
        first = Made(2)
        print(second)
        print(first)
        person = Person(Made(3), 30)
        older = person with { Age = 31 }
        mut renamed = older
        renamed.Name = Made(4)
        print(person)
        print(older)
        print(renamed)
        mut Tag | Leaf u = Tag(Made(5))
        match u {
            Tag g => {
                u = Leaf
                w = Made(6)
                print(g.Label)
                print(w)
            }
            Leaf => print(0)
        }
        mut string | Leaf v = Made(7)
        match v {
            string x => {
                v = Leaf
                y = Made(8)
                print(x)
                print(y)
            }
            Leaf => print(0)
        }
        mut held = Made(9)
        f = () => held
        held = Made(10)
        print(f())
        mut names = List<string>()
        names.Add(Made(11))
        mut k = 0
        while k < 8 {
            names.Add(names[0])
            k = k + 1
        }
        mut copy = names
        copy.Add(Made(100))
        copy.Add("lit")
        names = List<string>()
        print(copy)
        kept = copy.Where(x => x.Bytes.Count == 4)
        copy = List<string>()
        print(kept)
        mut words = List<string>()
        words.Add(Made(12))
        words.Add(Made(13))
        for word in words {
            words = List<string>()
            z = Made(14)
            print("{word} {z}")
        }
        Show(Made(15), Same(Made(16)))
        e = ""
        print("{Made(17)}{e}{Made(18)}")
        node = Node(Made(19), Node(Made(20), Leaf))
        print(node)
        print(Raw(1))
        print(Raw(2).Bytes.Count)
        mut named = Made(23)
        print(First("{named}", Rename(mut named)))
        print(named)
        mut bytes = Made(24).Bytes
        print(bytes)
        text = string.FromUtf8Lossy(bytes)
        bytes.Add(33)
        print(text)
        print(bytes)
        mut parts = List<string>()
        parts.Add(Made(25))
        parts.Add(Made(26))
        part = parts[1..<2]
        parts = List<string>()
        print(part)
        print("{Made(27)}".Bytes[1])
        match Wrapped(Made(28).Bytes) {
            string inner => print(inner)
            InvalidUtf8 bad => print(bad.At)
        }
        mut string? maybe = Made(29)
        fallback = maybe ?? "none"
        maybe = null
        print(fallback)
        print(maybe ?? Made(30))
        mut Contact? holder = Contact(Made(31))
        email = holder?.Email ?? "none"
        holder = null
        print(email)
        print(holder?.Email ?? "none")
        maybe = Made(32)
        if maybe != null {
            known = maybe
            maybe = null
            print(known)
        }
        mut List<string>? items = List<string>()
        items.Add(Made(33))
        print(items)
        maybe = Made(34)
        match maybe {
            null => print("none")
            string present => {
                maybe = null
                print(present)
            }
        }
        print(Shown(Made(35)))
        print(Shown(null))
        print(Contact(null))
        raw {
            room = Bytes.Allocate(4)
            room[0] = 104
            room[1] = 105
            made = Bytes.FromRaw(room, 2)
            print(string.FromUtf8Lossy(made))
            print(made.Pointer[1])
        }
        """;

    private static readonly string[] StringsPrint =
    [
        "n1", // the variable keeps the string it was given
        "n2",
        "Person { Name = n3, Age = 30 }",
        "Person { Name = n3, Age = 31 }",
        "Person { Name = n4, Age = 31 }",
        "n5", // the name keeps the string it was bound to, and the freed block the next string takes is not its own
        "n6",
        "n7", // the same for a case that is a string
        "n8",
        "n9", // the environment keeps the string its lambda read
        "[n11, n11, n11, n11, n11, n11, n11, n11, n11, n100, lit]", // an item read for its own list's `Add` across growths
        "[n100]", // an item `Where` keeps, which only the source list held before the source was let go of
        "n12 n14", // the loop walks the list as it was when it started (C6)
        "n13 n14",
        "n15",
        "n16",
        "n17n18",
        "Node { Label = n19, Next = Node { Label = n20, Next = Leaf } }", // strings a value laid out as a block holds
        "1!",
        "2",
        "n23", // one hole of a string is a new value, which the call that changes the place it read leaves whole
        "n21",
        "[110, 50, 52]", // the bytes of a string a call made, which share its block past the statement
        "n24", // a string of the bytes, which shares their block, and an `Add` that copies it
        "[110, 50, 52, 33]",
        "[n26]", // a part holds its items' blocks
        "50", // a byte read in place
        "<n28>", // a string of bytes in a function, through the success of its union
        "n29", // the value `??` gives, which the variable's next value leaves whole
        "n30", // the right side of `??`, made where the left is null
        "n31", // the member `?.` reads, which the variable's next value leaves whole
        "none",
        "n32", // a read of a variable known not null, the string of its case
        "[n33]", // an `Add` through the place of a list in a nullable variable
        "n34", // the name of a `null` arm's `match` on a variable known not null, which the arm's assignment leaves whole
        "n35", // a parameter read in the arm of its case
        "n35", // and after a `match` whose `null` arm leaves
        "none",
        "Contact { Email = null }",
        "hi", // a list that took over the room `Bytes.Allocate` gave
        "105",
    ];

    // every way out of a block a string is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, an assignment over a variable, a lambda's environment in a
    // variable and in a list, a list of strings, an interpolation of one hole of 4096 bytes, the bytes of such a string, a string of
    // them, which shares their block, and a part of them; each other string of 1024 bytes, joined from halves the join lets go of
    private const string StringRounds = """
        data Pair(string A, int B)

        string Make(int n) {
            mut s = "x"
            mut i = 0
            while i < n {
                s = "{s}{s}"
                i = i + 1
            }
            return s
        }

        int Early(int n) {
            s = Make(n)
            if n > 0 {
                return s.Bytes.Count
            }
            return 0
        }

        void Drop(int n) => Make(n)

        mut round = 0
        mut total = 0
        while round < 3000 {
            round = round + 1
            a = Make(10)
            mut b = a
            b = "{b}!"
            p = Pair(Make(10), 1)
            q = p with { B = 2 }
            for x in 0..<3 {
                c = Make(10)
                if x == 1 {
                    continue
                }
                total = total + c.Bytes.Count
                if x == 2 {
                    break
                }
            }
            Make(10)
            Drop(10)
            l = Make(10)
            h = () => l.Bytes.Count
            mut hs = List<() => int>()
            hs.Add(h)
            hs.Add(() => Make(10).Bytes.Count)
            mut ss = List<string>()
            ss.Add(Make(10))
            ss.Add(a)
            lone = "{Make(12)}"
            bb = Make(12).Bytes
            tt = string.FromUtf8Lossy(bb)
            pp = bb[0..<4000]
            total = total + Early(10) + q.A.Bytes.Count + b.Bytes.Count - a.Bytes.Count + hs[0]() + hs[1]() + ss[0].Bytes.Count - 4096 + lone.Bytes.Count - 4096 + bb.Count - 4096 + tt.Bytes.Count - 4096 + pp.Count - 4000
        }
        print(round)
        print(total)
        """;

    // maps (M9): a read that is a value of its own, a write over a key, a copy written to (M3), a value read from the map it is
    // written into, a key the map keeps after its variable changes, a `mut` parameter, a missing key, growth, a map in a data value
    // and in `with`, a map of lists, a list of maps, a map of maps, a lambda's copy, keys of each kind the checker takes, a map in a
    // union, a block replaced by growth under a `mut` parameter and under a lambda that read it, strings equal by their bytes with
    // and without a block, and the texts
    private const string Maps = """
        data Box(string Name, Map<string, string> Tags)

        string Made(int n) => "made {n}"

        int Total(Map<int, int> m, int n) {
            mut total = 0
            for i in 0..<n {
                total = total + (m[i] ?? 0)
            }
            return total
        }

        void Fill(mut Map<string, string> m, string k) {
            m[k] = "{k}!"
        }

        data NotFound
        data Holder(Map<string, string> M, int N)

        Map<string, int> | NotFound Lookup(int n) {
            if n == 0 {
                return NotFound
            }
            mut m = Map<string, int>()
            m["n"] = n
            return m
        }

        void Grow(mut Map<int, string> m, int n) {
            for i in 0..<n {
                m[i] = "v{i}"
            }
        }

        Map<string, int> Plus(Map<string, int> m) {
            mut copied = m
            copied["z"] = 26
            return copied
        }

        string Joined(string x, string y) => "{x}{y}"

        mut names = Map<string, string>()
        for i in 0..<20 {
            names[Made(i)] = Made(i + 100)
        }
        kept = names[Made(3)] ?? "none"
        names[Made(3)] = "over"
        print(kept)
        print(names[Made(3)] ?? "none")
        copy = names
        names[Made(4)] = "changed"
        print(copy[Made(4)] ?? "none")
        print(names[Made(4)] ?? "none")
        names[Made(5)] = names[Made(6)] ?? "none"
        print(names[Made(5)] ?? "none")
        mut key = Made(7)
        names[key] = "by key"
        key = "other"
        print(names[Made(7)] ?? "none")
        Fill(mut names, Made(8))
        print(names[Made(8)] ?? "none")
        print(names["missing"] ?? "none")
        mut squares = Map<int, int>()
        for i in 0..<100 {
            squares[i] = i * i
        }
        print(Total(squares, 100))
        mut tags = Map<string, string>()
        tags[Made(1)] = Made(2)
        b = Box("box", tags)
        tags[Made(1)] = "blue"
        c = b with { Name = "other" }
        print(b.Tags[Made(1)] ?? "none")
        print(c)
        mut lists = Map<string, List<string>>()
        mut xs = List<string>()
        xs.Add(Made(9))
        lists["a"] = xs
        xs.Add(Made(10))
        print(lists["a"] ?? List<string>())
        mut maps = List<Map<string, string>>()
        maps.Add(tags)
        tags[Made(1)] = "green"
        print(maps[0][Made(1)] ?? "none")
        mut nested = Map<string, Map<string, string>>()
        nested["in"] = tags
        tags = Map<string, string>()
        print(nested["in"] ?? Map<string, string>())
        f = () => names[Made(0)] ?? "none"
        names[Made(0)] = "after"
        print(f())
        mut wide = Map<long, string>()
        int minus = -1
        wide[minus] = "minus one"
        print(wide[-1] ?? "none")
        mut small = Map<byte, int>()
        small[200] = 7
        print(small[200] ?? 0)
        mut big = Map<ulong, int>()
        big[18446744073709551615UL] = 8
        print(big[18446744073709551615UL] ?? 0)
        print(big[0] ?? 0)
        mut answers = Map<bool, string>()
        answers[true] = "yes"
        answers[false] = "no"
        print(answers[true] ?? "none")
        print(answers[false] ?? "none")
        mut chars = Map<char, int>()
        chars['ü'] = 9
        print(chars['ü'] ?? 0)
        print(chars['u'] ?? 0)
        empty = Map<string, int>()
        print(empty)
        mut one = Map<int, string>()
        one[1] = "a"
        one[1] = "b"
        print(one)

        // a value read and written back to its own key
        mut words = Map<string, string>()
        words["a"] = "x"
        words["a"] = Joined(words["a"] ?? "", "y")
        print(words["a"] ?? "none")

        // a map in a union
        match Lookup(3) {
            NotFound => print("none")
            Map<string, int> found => print(found["n"] ?? -1)
        }
        match Lookup(0) {
            NotFound => print("none")
            Map<string, int> found => print(found["n"] ?? -1)
        }

        // a mut parameter whose block the callee replaces by growth
        mut grown = Map<int, string>()
        grown[0] = "first"
        Grow(mut grown, 50)
        print(grown[49] ?? "none")
        print(grown[0] ?? "none")

        // a lambda keeps the block it captured while the variable grows
        mut seen = Map<int, string>()
        seen[1] = "one"
        look = () => seen[1] ?? "gone"
        Grow(mut seen, 40)
        print(look())
        print(seen[1] ?? "none")

        // a copy made inside a function, the parameter untouched
        base = Map<string, int>()
        more = Plus(base)
        print(base)
        print(more)

        // keys equal by their bytes, from a literal and from a join; the empty string
        mut keys = Map<string, int>()
        keys["ab"] = 1
        print(keys[Joined("a", "b")] ?? 0)
        keys[Joined("c", "d")] = 2
        print(keys["cd"] ?? 0)
        keys[""] = 3
        print(keys[""] ?? 0)
        keys[Joined("a", "b")] = 4
        print(keys["ab"] ?? 0)

        // narrow and wide integer keys of both signs, and a char beyond 16 bits
        mut shorts = Map<short, int>()
        short neg = -5
        shorts[neg] = 5
        print(shorts[-5] ?? 0)
        mut uints = Map<uint, int>()
        uints[4294967295U] = 7
        print(uints[4294967295U] ?? 0)
        mut emoji = Map<char, string>()
        emoji['😀'] = "smile"
        print(emoji['😀'] ?? "none")

        // a map of maps written through a copy of the inner map
        mut outer = Map<string, Map<string, int>>()
        mut inner = Map<string, int>()
        inner["x"] = 1
        outer["a"] = inner
        mut changed = outer["a"] ?? Map<string, int>()
        changed["x"] = 2
        outer["a"] = changed
        print(inner["x"] ?? 0)
        print((outer["a"] ?? Map<string, int>())["x"] ?? 0)

        // a map in a data value, and `with`
        h = Holder(words, 1)
        words["a"] = "changed"
        h2 = h with { N = 2 }
        print(h2.M["a"] ?? "none")

        // texts
        mut listed = Map<int, List<int>>()
        mut items = List<int>()
        items.Add(1)
        items.Add(2)
        listed[7] = items
        print(listed)
        mut deep = Map<int, Map<int, int>>()
        mut single = Map<int, int>()
        single[1] = 2
        deep[3] = single
        print(deep)

        // a write into a nullable map known not null reaches the map in the union's room
        mut Map<string, int>? maybe = Map<string, int>()
        if maybe != null {
            maybe["a"] = 1
            print(maybe["a"] ?? 0)
        }
        print(maybe)
        """;

    private static readonly string[] MapsPrint =
    [
        "made 103",
        "over",
        "made 104", // the copy keeps what it had (M3)
        "changed",
        "made 106",
        "by key",
        "made 8!",
        "none",
        "328350", // 100 entries, which grow the map from 8 slots to 128
        "made 2",
        "Box { Name = other, Tags = [made 1: made 2] }",
        "[made 9]",
        "blue",
        "[made 1: green]",
        "made 100", // the lambda's copy of `names` (F15)
        "minus one", // an `int` key widened to the `long` of the map (T8)
        "7",
        "8",
        "0",
        "yes",
        "no",
        "9",
        "0",
        "[]",
        "[1: b]", // a write over a key keeps one entry
        "xy", // a value read from its own key and written back to it
        "3", // a map in a union
        "none",
        "v49", // a `mut` parameter whose block the callee replaced as it grew
        "v0",
        "one", // a lambda keeps the block it read while the variable grows
        "v1",
        "[]", // a copy written to inside a function, the argument as it was
        "[z: 26]",
        "1", // keys equal by their bytes, a literal's and a joined string's, whose count carries the mark of a block
        "2",
        "3", // the empty string
        "4",
        "5", // a negative `short`, widened with its sign
        "7",
        "smile", // a `char` beyond 16 bits
        "1", // a map of maps written through a copy of its entry
        "2",
        "xy", // a map in a data value and in `with`
        "[7: [1, 2]]",
        "[3: [1: 2]]",
        "1", // a write into a nullable map known not null
        "[a: 1]",
    ];

    // every way out of a block a map is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, a copy written to, a lambda's environment, a list of maps, a map
    // of maps whose entry is written over, and a map of strings whose value and key a write over a key lets go of
    private const string MapRounds = """
        data Pair(Map<int, int> A, int B)

        Map<int, int> Make(int n) {
            mut m = Map<int, int>()
            for i in 0..<n {
                m[i] = i
            }
            return m
        }

        int Seven(Map<int, int> m) => m[7] ?? 0

        int Early(int n) {
            m = Make(n)
            if n > 0 {
                return Seven(m)
            }
            return 0
        }

        void Drop(int n) => Make(n)

        mut round = 0
        mut total = 0
        while round < 3000 {
            round = round + 1
            a = Make(1000)
            mut b = a
            b[7] = 8
            p = Pair(Make(1000), 1)
            q = p with { B = 2 }
            for x in 0..<3 {
                c = Make(1000)
                if x == Seven(c) - 6 {
                    break
                }
            }
            for j in 0..<3 {
                c = Make(1000)
                if j == 1 {
                    continue
                }
                total = total + Seven(c)
            }
            Make(1000)
            Drop(1000)
            l = Make(1000)
            h = () => Seven(l)
            mut ms = List<Map<int, int>>()
            ms.Add(Make(1000))
            mut nested = Map<int, Map<int, int>>()
            nested[0] = Make(1000)
            nested[0] = Make(10)
            mut words = Map<string, string>()
            words["{round}"] = "{round}"
            words["a"] = "b"
            words["a"] = "{round}"
            total = total + Early(1000) + Seven(q.A) + Seven(b) - Seven(a) + h() + Seven(ms[0]) + Seven(nested[0] ?? Map<int, int>()) + (words["a"] ?? "").Bytes.Count - (words["{round}"] ?? "").Bytes.Count - 36
        }
        print(round)
        print(total)
        """;

    private static Compilation Compile(string text, CheckedRuntime runtime, params string[] grants) =>
        Builder.Compile(SourceText.FromString("program.kz", text), grants, runtime, testBuild: true);

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, CheckedRuntime runtime, string name, params string[] grants)
    {
        var compilation = Compile(text, runtime, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    /// <summary>The run ended with the system's signal before it printed: neither 0 nor the runtime's error exit 1, and no error line of the runtime.</summary>
    private static void Crashed((int Code, IReadOnlyList<string> Output, string Error) run)
    {
        Assert.True(run.Code is not 0 and not 1, $"exit {run.Code}");
        Assert.Empty(run.Output);
        Assert.DoesNotContain("error:", run.Error, StringComparison.Ordinal);
    }

    /// <summary>The runtime and the standard library of this machine's target with each file's text edited where <paramref name="edits"/> names its path; each text it replaces is there.</summary>
    internal static CheckedRuntime Edited(params (string Path, string Old, string New)[] edits)
    {
        var target = Builder.HostTarget!.Value;
        var files = Runtime.Files.Select(f =>
        {
            var text = Encoding.UTF8.GetString(f.Bytes);
            foreach (var (path, old, @new) in edits.Where(e => e.Path == f.Path))
            {
                Assert.Contains(old, text, StringComparison.Ordinal);
                text = text.Replace(old, @new, StringComparison.Ordinal);
            }

            return new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(text));
        }).ToList();
        Assert.All(edits, e => Assert.Contains(files, f => f.Path == e.Path));
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        return runtime;
    }

    private static string TargetFile => $"runtime/{Runtime.Directory(Builder.HostTarget!.Value)}/{Runtime.Directory(Builder.HostTarget!.Value)}.kz";

    /// <summary>The runtime of this machine's target whose system gives 16 pieces of memory and refuses every request after them.</summary>
    private static CheckedRuntime Budget() => Edited((TargetFile, "pub byte* Memory(long bytes) {", """
        thread static mut long pieces = 0

        pub byte* Memory(long bytes) {
            mut long taken = 0
            raw {
                pieces = pieces + 1
                taken = pieces
            }

            if taken > 16 {
                return null
            }

            return Granted(bytes)
        }

        byte* Granted(long bytes) {
        """));

    /// <summary>
    /// The runtime of this machine's target whose system refuses a request that would take what it gave and was not given back past
    /// 64 mebibytes; its own `Memory` and `Release` run behind the count.
    /// </summary>
    private static CheckedRuntime Bytes() => Edited(
        (TargetFile, "pub byte* Memory(long bytes) {", """
            thread static mut long live = 0

            pub byte* Memory(long bytes) {
                mut long total = 0
                raw {
                    live = live + bytes
                    total = live
                }

                if total > 67108864 {
                    return null
                }

                return Granted(bytes)
            }

            byte* Granted(long bytes) {
            """),
        (TargetFile, "pub void Release(byte* p, long bytes) {", """
            pub void Release(byte* p, long bytes) {
                raw {
                    live = live - bytes
                }

                Released(p, bytes)
            }

            void Released(byte* p, long bytes) {
            """));

    /// <summary>
    /// The edits that make the allocator take every block from pages of its own and make them unreachable when the block is freed, so
    /// that a read or a write of a freed block ends the process with the system's signal; nothing is used again.
    /// </summary>
    private static (string Path, string Old, string New)[] Fence => Builder.HostTarget == Target.Windows
        ?
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            (TargetFile, "VirtualFree(Ptr<void>(p), 0, 32768)", "VirtualFree(Ptr<void>(p), ulong(bytes), 16384)"), // MEM_DECOMMIT: the pages stay reserved
        ]
        :
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            (TargetFile, "munmap(Ptr<void>(p), bytes)", "mprotect(Ptr<void>(p), bytes, 0)"), // PROT_NONE: the pages stay mapped
        ];

    private static readonly Lazy<CheckedRuntime> FencedRuntime = new(() => Edited(Fence));

    /// <summary>The runtime of this machine's target with the allocator <see cref="Fence"/> makes, which the corpus runner builds every case with too.</summary>
    internal static CheckedRuntime Fenced => FencedRuntime.Value;

    [Fact]
    public void Lists_are_values_whose_storage_is_shared_until_one_side_writes()
    {
        Ran(BuildAndRun(Values, toolchain.Runtime, "list-values"), 0, ValuesPrint, "");
        Ran(BuildAndRun(Sharing, toolchain.Runtime, "list-sharing"), 0, SharingPrint, "");
        Ran(BuildAndRun(Strings, toolchain.Runtime, "string-values", "raw"), 0, StringsPrint, "");
        Ran(BuildAndRun(Maps, toolchain.Runtime, "map-values"), 0, MapsPrint, "");
    }

    [Fact]
    public void No_block_is_read_or_written_after_it_is_freed()
    {
        var fenced = Fenced;
        Ran(BuildAndRun(Values, fenced, "list-values-fenced"), 0, ValuesPrint, "");
        Ran(BuildAndRun(Sharing, fenced, "list-sharing-fenced"), 0, SharingPrint, "");
        Ran(BuildAndRun(Strings, fenced, "string-values-fenced", "raw"), 0, StringsPrint, "");
        Ran(BuildAndRun(Maps, fenced, "map-values-fenced"), 0, MapsPrint, "");
        // every way out of a block, as the budget's program takes them, in rounds few enough for a mapping per block
        Ran(BuildAndRun(Rounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "list-rounds-fenced"), 0, ["20", "80020"], "");
        Ran(BuildAndRun(StringRounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "string-rounds-fenced"), 0, ["20", "61460"], "");
        Ran(BuildAndRun(MapRounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "map-rounds-fenced"), 0, ["20", "280"], "");

        // the positive control: a write into a freed block under the same allocator ends the process
        var writer = Edited([.. Fence, ("runtime/common/runtime.kz", "pub void Start() {\n    sys.Start()\n", "pub void Start() {\n    sys.Start()\n    byte* p = Allocate(16)\n    raw {\n        std.Free(p, 16)\n        p[0] = 1\n    }\n")]);
        Crashed(BuildAndRun("print(1)\n", writer, "list-fence-control"));
    }

    [Fact]
    public void What_no_value_holds_is_freed_and_given_out_again()
    {
        Ran(BuildAndRun(Rounds, Budget(), "list-rounds"), 0, ["3000", "12003000"], "");
        Ran(BuildAndRun(StringRounds, Budget(), "string-rounds"), 0, ["3000", "9219000"], "");
        Ran(BuildAndRun(MapRounds, Budget(), "map-rounds"), 0, ["3000", "42000"], "");

        // the positive control: the same lists, all of them held, outgrow the same memory
        const string held = "mut all = List<List<int>>()\nmut round = 0\nwhile round < 3000 {\n    round = round + 1\n    mut xs = List<int>()\n    mut i = 0\n    while i < 1000 {\n        xs.Add(i)\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n";
        var run = BuildAndRun(held, Budget(), "list-rounds-held");
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);

        // and five of the rounds' strings of 1024 bytes held each round, a block of 1040 bytes in the size class of 2048: 30 megabytes
        const string strings = "string Make(int n) {\n    mut s = \"x\"\n    mut i = 0\n    while i < n {\n        s = \"{s}{s}\"\n        i = i + 1\n    }\n    return s\n}\n\nmut all = List<string>()\nmut round = 0\nwhile round < 3000 {\n    round = round + 1\n    all.Add(Make(10))\n    all.Add(Make(10))\n    all.Add(Make(10))\n    all.Add(Make(10))\n    all.Add(Make(10))\n}\nprint(all.Count)\n";
        var kept = BuildAndRun(strings, Budget(), "string-rounds-held");
        Assert.Equal(1, kept.Code);
        Assert.StartsWith("error: allocation-failed (", kept.Error, StringComparison.Ordinal);

        // and a map of 1000 entries held each round, a block of 32800 bytes in the size class of 65536: 196 megabytes
        const string maps = "Map<int, int> Make(int n) {\n    mut m = Map<int, int>()\n    for i in 0..<n {\n        m[i] = i\n    }\n    return m\n}\n\nmut all = List<Map<int, int>>()\nmut round = 0\nwhile round < 3000 {\n    round = round + 1\n    all.Add(Make(1000))\n}\nprint(all.Count)\n";
        var full = BuildAndRun(maps, Budget(), "map-rounds-held");
        Assert.Equal(1, full.Code);
        Assert.StartsWith("error: allocation-failed (", full.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_of_more_than_262144_bytes_goes_back_to_the_system()
    {
        // the target's own `Release`: a write into such a block after its `Free` ends the process with the system's signal
        const string start = "pub void Start() {\n    sys.Start()\n";
        const string write = "    byte* p = Allocate(300000)\n    raw {\n        p[0] = 1\n        std.Free(p, 300000)\n        p[0] = 2\n    }\n";
        Crashed(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + write)), "list-large-freed"));
        var kept = Edited(("runtime/common/runtime.kz", start, start + write.Replace("        std.Free(p, 300000)\n", "", StringComparison.Ordinal)));
        Ran(BuildAndRun("print(1)\n", kept, "list-large-kept"), 0, ["1"], ""); // the positive control: the block not freed

        // the lists of 70000 items grow through blocks of 262160 and 524304 bytes, 150 mebibytes in 200 rounds were none of them given back
        const string rounds = "long Fill(int n) {\n    mut xs = List<int>()\n    mut i = 0\n    while i < n {\n        xs.Add(i)\n        i = i + 1\n    }\n    return long(xs.Count)\n}\n\nmut round = 0\nmut long total = 0\nwhile round < 200 {\n    round = round + 1\n    total = total + Fill(70000)\n}\nprint(total)\n";
        Ran(BuildAndRun(rounds, Bytes(), "list-large-rounds"), 0, ["14000000"], "");
        const string held = "mut all = List<List<int>>()\nmut round = 0\nwhile round < 200 {\n    round = round + 1\n    mut xs = List<int>()\n    mut i = 0\n    while i < 70000 {\n        xs.Add(i)\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n";
        var run = BuildAndRun(held, Bytes(), "list-large-held"); // the positive control: the same lists, all of them held
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);

        // strings of 524288 bytes joined from halves of 262144, blocks of 524304 and 262160 bytes with their counts: 150 mebibytes in
        // 200 rounds were none of them given back
        const string fill = "string Fill(int n) {\n    mut s = \"x\"\n    mut i = 0\n    while i < n {\n        s = \"{s}{s}\"\n        i = i + 1\n    }\n    return s\n}\n\n";
        Ran(BuildAndRun(fill + "mut round = 0\nmut long total = 0\nwhile round < 200 {\n    round = round + 1\n    total = total + long(Fill(19).Bytes.Count)\n}\nprint(total)\n", Bytes(), "string-large-rounds"), 0, ["104857600"], "");
        var strings = BuildAndRun(fill + "mut all = List<string>()\nmut round = 0\nwhile round < 200 {\n    round = round + 1\n    all.Add(Fill(19))\n}\nprint(all.Count)\n", Bytes(), "string-large-held");
        Assert.Equal(1, strings.Code); // the positive control: the same strings, all of them held
        Assert.StartsWith("error: allocation-failed (", strings.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("long", "1", 32)]
    [InlineData("byte", "byte(1)", 4)]
    [InlineData("string", "\"a\"", 64)] // its bytes' address and their count
    [InlineData("List<int>", "List<int>()", 32)] // the address of its block
    [InlineData("Padded", "Padded(1, byte(2))", 64)] // 9 bytes of fields, padded to the `long`'s alignment
    [InlineData("Tree", "Tree(1, Leaf)", 32)] // a block: the list holds its address
    public void An_Add_past_the_limit_reports_the_bytes_of_the_items_after_it(string type, string item, int bytes)
    {
        // the owner, 2026-10-24, "Items after the Add": the limit of the largest `int` lowered to 3 in the library's text, so that a
        // fourth `Add` asks for 4 times the bytes one item takes in the list
        var add = $"xs.Add({item})\n";
        var text = $"data Padded(long A, byte B)\ndata Leaf\ndata Tree(int V, Tree | Leaf Next)\n\nmut xs = List<{type}>()\n{add}{add}{add}{add}print(xs.Count)\n";
        var name = new string(type.Where(char.IsLetter).ToArray());
        Ran(BuildAndRun(text, Edited(("std/std.kz", "2147483647", "3")), "list-limit-" + name), 1, [], $"error: allocation-failed ({bytes} bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-below-limit-" + name), 0, ["4"], ""); // the positive control
    }

    [Fact]
    public void A_string_asked_of_more_bytes_than_the_limit_ends_with_the_count_it_asked_for_and_a_refused_one_with_its_block()
    {
        // T15: the limit of the largest `int` lowered to 3 in the runtime's text, so that `string.Allocate(4)` asks past it; the
        // bytes of a refused block are those the allocator was asked for, the 16 of its counts among them, as for a list's growth
        const string text = "raw {\n    p = string.Allocate(3)\n    print(p != null)\n    q = string.Allocate(4)\n    print(q != null)\n}\n";
        Ran(BuildAndRun(text, Edited(("runtime/common/runtime.kz", "    if size > 2147483647 {\n        AllocationFailed(size)", "    if size > 3 {\n        AllocationFailed(size)")), "string-limit", "raw"), 1, ["true"], "error: allocation-failed (4 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "string-below-limit", "raw"), 0, ["true", "true"], ""); // the positive control
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "string-refused", "raw"), 1, [], "error: allocation-failed (19 bytes)\n");
    }

    [Theory]
    [InlineData("-1", "minus")]
    [InlineData("2", "count")]
    [InlineData("100000", "far")]
    [InlineData("-2147483648", "smallest")]
    public void An_index_below_0_or_at_Count_or_beyond_raises_index_out_of_range_at_its_line(string index, string name)
    {
        // T14, M9: the read before it is the positive control, an index inside the list
        var text = $"mut xs = List<int>()\nxs.Add(4)\nxs.Add(5)\nprint(xs[1])\nprint(xs[{index}])\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-index-" + name), 1, ["5"], "error: index-out-of-range at program.kz:5\n");
    }

    [Theory]
    [InlineData("-1", "1", false, "index-out-of-range", "minus")]
    [InlineData("0", "4", false, "index-out-of-range", "past")]
    [InlineData("0", "3", true, "index-out-of-range", "past-last")]
    [InlineData("4", "4", false, "index-out-of-range", "start-past")]
    [InlineData("2", "1", false, "reversed-range-at-run-time", "reversed")]
    [InlineData("5", "1", false, "reversed-range-at-run-time", "reversed-past")] // the order before the count, as C8 checks it first
    public void A_part_outside_the_list_or_with_its_ends_reversed_raises_at_its_line(string start, string end, bool inclusive, string id, string name)
    {
        // M9 (section 4, the owner, 2026-10-29, "A List<T>, a value"): the part before it is the positive control
        var text = $"mut xs = List<int>()\nxs.Add(1)\nxs.Add(2)\nxs.Add(3)\na = {start}\nb = {end}\nprint(xs[1..<3])\nprint(xs[a..{(inclusive ? "" : "<")}b])\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-part-" + name), 1, ["[2, 3]"], $"error: {id} at program.kz:8\n");
    }

    [Fact]
    public void A_part_reaches_both_ends_of_the_list_with_ends_of_any_integer_type()
    {
        // M9: a part that ends at the count, an empty one there, one item of `a..b`, and ends of `uint` and of `long`
        const string text = "mut xs = List<int>()\nxs.Add(1)\nxs.Add(2)\nxs.Add(3)\nprint(xs[0..<3])\nprint(xs[3..<3])\nprint(xs[2..2])\nuint a = 1\nuint b = 3\nprint(xs[a..<b])\nlong c = 0\nprint(xs[c..c])\nprint(List<int>()[0..<0])\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-part-ends"), 0, ["[1, 2, 3]", "[]", "[3]", "[2, 3]", "[1]", "[]"], "");
    }

    [Fact]
    public void FromUtf8_reads_bytes_as_Rust_s_from_utf8_and_from_utf8_lossy_do()
    {
        // T15 (section 4, the owner, 2026-10-29, "Union, as Rust"): for each sequence, what Rust 1.82's `str::from_utf8` gave on
        // 2026-10-29, `ok` or the error's `valid_up_to`, and the bytes of its `String::from_utf8_lossy`: overlong forms, surrogates, code
        // points past U+10FFFF, bytes that begin no sequence, and sequences cut short inside the bytes and at their end
        (byte[] Bytes, string Valid, string Lossy)[] cases =
        [
            ([], "ok", "[]"),
            ([0x41], "ok", "[65]"),
            ([0x80], "0", "[239, 191, 189]"),
            ([0xC2, 0x80], "ok", "[194, 128]"),
            ([0xC1, 0xBF], "0", "[239, 191, 189, 239, 191, 189]"),
            ([0xE0, 0x80, 0x80], "0", "[239, 191, 189, 239, 191, 189, 239, 191, 189]"),
            ([0xE0, 0xA0, 0x80], "ok", "[224, 160, 128]"),
            ([0xED, 0xA0, 0x80], "0", "[239, 191, 189, 239, 191, 189, 239, 191, 189]"),
            ([0xED, 0x9F, 0xBF], "ok", "[237, 159, 191]"),
            ([0xEF, 0xBF, 0xBF], "ok", "[239, 191, 191]"),
            ([0xF0, 0x8F, 0xBF, 0xBF], "0", "[239, 191, 189, 239, 191, 189, 239, 191, 189, 239, 191, 189]"),
            ([0xF0, 0x90, 0x80, 0x80], "ok", "[240, 144, 128, 128]"),
            ([0xF4, 0x8F, 0xBF, 0xBF], "ok", "[244, 143, 191, 191]"),
            ([0xF4, 0x90, 0x80, 0x80], "0", "[239, 191, 189, 239, 191, 189, 239, 191, 189, 239, 191, 189]"),
            ([0xF5, 0x80, 0x80, 0x80], "0", "[239, 191, 189, 239, 191, 189, 239, 191, 189, 239, 191, 189]"),
            ([0xFF], "0", "[239, 191, 189]"),
            ([0xC2], "0", "[239, 191, 189]"),
            ([0xE2, 0x82], "0", "[239, 191, 189]"),
            ([0xF0, 0x90, 0x80], "0", "[239, 191, 189]"),
            ([0x61, 0xE2, 0x82, 0x61], "1", "[97, 239, 191, 189, 97]"),
            ([0xF0, 0x90, 0x80, 0x41], "0", "[239, 191, 189, 65]"),
            ([0x68, 0x69, 0xC3, 0x28, 0x21], "2", "[104, 105, 239, 191, 189, 40, 33]"),
            ([0xE2, 0x82, 0xAC, 0xC3, 0xA4, 0x80, 0xBF, 0x7A], "5", "[226, 130, 172, 195, 164, 239, 191, 189, 239, 191, 189, 122]"),
        ];
        var program = new StringBuilder("void Show(Bytes b) {\n    match string.FromUtf8(b) {\n        InvalidUtf8 e => print(e.At)\n        else => print(\"ok\")\n    }\n    print(string.FromUtf8Lossy(b).Bytes)\n}\n\n");
        for (var i = 0; i < cases.Length; i++)
        {
            program.Append(cases[i].Bytes.Length == 0 ? $"c{i} = Bytes()\n" : $"mut c{i} = Bytes()\n");
            foreach (var b in cases[i].Bytes)
            {
                program.Append($"c{i}.Add({b})\n");
            }

            program.Append($"Show(c{i})\n");
        }

        string[] lines = [.. cases.SelectMany(c => new[] { c.Valid, c.Lossy })];
        Ran(BuildAndRun(program.ToString(), toolchain.Runtime, "utf8-table"), 0, lines, "");
        Ran(BuildAndRun(program.ToString(), Fenced, "utf8-table-fenced"), 0, lines, "");
    }

    [Fact]
    public void A_list_takes_over_the_room_Allocate_gave_and_shows_its_memory_in_raw()
    {
        // R15 (section 3, the owner, 2026-10-29, "List.Allocate, take over"): the list holds the room `Allocate` gave and grows from it;
        // the pointer of a list without a block is not null
        const string text = "raw {\n    p = List<long>.Allocate(4)\n    p[0] = 7\n    p[1] = 8\n    mut xs = List<long>.FromRaw(p, 2)\n    xs.Add(9)\n    print(xs)\n    print(xs.Pointer == p)\n    xs.Add(10)\n    xs.Add(11)\n    print(xs)\n    empty = List<int>()\n    print(empty.Pointer != null)\n}\n";
        string[] lines = ["[7, 8, 9]", "true", "[7, 8, 9, 10, 11]", "true"];
        Ran(BuildAndRun(text, toolchain.Runtime, "list-from-raw", "raw"), 0, lines, "");
        Ran(BuildAndRun(text, Fenced, "list-from-raw-fenced", "raw"), 0, lines, "");
    }

    [Fact]
    public void A_list_asked_of_more_items_than_the_limit_ends_with_their_bytes_and_a_refused_one_with_its_block()
    {
        // M9, R15: the limit of the largest `int` lowered to 3 in the library's text, so that `List<long>.Allocate(4)` asks past it with
        // the bytes of 4 items of 8; a refused block reports the bytes the allocator was asked for, the 16 of its counts among them
        const string text = "raw {\n    p = List<long>.Allocate(3)\n    print(p != null)\n    q = List<long>.Allocate(4)\n    print(q != null)\n}\n";
        Ran(BuildAndRun(text, Edited(("std/std.kz", "if count > 2147483647 {\n        asked = count * item", "if count > 3 {\n        asked = count * item")), "list-allocate-limit", "raw"), 1, ["true"], "error: allocation-failed (32 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-allocate-below-limit", "raw"), 0, ["true", "true"], ""); // the positive control
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "list-allocate-refused", "raw"), 1, [], "error: allocation-failed (40 bytes)\n");

        // a count past the largest `int` handed to `FromRaw`, the bytes of its items, and the bytes of a literal the system refuses
        Ran(BuildAndRun("raw {\n    p = List<long>.Allocate(1)\n    xs = List<long>.FromRaw(p, 2147483648)\n    print(xs.Count)\n}\n", toolchain.Runtime, "list-from-raw-limit", "raw"), 1, [], "error: allocation-failed (17179869184 bytes)\n");
        Ran(BuildAndRun("b = \"abc\".Bytes\nprint(b)\n", EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "bytes-refused"), 1, [], "error: allocation-failed (19 bytes)\n");
        Ran(BuildAndRun("b = \"abc\".Bytes\nprint(b)\n", toolchain.Runtime, "bytes-granted"), 0, ["[97, 98, 99]"], ""); // the positive control
    }

    [Fact]
    public void A_growth_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // the first block of a list of `long`: 16 bytes of counts and room for 4 items, as Rust's `Vec` takes 4 of up to a kibibyte
        const string text = "mut xs = List<long>()\nxs.Add(1)\nprint(xs.Count)\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "list-refused"), 1, [], "error: allocation-failed (48 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-granted"), 0, ["1"], ""); // the positive control
    }

    [Fact]
    public void A_map_block_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // the first block of a `Map<long, long>`: 32 bytes of counts and keys and 8 entries of a hash, a key and a value
        const string text = "mut m = Map<long, long>()\nm[1] = 1\nprint(m[1] ?? 0)\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "map-refused"), 1, [], "error: allocation-failed (224 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "map-granted"), 0, ["1"], ""); // the positive control
    }

    [Fact]
    public void A_key_is_hashed_by_SipHash_1_3_under_keys_the_system_gives_each_process()
    {
        // the hash Rust's `HashMap` takes by default: the two vectors under keys of 0 are CPython's `siphash13` of the same bytes, the
        // third is under the reference implementation's keys, from the same code in Python, which gives the reference's SipHash-2-4
        // vectors with two and four rounds
        const string start = "pub void Start() {\n    sys.Start()\n";
        const string hash = "    byte* p = Allocate(15)\n    raw {\n        p[0] = 97\n        p[1] = 98\n        p[2] = 99\n    }\n    WriteNumber(std.SipHash13(0, 0, p, 3))\n    WriteError(\"\\n\")\n    raw {\n        for i in 0..<15 {\n            p[i] = byte(i)\n        }\n    }\n    WriteNumber(std.SipHash13(0, 0, p, 15))\n    WriteError(\"\\n\")\n    WriteNumber(std.SipHash13(506097522914230528UL, 1084818905618843912UL, p, 15))\n    WriteError(\"\\n\")\n";
        var runtime = Edited(("std/std.kz", "\nulong SipHash13(", "\npub ulong SipHash13("), ("runtime/common/runtime.kz", start, start + hash));
        Ran(BuildAndRun("print(1)\n", runtime, "map-siphash"), 0, ["1"], "13851880170939887858\n17514137373579004394\n15213397504630561110\n");

        // the keys differ between two runs of one program, so the order of a map's 20 entries does too (M10), as in Rust
        const string text = "mut m = Map<int, int>()\nfor i in 0..<20 {\n    m[i] = i\n}\nprint(m)\n";
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var program = toolchain.Build(compilation.Program, "map-order");
        var first = Toolchain.Run(program);
        var second = Toolchain.Run(program);
        Assert.Equal((0, ""), (first.Code, first.Error));
        Assert.Equal((0, ""), (second.Code, second.Error));
        Assert.NotEqual(first.Output, second.Output);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"{i}: {i}").Order(), Assert.Single(first.Output).Trim('[', ']').Split(", ").Order());
    }

    [Theory]
    [InlineData("mut xs = List<int>()\nxs.Add(1)\nxs[0] = 2\n", 3, "an assignment into an index (M9)", null)]
    [InlineData("data Box(List<int> Items)\n\nmut b = Box(List<int>())\nb.Items.Add(1)\n", 4, "an `Add` on a list in a field (M5)", null)]
    [InlineData("data Box(List<int> Items)\n\na = Box(List<int>())\nb = Box(List<int>())\nprint(a == b)\n", 5, "`==` on a `List<int>`", null)] // D3 compares the fields
    [InlineData("s = \"ab\"\nprint(s.Chars[0])\n", 2, "`s.Chars` as a list (T15)", null)]
    [InlineData("data Box(List<int> Items)\n\nvoid Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nmut b = Box(List<int>())\nFill(mut b.Items)\n", 8, "a `mut` parameter given anything but a variable (M7)", null)]
    public void What_the_emitter_does_not_lay_out_of_a_list_is_not_supported_at_its_line(string text, int line, string message, string? grant)
    {
        var compilation = Compile(text, toolchain.Runtime, grant is null ? [] : [grant]);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((line, message), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void Where_calls_its_test_once_for_each_item_in_order()
    {
        // M9: `Where` calls its test once for each item, in order (section 4, the owner, 2026-10-28, "Once each, in order"), as Rust's
        // `filter` and `collect` run it
        const string text = "mut xs = List<int>()\nxs.Add(3)\nxs.Add(1)\nxs.Add(2)\nkept = xs.Where(x => {\n    print(x)\n    return x > 1\n})\nprint(kept)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-where-order"), 0, ["3", "1", "2", "[3, 2]"], "");
    }

    [Fact]
    public void An_environment_the_system_refuses_reports_its_bytes()
    {
        // the count, the address of the function that lets go of it, and the `int` the lambda read, padded to the count's alignment
        const string text = "n = 1\nf = () => n\nprint(f())\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "lambda-refused"), 1, [], "error: allocation-failed (24 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "lambda-granted"), 0, ["1"], ""); // the positive control
    }

    [Fact]
    public void What_Add_s_argument_made_besides_the_item_is_let_go_of_before_the_list_grows()
    {
        // the lambda given to `Count` holds a count of the list (F15) until its environment is let go of: before the `Add`, so that
        // the list is not seen as shared and copied at each `Add`
        var ir = Compile("int Count(() => int f) => f()\n\nmut xs = List<int>()\nxs.Add(Count(() => xs.Count))\nprint(xs)\n", toolchain.Runtime).Program!.Ir;
        var main = ir[ir.IndexOf("define internal void @kz.main()", StringComparison.Ordinal)..];
        main = main[..main.IndexOf("\n}", StringComparison.Ordinal)];
        var release = main.IndexOf("call void @\"release.fn\"", StringComparison.Ordinal);
        Assert.True(release >= 0, "the environment is let go of in the top-level code");
        Assert.True(release < main.IndexOf("call void @\"add.List<int>\"", StringComparison.Ordinal), "the environment is let go of after the `Add`");
    }

    [Fact]
    public void A_mut_parameter_of_a_lambda_is_not_supported_at_its_line()
    {
        // a function type passes no `mut` (F11), so no caller's variable sees what such a parameter is given (M7)
        var compilation = Compile("void Run((int) => int f) {\n    print(f(1))\n}\n\nRun((mut int x) => x)\n", toolchain.Runtime);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((5, "a `mut` parameter of a lambda (M7)"), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
        Assert.NotNull(Compile("void Run((int) => int f) {\n    print(f(1))\n}\n\nRun((int x) => x)\n", toolchain.Runtime).Program); // the positive control
    }
}
