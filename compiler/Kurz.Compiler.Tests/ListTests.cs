using System.Globalization;
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
/// reports past the limit and for a refused growth, an integer's digits in every text that holds them, against what .NET writes (A2),
/// and what the emitter says `not supported` to; what the checker refuses of a
/// read beside a `mut` call (M7) the corpus pins.
/// </summary>
public sealed class ListTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    // nested lists, a list in a data value and in `with`, a path assignment, a list of strings, a loop's `continue`, a list in a
    // value laid out as a block, the texts, and list literals (M11) whose items an item after them changes the variables of or that
    // leave their function after a counted item
    private const string Values = """
        data Cart(string Owner, List<int> Items)
        data Leaf
        data Tree(List<int> Items, Tree | Leaf Next)
        data TooBig

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

        List<int> Grow(mut List<int> list) {
            list.Add(5)
            return list
        }

        string Relabel(mut string s) {
            s = "m{s}"
            return s
        }

        List<int> | TooBig Checked(int n) {
            if n > 3 {
                return TooBig
            }
            return Range(n)
        }

        List<List<int>> | TooBig Pair(int n) => [Range(n), Checked(n)]

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
        mut held = Range(2)
        print([held, Grow(mut held), held])
        mut label = "n{Sum(Range(3))}"
        print([label, Relabel(mut label)])
        print(Pair(2) else { TooBig => List<List<int>>() })
        print(Pair(5) else { TooBig => List<List<int>>() })
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
        "[[0, 1], [0, 1, 5], [0, 1, 5]]", // the first item keeps the list it was read with, counted as it was made (M11)
        "[n3, mn3]",
        "[[0, 1], [0, 1]]",
        "[]", // the second item left `Pair`, which let go of the first (O3)
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
    // parameters of a call, an interpolation, one of several texts in one block that a variable holds past its statement, one of no
    // bytes, the empty string with no block, and a hole of it, integers whose digits the block takes in place, the text of a value
    // laid out as a block, literals, a string made in a `raw` block,
    // an interpolation of one hole beside a call that changes the place the hole read, the values `??` and `?.` give, a read of a
    // nullable variable known not null, an `Add` through the place of one, the item `?[` reads, a list without items in a nullable
    // variable, a `match` with a `null` arm on one whose arm changes it, a nullable parameter read in the arm of its case and after
    // a `match` whose `null` arm leaves, and a call's whole result, a string read from a list, on a parameter of a written union (O4),
    // which the callee adds to a list that outlives the call and the source list's next value
    private const string Strings = """
        data Person(string Name, int Age)
        data Tag(string Label)
        data Leaf
        data Node(string Label, Node | Leaf Next)
        data Contact(string? Email)
        data Reading(long Low, ulong High, List<int> Xs, double D)

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

        string | Leaf Pick(List<string> xs, int i) {
            if i < xs.Count {
                return xs[i]
            }
            return Leaf
        }

        void Kept(string | Leaf x, mut List<string | Leaf> into) {
            into.Add(x)
            print(x)
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
        joined = "<{Made(40)}|{e}|{Made(41)}>"
        blank = "{e}{e}"
        print("{joined}{blank}[{blank}]")
        raw {
            print(blank.Pointer == "".Pointer)
        }
        long smallest = -9223372036854775807 - 1
        ulong largest = 18446744073709551615
        print("{smallest}|{largest}|{byte(255)}|{-7}|{0}:{Made(42)}")
        print(Reading(smallest, largest, [-1, 22], 2.5))
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
        mut List<string>? labels = List<string>()
        labels.Add(Made(36))
        first = labels?[0] ?? "none"
        labels = null
        print(first)
        print(labels?[0] ?? "none")
        mut List<string>? empty = List<string>()
        print(empty == null)
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
        print(Made(37) < Same(Made(38)))
        mut pile = List<string | Leaf>()
        mut sources = List<string>()
        sources.Add(Made(39))
        Kept(Pick(sources, 0), mut pile)
        Kept(Pick(sources, 1), mut pile)
        sources = List<string>()
        print(pile)
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
        "<n40||n41>[]", // texts in one block, which the variable takes over from the statement, and the empty string as a hole
        "true", // pieces of no bytes together are the empty string, the constant of no bytes, with no block
        "-9223372036854775808|18446744073709551615|255|-7|0:n42", // digits written in the block, a `-` before a negative number's
        "Reading { Low = -9223372036854775808, High = 18446744073709551615, Xs = [-1, 22], D = 2.5 }", // a value's text in one block
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
        "n36", // the item `?[` reads, which the variable's next value leaves whole
        "none",
        "false", // a list without items, the address 0, beside the `null` of a nullable list, the address 1
        "n34", // the name of a `null` arm's `match` on a variable known not null, which the arm's assignment leaves whole
        "n35", // a parameter read in the arm of its case
        "n35", // and after a `match` whose `null` arm leaves
        "none",
        "Contact { Email = null }",
        "hi", // a list that took over the room `Bytes.Allocate` gave
        "105",
        "true", // two strings calls made, ordered by their bytes (T32) and let go of after the statement
        "n39", // a call's whole result on a parameter of a written union (O4), let go of after the call
        "Leaf",
        "[n39, Leaf]", // and the count the callee's list took of it, past the source list's next value
    ];

    // every way out of a block a string is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, an assignment over a variable, a lambda's environment in a
    // variable and in a list, a list of strings, an interpolation of one hole of 4096 bytes, the bytes of such a string, a string of
    // them, which shares their block, and a part of them; each other string of 1024 bytes, joined from halves the join lets go of
    private const string StringRounds = """
        data Pair(string A, int B)
        data Wrap(Pair P, List<int> Xs)

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

        mut big = List<int>()
        for n in 0..<1000 {
            big.Add(n)
        }

        mut round = 0
        mut total = 0
        while round < 3000 {
            round = round + 1
            a = Make(10)
            w = Wrap(Pair("a", 1), big)
            wrapped = "{w}"
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
            total = total + Early(10) + q.A.Bytes.Count + b.Bytes.Count - a.Bytes.Count + hs[0]() + hs[1]() + ss[0].Bytes.Count - 4096 + lone.Bytes.Count - 4096 + bb.Count - 4096 + tt.Bytes.Count - 4096 + pp.Count - 4000 + wrapped.Bytes.Count - 4931
        }
        print(round)
        print(total)
        """;

    // maps (M9): a read that is a value of its own, a write over a key, a copy written to (M3), a value read from the map it is
    // written into, a key the map keeps after its variable changes, a `mut` parameter, a missing key, growth, a map in a data value
    // and in `with`, a map of lists, a list of maps, a map of maps, a lambda's copy, integer, `bool`, `char` and `string` keys (the
    // other kinds in `MapMembers`), a map in a union, a block replaced by growth under a `mut` parameter and under a lambda that read
    // it, strings equal by their bytes with and without a block, and the texts
    private const string Maps = """
        data Box(string Name, Map<string, string> Tags)

        enum Plan { Free, Pro }

        T Id<T>(T x) => x

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

        // a map through a generic function, a map of enum values, `Where` over maps, and a read of a call's map
        same = Id(words)
        words["a"] = "again"
        print(same["a"] ?? "none")
        mut plans = Map<string, Plan>()
        plans["ann"] = Plan.Pro
        print(plans)
        mut every = List<Map<string, int>>()
        every.Add(Plus(base))
        every.Add(Map<string, int>())
        print(every.Where(x => (x["z"] ?? 0) > 0))
        print(Plus(base)["z"] ?? 0)
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
        "changed", // a map through a generic function
        "[ann: Pro]",
        "[[z: 26]]",
        "26",
    ];

    // a map's members, its loops, its keys and its nullable values (M9, C6, M10; section 4, the owner, 2026-10-30): `Count`,
    // `ContainsKey`, `Remove` of an entry whose value a variable holds and of one a copy holds, a `Remove` whose result nothing holds,
    // removals and writes over many rounds, whose slots are taken again, `Clear` of a block the map alone holds and of one a copy
    // holds, loops with one name and two, a `break` and a `continue`, a write, a `Remove` and a `Clear` inside a loop over the same map,
    // the names read after them, keys of enums, `data` values, lists, a type that holds itself and a time, and values of a union and
    // function values; the texts print one entry, since the order of several differs between runs (M10)
    private const string MapMembers = """
        data P(int X, string Name)
        data Tag(string? Note, List<int> Ids)
        data Tree(int V, List<Tree> Kids)
        enum Tier { Free, Pro }
        data Pt(double X)
        data Samples(List<double> Xs)
        data Maybes(List<double?> Xs)

        void ShowEntry(Entry<string, List<int>> entry) {
            print(entry.Value.Count)
        }

        T? ValueAt<T>(Map<string, T> src, string k) => src[k]

        int Twice<T>(T x) {
            mut tm = Map<string, T>()
            tm["a"] = x
            tm["b"] = x
            tm.Remove("a")
            return tm.Count
        }

        string Made(int n) => "made {n}"

        int Sum(Map<int, int> sm) {
            mut acc = 0
            for a, b in sm {
                acc = acc + a * 1000 + b
            }
            return acc
        }

        mut m = Map<string, string>()
        print(m.Count)
        print(m.ContainsKey("a"))
        print(m.Remove("a") ?? "none")
        for i in 0..<20 {
            m[Made(i)] = Made(i + 100)
        }
        print(m.Count)
        print(m.ContainsKey(Made(3)))
        held = m[Made(3)] ?? "none"
        removed = m.Remove(Made(3)) ?? "none"
        print(m.Count)
        print(m.ContainsKey(Made(3)))
        print(m[Made(3)] ?? "none")
        print(m.Remove(Made(3)) ?? "none")
        m[Made(3)] = "back"
        print(m[Made(3)] ?? "none")
        print(m.Count)

        // a `Remove` from a map a copy holds: the copy keeps its entry (M3)
        copy = m
        m.Remove(Made(4))
        print(copy[Made(4)] ?? "none")
        print(m[Made(4)] ?? "none")
        print(copy.Count)
        print(m.Count)
        m.Clear()
        print(removed)
        print(held)
        made = Made(5)
        m[made] = made
        print(m.Remove(m[made] ?? "") ?? "none")
        print(m.Count)

        // removals and writes, round after round
        mut ints = Map<int, int>()
        for round in 0..<50 {
            for i in 0..<100 {
                ints[i] = round
            }
            for i in 0..<100 {
                if i % 3 != 0 {
                    ints.Remove(i)
                }
            }
        }
        print(ints.Count)
        mut found = 0
        for i in 0..<100 {
            if ints.ContainsKey(i) {
                found = found + 1
            }
        }
        print(found)
        print(ints[99] ?? -1)
        print(ints[98] ?? -1)
        mut left = 0
        for k, v in ints {
            left = left + 1 + v - 49
        }
        print(left)

        // `Clear` of a block a copy holds and of one the map alone holds
        mut c = Map<string, List<int>>()
        mut l = List<int>()
        l.Add(1)
        c["a"] = l
        c["b"] = l
        other = c
        c.Clear()
        print(c.Count)
        print(other.Count)
        c["x"] = l
        print(c)
        mut own = Map<int, string>()
        own[1] = Made(1)
        own.Clear()
        print(own)
        own[2] = Made(2)
        print(own)
        mut none = Map<int, int>()
        none.Clear()
        print(none.Count)

        // loops, with one name and with two
        mut sums = Map<int, int>()
        for i in 0..<10 {
            sums[i] = i * 2
        }
        print(Sum(sums))
        mut total = 0
        for e in sums {
            total = total + e.Key + e.Value
        }
        print(total)
        for k, v in Map<int, int>() {
            print("never")
        }
        mut reached = 0
        for k, v in sums {
            if k == 3 {
                continue
            }
            reached = reached + 1
        }
        print(reached)
        mut seen = 0
        for k, v in sums {
            if k == 3 {
                continue
            }
            if seen == 5 {
                break
            }
            seen = seen + 1
        }
        print(seen)

        // the loop walks the map as it was when it started (C6): a `Remove`, a write and a `Clear` inside it
        mut walked = 0
        for k, v in sums {
            sums.Remove(k)
            sums[k + 100] = v
            walked = walked + 1
        }
        print(walked)
        print(sums.Count)
        print(sums.ContainsKey(105))
        mut cleared = 0
        for e in sums {
            sums.Clear()
            cleared = cleared + e.Key + e.Value
        }
        print(cleared)
        print(sums.Count)
        mut words = Map<string, string>()
        for i in 0..<10 {
            words[Made(i)] = Made(i * 2)
        }
        mut bytes = 0
        for k, v in words {
            words.Remove(k)
            words.Clear()
            bytes = bytes + k.Bytes.Count + v.Bytes.Count
        }
        print(bytes)

        // keys: an enum, `data` values equal by content, lists equal by their items, a type that holds itself, a time
        mut tiers = Map<Tier, int>()
        tiers[Tier.Pro] = 2
        tiers[Tier.Free] = 1
        print(tiers[Tier.Pro] ?? 0)
        mut points = Map<P, string>()
        points[P(1, Made(1))] = "one"
        points[P(2, "b")] = "two"
        print(points[P(1, "made 1")] ?? "none")
        print(points[P(1, "made 2")] ?? "none")
        points[P(1, Made(1))] = "uno"
        print(points.Count)
        print(points[P(1, "made 1")] ?? "none")
        mut lists = Map<List<int>, int>()
        mut k1 = List<int>()
        k1.Add(1)
        k1.Add(2)
        lists[k1] = 12
        mut k2 = List<int>()
        k2.Add(1)
        k2.Add(2)
        print(lists[k2] ?? 0)
        k2.Add(3)
        print(lists[k2] ?? 0)
        print(lists[k2[0..<2]] ?? 0)
        lists[List<int>()] = 5
        print(lists[List<int>()] ?? 0)
        k1.Add(9)
        print(lists[k1] ?? 0)
        print(lists[k2[0..<2]] ?? 0)
        mut tags = Map<Tag, int>()
        tags[Tag(null, k1)] = 1
        tags[Tag("n", k1)] = 2
        print(tags[Tag(null, k1)] ?? 0)
        print(tags[Tag("n", k1)] ?? 0)
        print(tags[Tag("m", k1)] ?? 0)
        mut trees = Map<Tree, int>()
        mut kids = List<Tree>()
        kids.Add(Tree(2, List<Tree>()))
        trees[Tree(1, kids)] = 7
        print(trees[Tree(1, kids)] ?? 0)
        print(trees[Tree(1, List<Tree>())] ?? 0)
        mut times = Map<duration, int>()
        times[90min] = 1
        print(times[90min] ?? 0)

        // nullable values: a missing entry and a null one read alike
        mut maybe = Map<string, int?>()
        maybe["a"] = null
        maybe["b"] = 2
        print(maybe.ContainsKey("a"))
        print(maybe["a"] ?? -1)
        print(maybe["c"] ?? -1)
        print(maybe["b"] ?? -1)
        print(maybe.Remove("b") ?? -1)
        print(maybe.Remove("a") ?? -1)
        print(maybe.Count)
        mut notes = Map<int, string?>()
        notes[1] = Made(1)
        notes[2] = null
        print(notes[1] ?? "null")
        print(notes[2] ?? "null")
        print(notes.Remove(1) ?? "null")
        print(notes)

        // `double` and `float` keys, alone and in a field, as C#'s `double.Equals`: NaN finds NaN, a NaN of other bits among them, and
        // -0.0 finds 0.0
        double z = 0.0
        nan = z / z
        mut reals = Map<double, string>()
        reals[nan] = "nan"
        reals[z] = "zero"
        reals[-z] = "minus zero"
        print(reals.Count)
        print(reals[nan] ?? "none")
        print(reals[-nan] ?? "none")
        print(reals[z] ?? "none")
        print(reals.ContainsKey(-z))
        mut floats = Map<float, int>()
        floats[float(nan)] = 1
        floats[float(-z)] = 2
        print(floats[float(z)] ?? 0)
        print(floats[float(nan)] ?? 0)
        mut pts = Map<Pt, int>()
        pts[Pt(nan)] = 1
        pts[Pt(-z)] = 2
        print(pts[Pt(nan)] ?? 0)
        print(pts[Pt(z)] ?? 0)
        print(pts.Count)

        // a list of `double` or `float` items in a key and in a `data` value, as C#'s `Equals`: NaN equal to NaN, a NaN of other bits
        // among them, and -0.0 to 0.0, so the key is found again and the value equals itself, while two lists alone compare by IEEE's
        // `==` (section 4, the owner, 2026-10-30, "Equals inside data/keys")
        mut ws = List<double>()
        ws.Add(nan)
        ws.Add(z)
        mut vs = List<double>()
        vs.Add(-nan)
        vs.Add(-z)
        mut byList = Map<List<double>, string>()
        byList[ws] = "found"
        print(byList[vs] ?? "none")
        mut bySamples = Map<Samples, int>()
        bySamples[Samples(ws)] = 1
        bySamples[Samples(vs)] = 2
        print(bySamples.Count)
        print(Samples(ws) == Samples(vs))
        print(ws == vs)
        mut fls = List<float>()
        fls.Add(float(nan))
        mut flls = List<List<float>>()
        flls.Add(fls)
        mut byLists = Map<List<List<float>>, int>()
        byLists[flls] = 3
        print(byLists[flls] ?? 0)

        // keys of a nullable type, `null` among them, and lists of nullable items: `null` equal to `null` alone (N10), a `double?` key's
        // value and a `double?` item's in a `data` value as C#'s `Equals`, NaN equal to NaN and -0.0 to 0.0, and two lists alone by IEEE's
        // `==` (section 4, the owner, 2026-10-30, "As plain double"; for the other types an assumption there)
        mut byMaybe = Map<int?, string>()
        byMaybe[null] = Made(0)
        byMaybe[1] = "one"
        int? noKey = null
        print(byMaybe[noKey] ?? "missing")
        print(byMaybe[1] ?? "missing")
        print(byMaybe[2] ?? "missing")
        print(byMaybe.Count)
        print(byMaybe.Remove(1) ?? "missing")
        print(byMaybe)
        mut byNote = Map<string?, int>()
        byNote[Made(1)] = 1
        byNote[null] = 2
        print(byNote[Made(1)] ?? 0)
        print(byNote[null] ?? 0)
        mut byReal = Map<double?, int>()
        byReal[nan] = 1
        byReal[null] = 2
        byReal[-z] = 3
        print(byReal[-nan] ?? 0)
        print(byReal[null] ?? 0)
        print(byReal[z] ?? 0)
        print(byReal.Count)
        mut maybes = List<double?>()
        maybes.Add(nan)
        maybes.Add(null)
        print(maybes == maybes)
        print(Maybes(maybes) == Maybes(maybes))
        mut nulls = List<double?>()
        nulls.Add(null)
        nulls.Add(-z)
        mut zeros = List<double?>()
        zeros.Add(null)
        zeros.Add(z)
        print(nulls == zeros)

        // the entry, a value of `Entry<K, V>`: kept in a list, read in a lambda, passed and compared, past a `Clear` of the map
        mut book = Map<string, List<int>>()
        mut pages = List<int>()
        pages.Add(1)
        book[Made(7)] = pages
        mut kept = List<Entry<string, List<int>>>()
        for e in book {
            kept.Add(e)
            book.Clear()
            kept.Add(Entry(e.Key, e.Value))
            kept.Add(e with { Value = List<int>() })
            f = () => e.Key
            print(f())
            ShowEntry(e)
            print(e == e)
        }
        print(kept)
        print(book.Count)

        // an entry a program makes and copies (section 4, the owner, 2026-10-30, "Both, as a data type")
        pair = Entry(Made(1), 2)
        print(pair)
        print(pair == Entry("made 1", 2))
        print(pair with { Value = 3 })

        // values of a type parameter, `T?` of a nullable `T` being `T` (N11)
        mut ages = Map<string, int?>()
        ages["a"] = null
        ages["b"] = 2
        print(ValueAt(ages, "a") ?? -1)
        print(ValueAt(ages, "b") ?? -1)
        print(ValueAt(ages, "c") ?? -1)
        mut names = Map<string, string>()
        names["x"] = Made(9)
        print(ValueAt(names, "x") ?? "none")
        print(Twice(Made(8)))
        print(Twice(List<int>()))

        // values of a union and function values: `taken` and `f` read and kept past a write over their key and a `Clear`, `gone` and
        // `g` removed after that write and kept past the `Clear` (N1; section 4, the owner, 2026-10-30, "Nullable form, as Option")
        mut mixed = Map<string, int | string>()
        mixed["n"] = 5
        mixed["s"] = Made(6)
        taken = mixed["s"]
        mixed["s"] = Made(7)
        gone = mixed.Remove("s")
        mixed.Clear()
        print(taken ?? 0)
        print(gone ?? 0)
        suffix = Made(8)
        mut ops = Map<string, (int) => string>()
        ops["tag"] = x => "{x} {suffix}"
        f = ops["tag"]
        ops["tag"] = x => "{x}"
        g = ops.Remove("tag")
        ops.Clear()
        if f != null {
            print(f(1))
        }
        if g != null {
            print(g(2))
        }
        print(ops.Count)
        """;

    private static readonly string[] MapMembersPrint =
    [
        "0", // a map without a block
        "false",
        "none",
        "20",
        "true",
        "19", // a `Remove`
        "false",
        "none",
        "none", // a second `Remove` of the key
        "back",
        "20",
        "made 104", // a copy keeps the entry a `Remove` took out of the map (M3)
        "none",
        "20",
        "19",
        "made 103", // the value `Remove` gave, after a `Clear` of the map
        "made 103", // the value a read gave
        "made 5", // a `Remove` whose key is a value read from the map, the key's block and the value's one block
        "0",
        "34", // the multiples of 3 below 100
        "34",
        "49",
        "-1",
        "34", // a loop over the entries the removals left, past the slots they marked
        "0", // a `Clear` of a block a copy holds
        "2",
        "[x: [1]]",
        "[]", // a `Clear` of a block the map alone holds
        "[2: made 2]",
        "0",
        "45090", // a loop with two names
        "135", // a loop with one
        "9", // a `continue`
        "5", // and a `break` after five entries, whatever the order of the slots
        "10", // a loop that removes and writes the map it walks
        "10",
        "true",
        "1135", // a loop that clears the map it walks, the keys from 100 to 109 and the values from 0 to 18
        "0",
        "125", // the names read after a `Remove` and a `Clear` of the map the loop walks
        "2", // an enum key
        "one", // `data` keys equal by content, a joined string's and a literal's
        "none",
        "2",
        "uno",
        "12", // list keys equal by their items
        "0",
        "12", // a part of a list
        "5", // a list without items
        "0", // the map's key is a value of its own (M3): `k1` changed, and no entry is found under its items
        "12", // and the entry is still found under the items the key had
        "1", // a `data` key with a nullable field and a list
        "2",
        "0",
        "7", // a key of a type that holds itself through a list
        "0",
        "1", // a `duration` key
        "true", // a null entry
        "-1",
        "-1",
        "2",
        "2",
        "-1",
        "0",
        "made 1",
        "null",
        "made 1",
        "[2: null]",
        "2", // `double` keys: NaN, and 0.0 written over by -0.0
        "nan",
        "nan",
        "minus zero",
        "true",
        "2", // `float` keys
        "1",
        "1", // a `data` key with a `double` field
        "2",
        "2",
        "found", // a list key with a NaN and -0.0 among its items
        "1", // a `data` key that holds such a list
        "true",
        "false", // the two lists alone, by IEEE's `==`
        "3", // a list of lists of `float` items as a key
        "made 0", // an `int?` key: `null` finds `null`
        "one",
        "missing",
        "2",
        "one",
        "[null: made 0]",
        "1", // a `string?` key
        "2",
        "1", // a `double?` key: NaN finds NaN, `null` finds `null`, 0.0 finds -0.0
        "2",
        "3",
        "3",
        "false", // a `List<double?>` alone, by IEEE's `==`
        "true", // the same list in a `data` value, as C#'s `Equals`
        "true", // `null` equal to `null`, -0.0 to 0.0
        "made 7", // the entry read in a lambda, passed and compared, after a `Clear` of the map
        "1",
        "true",
        "[Entry { Key = made 7, Value = [1] }, Entry { Key = made 7, Value = [1] }, Entry { Key = made 7, Value = [] }]", // one made, one copied
        "0",
        "Entry { Key = made 1, Value = 2 }",
        "true",
        "Entry { Key = made 1, Value = 3 }",
        "-1", // values of a type parameter
        "2",
        "-1",
        "made 9",
        "1",
        "1",
        "made 6", // values of a union and function values, read and removed
        "made 7",
        "1 made 8",
        "2",
        "0",
    ];

    // `AddRange` and `Reverse` (X9): a list appended to itself, beside a copy and alone, a list made for the call, a list of one item, a
    // path of fields beside a copy of the data value; `IndexOf` of an item and of a run (X9), on a list of lists (F13), of `double` items
    // by IEEE's `==` and of `data` items as D3 compares them (M9); the searches of a string made while the program runs, with arguments
    // made in the call and read from a variable, a field and an item, and of `char`s of three and four bytes (X1); a list of bytes that
    // shares a string's block, grown while the string keeps its bytes (M3); and `AddRange` of a list read from an item and from a field
    private const string Searches = """
        data Bag(List<string> Items)
        data P(string Name, double X)

        string Made(int n) => "made {n}"

        mut words = [Made(0), Made(1)]
        copy = words
        words.AddRange(words)
        print(words)
        print(copy)
        mut alone = List<string>()
        for i in 0..<3 {
            alone.Add(Made(i + 2))
        }
        alone.AddRange(alone)
        print(alone)
        alone.AddRange(List<string>())
        alone.AddRange([Made(5)])
        print(alone.Count)
        held = alone
        alone.Reverse()
        print(alone)
        print(held)
        alone.Reverse()
        print(alone == held)
        mut one = [Made(9)]
        one.Reverse()
        print(one)
        mut bag = Bag([Made(6)])
        other = bag
        bag.Items.AddRange(bag.Items)
        bag.Items.Reverse()
        print(bag.Items)
        print(other.Items)

        mut grid = [[1], [2, 3]]
        grid.AddRange([[4]])
        grid.AddRange(grid)
        print(grid)
        print(grid.IndexOf([2, 3]) ?? -1)
        print(grid.IndexOf([[4], [1]]) ?? -1)
        print(grid.IndexOf([[4], [2]]) ?? -1)
        double z = 0.0
        nan = z / z
        ds = [1.0, nan, -z]
        print(ds.IndexOf(nan) ?? -1)
        print(ds.IndexOf(z) ?? -1)
        print(ds.IndexOf([nan]) ?? -1)
        ps = [P(Made(7), nan), P(Made(8), 1.0)]
        print(ps.IndexOf(P(Made(7), nan)) ?? -1)
        print(ps.IndexOf(P(Made(8), 2.0)) ?? -1)
        print(words.IndexOf(Made(1)) ?? -1)
        print(words.IndexOf([Made(1), Made(0)]) ?? -1)

        s = "{Made(1)}-{Made(2)}"
        print(s.IndexOf("-") ?? -1)
        print(s.IndexOf(Made(2)) ?? -1)
        print(s.Contains(Made(3)))
        print(s.StartsWith(Made(1)))
        print(s.EndsWith('2'))
        print(s.IndexOf('ä') ?? -1)
        mut bs = s.Bytes
        bs.AddRange("!".Bytes)
        print(bs.IndexOf(byte(33)) ?? -1)
        print(bs.IndexOf("e 2!".Bytes) ?? -1)
        print(s)
        needle = Made(2)
        print(s.IndexOf(needle) ?? -1)
        print(s.StartsWith(words[1]))
        print(s.Contains(bag.Items[0]))
        mut flat = [0]
        flat.AddRange(grid[1])
        print(flat)
        mut more = [Made(3)]
        more.AddRange(bag.Items)
        print(more)
        t = "a€😀"
        euro = '€'
        print(t.IndexOf(euro) ?? -1)
        print(t.IndexOf('😀') ?? -1)
        print(t.Contains('😁'))
        """;

    private static readonly string[] SearchesPrint =
    [
        "[made 0, made 1, made 0, made 1]", // a list appended to itself beside a copy, which keeps its items (M3)
        "[made 0, made 1]",
        "[made 2, made 3, made 4, made 2, made 3, made 4]", // a list it alone holds, appended to itself
        "7",
        "[made 5, made 4, made 3, made 2, made 4, made 3, made 2]", // reversed beside a copy, which keeps its order
        "[made 2, made 3, made 4, made 2, made 3, made 4, made 5]",
        "true",
        "[made 9]",
        "[made 6, made 6]", // through a field, beside a copy of the data value
        "[made 6]",
        "[[1], [2, 3], [4], [1], [2, 3], [4]]",
        "1", // an item of a list of lists
        "2", // a run of it
        "-1",
        "-1", // NaN is no item of a list of `double`: IEEE's `==`
        "2", // 0.0 is -0.0
        "-1",
        "0", // a `data` item with a NaN field is equal to itself (D3)
        "-1",
        "1",
        "1",
        "6",
        "7",
        "false",
        "true",
        "true",
        "-1",
        "13", // a list of bytes that shares the string's block, grown
        "10",
        "made 1-made 2", // the string keeps its bytes (M3)
        "7",
        "true",
        "false",
        "[0, 2, 3]",
        "[made 3, made 6, made 6]",
        "1", // a `char` of three bytes
        "4", // of four
        "false", // a `char` whose four bytes differ from the string's in the last
    ];

    // `Insert` and `RemoveAt` (X11): an item inserted beside a copy, which keeps its items (M3), and one read from the list it goes into,
    // which `Insert` holds before the list moves (M7); the item `RemoveAt` gives, kept, printed and dropped, from a list beside a copy;
    // a list it alone holds grown by inserts at the end and at the front and emptied from its middle; through a path of fields beside a
    // copy of the data value, with an item read from the same path; and a list of lists, whose item is a block
    private const string Edits = """
        data Bag(List<string> Items)

        string Made(int n) => "made {n}"

        mut words = [Made(1), Made(3)]
        held = words
        words.Insert(1, Made(2))
        print(words)
        print(held)
        words.Insert(0, words[2])
        print(words)
        gone = words.RemoveAt(1)
        print(gone)
        print(words)
        copy = words
        print(words.RemoveAt(2))
        print(copy)
        words.RemoveAt(0)
        print(words)
        mut alone = List<string>()
        for i in 0..<5 {
            alone.Insert(alone.Count, Made(i))
        }
        for i in 0..<5 {
            alone.Insert(0, Made(10 + i))
        }
        print(alone.Count)
        print(alone[0])
        print(alone[9])
        mut total = 0
        while alone.Count > 0 {
            s = alone.RemoveAt(alone.Count / 2)
            total = total + s.Bytes.Count
        }
        print(total)
        mut bag = Bag([Made(6)])
        other = bag
        bag.Items.Insert(0, bag.Items[0])
        print(bag.Items)
        print(other.Items)
        print(bag.Items.RemoveAt(1))
        mut grid = [[1], [2, 3]]
        grid.Insert(1, grid[1])
        print(grid)
        row = grid.RemoveAt(0)
        print(row)
        print(grid)
        """;

    private static readonly string[] EditsPrint =
    [
        "[made 1, made 2, made 3]", // inserted beside a copy, which keeps its items (M3)
        "[made 1, made 3]",
        "[made 3, made 1, made 2, made 3]", // the item read from the list before it moves
        "made 1",
        "[made 3, made 2, made 3]",
        "made 3", // removed from a list beside a copy
        "[made 3, made 2, made 3]",
        "[made 2]", // an item given and dropped
        "10",
        "made 14",
        "made 4",
        "65", // five items of 6 bytes and five of 7
        "[made 6, made 6]", // through a field, beside a copy of the data value
        "[made 6]",
        "made 6",
        "[[1], [2, 3], [2, 3]]",
        "[1]",
        "[[2, 3], [2, 3]]",
    ];

    // The sorts of X8: a list of strings sorted beside a copy, which keeps its order (M3), and one it alone holds, longer than a run,
    // sorted stable and unstable; `SortBy` with keys the call makes, strings let go of after the sort, with a key that reads a copy of
    // the list it sorts, which the lambda holds (F11), so that the sort copies its block, and on a list of one item; and a sort through a
    // field of a data value beside a copy
    private const string Sorts = """
        data Bag(List<string> Items)

        string Made(int n) => "made {n}"

        mut words = [Made(3), Made(1), Made(2), Made(1)]
        held = words
        words.Sort()
        print(words)
        print(held)
        mut many = List<string>()
        for i in 0..<45 {
            many.Add(Made((i * 7) % 45))
        }

        mut other = many
        many.Sort()
        other.SortUnstable()
        print(many == other)
        print(many[0])
        print(many[44])
        mut keyed = [Made(2), Made(10), Made(1)]
        keyed.SortBy(x => Made(x.Bytes.Count))
        print(keyed)
        mut own = [3, 1, 2]
        same = own
        own.SortBy(x => same.Count - x)
        print(own)
        print(same)
        mut one = [Made(9)]
        one.SortBy(x => x)
        print(one)
        mut bag = Bag([Made(2), Made(1)])
        copy = bag
        bag.Items.Sort()
        print(bag.Items)
        print(copy.Items)
        """;

    private static readonly string[] SortsPrint =
    [
        "[made 1, made 1, made 2, made 3]", // sorted beside a copy, which keeps its order (M3)
        "[made 3, made 1, made 2, made 1]",
        "true",
        "made 0",
        "made 9",
        "[made 2, made 1, made 10]", // by the keys made 6, made 7 and made 6, of items of 6, 7 and 6 bytes, equal keys in their order
        "[3, 2, 1]", // by keys of 0, 2 and 1, the block the lambda holds copied first
        "[3, 1, 2]",
        "[made 9]",
        "[made 1, made 2]", // through a field, beside a copy of the data value
        "[made 2, made 1]",
    ];

    // The trims, splits, replaces, changes of case and joins of X3 to X6 on strings made while the program runs, each source given a
    // new value after its result is taken, with the result printed after that: a trim that copies, one that keeps the whole string,
    // which shares it, and one of a literal, which keeps its bytes in place, a trim of a `char` from the start and from the end, and
    // the ASCII trims of the start and of the end; a
    // split into copies, into the whole string and of a literal, an item of a split read after its list is gone; a replace that finds
    // nothing, which shares the string, one with arguments made in the call, one into an empty string and one into the variable it
    // reads, and a replace of a `char` by a `char` of three bytes into the variable it reads and one that finds nothing; a change of
    // case that changes nothing and one that changes the string, by ASCII's letters and by Unicode's mapping, which grows it; a join of a list in a field of a data value given a new value, of a
    // list made for the call, of items whose text is made for the join (A4), and with a `char` separator read from a variable and
    // written in the call; and a cut of X2 that copies, one that keeps the whole string, one of a literal, one of nothing and one
    // through a field, and a parse and a decode (X7, H7) of a variable given a new value after it, of a cut made in the call and of a
    // field, and a parse of a `double`, a `float` and a `decimal` of the same and of an interpolation, the data value of the field
    // given a new value after its cut, its decode and its parse
    private const string Pieces = """
        data Bag(List<string> Items)
        data Name(string Value)

        string Made(int n) => "made {n}"

        mut t = "  {Made(1)}\t"
        a = t.Trim()
        t = Made(2)
        print(a)
        mut w = Made(3)
        b = w.Trim()
        w = Made(4)
        print(b)
        print("  lit  ".Trim())
        print("x{Made(5)}xx".Trim('x'))
        mut q = "\u3000{Made(6)}\u00A0 "
        print(q.TrimAscii())
        q = q.Trim()
        print(q)
        name = Name("  {Made(7)} ")
        start = name.Value.TrimStart()
        end = name.Value.TrimEnd()
        print("[{start}]")
        print("[{end}]")
        mut v = "xx{Made(8)}xx"
        lead = v.TrimStart('x')
        tail = v.TrimEnd('x')
        v = Made(0)
        print(lead)
        print(tail)
        mut z = "  {Made(9)}  "
        front = z.TrimStartAscii()
        back = z.TrimEndAscii()
        z = Made(0)
        print("[{front}]")
        print("[{back}]")

        mut s = "{Made(1)},{Made(2)},"
        parts = s.Split(',')
        s = Made(0)
        print(parts)
        print(parts.Count)
        mut s2 = Made(8)
        whole = s2.Split(";")
        s2 = Made(9)
        print(whole)
        print("a b".Split(' '))
        first = "{Made(1)}:{Made(2)}".Split(':')[0]
        print(first)
        print("{Made(3)}::".Split("::"))

        mut r = Made(7)
        same = r.Replace("zz", "y")
        r = Made(8)
        print(same)
        print(r.Replace("made", "M"))
        print(r.Replace(Made(8), Made(9)))
        none = ""
        gone = r.Replace(Made(8), none)
        print("[{gone}]")
        print(r.Replace(' ', "_"))
        r = r.Replace('e', "ee")
        print(r)
        r = r.Replace('e', '€')
        print(r)
        print(r.Replace('z', 'y'))

        mut u = Made(1)
        low = u.ToLowerAscii()
        u = Made(2)
        print(low)
        up = low.ToUpperAscii()
        print(up)
        print(up.ToLowerAscii())

        mut greek = "ŉΣ{Made(3)}xΣ"
        lowered = greek.ToLower()
        greek = Made(4)
        print(lowered)
        raised = lowered.ToUpper()
        print(raised)
        print(raised.ToUpper())

        mut bag = Bag([Made(1), Made(2)])
        j = string.Join(", ", bag.Items)
        bag = Bag([Made(3)])
        print(j)
        print(string.Join("-", [1, 2, 3]))
        print(string.Join(Made(0), bag.Items))
        words = [Made(4), Made(5)]
        print(string.Join(Made(0), words))
        print(string.Join("+", "{Made(1)} {Made(2)}".Split(' ')))
        print(string.Join("; ", [[1], [2, 3]]))
        print(string.Join(",", [true, false]))
        sep = '€'
        print(string.Join(sep, words))
        print(string.Join(';', [1, 2]))
        empty = string.Join(", ", List<string>())
        print("[{empty}]")

        mut cut = " {Made(9)}ä"
        piece = cut.Substring(1, 6)
        cut = Made(0)
        print(piece)
        print(piece.Substring(0, piece.Bytes.Count))
        print("lit cut".Substring(4, 3))
        print("[{Made(1).Substring(3, 0)}]")
        mut named = Name("{Made(2)}ä")
        inField = named.Value.Substring(5, 3)
        named = Name("{Made(2)}ä")
        print(inField)
        mut digits = " {40 + 2}\t"
        long | ParseError number = long.Parse(digits)
        digits = Made(0)
        print(number)
        byte | ParseError half = byte.Parse(Made(1).Substring(5, 1))
        print(half)
        Bytes | HexError hexed = Hex.Decode("{Made(1).Substring(5, 1)}f")
        print(hexed)
        Bytes | HexError wrong = Hex.Decode(named.Value)
        named = Name("{Made(2)}ä")
        print(wrong)
        mut real = " {1}.5e1 "
        double | ParseError scaled = double.Parse(real)
        real = Made(0)
        print(scaled)
        float | ParseError single = float.Parse(Made(1).Substring(5, 1))
        print(single)
        decimal | ParseError money = decimal.Parse("-{Made(2).Substring(5, 1)}.50")
        print(money)
        double | ParseError fielded = double.Parse(named.Value)
        named = Name(Made(0))
        print(fielded)
        """;

    private static readonly string[] PiecesPrint =
    [
        "made 1", // a trim that copies, its source given a new value
        "made 3", // a trim that keeps the whole string shares it
        "lit", // a literal's bytes in place
        "made 5",
        "\u3000made 6\u00A0", // the ASCII form keeps whitespace outside ASCII (X4)
        "made 6", // into the variable it reads
        "[made 7 ]",
        "[  made 7]",
        "made 8xx", // a `char` from the start
        "xxmade 8", // from the end
        "[made 9  ]", // the ASCII form of the start, its source given a new value
        "[  made 9]", // of the end
        "[made 1, made 2, ]", // copies, an empty piece kept
        "3",
        "[made 8]", // the whole string, shared
        "[a, b]",
        "made 1", // an item read from a list that is gone after the statement
        "[made 3, ]",
        "made 7", // a replace that finds nothing shares the string
        "M 8",
        "made 9",
        "[]",
        "made_8",
        "madee 8", // into the variable it reads
        "mad€€ 8", // a `char` of one byte by one of three
        "mad€€ 8", // a `char` it does not find
        "made 1", // a change of case that changes nothing shares the string
        "MADE 1",
        "made 1",
        "ŉσmade 3xς", // a change of case by Unicode's mapping, its source given a new value, a sigma that ends a word lowered to `ς`
        "ʼNΣMADE 3XΣ", // a string that grows, `ŉ` raised to two code points
        "ʼNΣMADE 3XΣ", // one that changes nothing shares the string
        "made 1, made 2", // the field's list, its data value given a new value after the join
        "1-2-3",
        "made 3",
        "made 4made 0made 5",
        "made+1+made+2",
        "[1]; [2, 3]",
        "true,false",
        "made 4€made 5", // a `char` separator
        "1;2",
        "[]",
        "made 9", // a cut that copies, its source given a new value
        "made 9", // the whole string, shared
        "cut", // a literal's bytes in place
        "[]",
        "2ä", // through a field, its data value given a new value
        "42", // a parse of a variable given a new value after it
        "1",
        "[31]",
        "invalid digit at 0", // a decode of a field
        "15", // a parse of a `double` from a variable given a new value after it
        "1", // of a `float` from a cut that is gone after the statement
        "-2.50", // of a `decimal` from an interpolation
        "invalid digit", // of a field
    ];

    // `xs[i] = v` (M9, M4; section 4, the owner, 2026-10-30, "As C# and Rust"): a write into a list a copy holds (M3), an item read from
    // the list it is written into, over another item and over itself, a value that changes the list through a `mut` argument before the
    // write (M4), a `mut` parameter, an index of a narrow type, a list of lists, a list known not null, and a value made before the index
    // (M4); then `==` on two lists, which reads both while a call can change one (M7)
    private const string ListWrites = """
        string Made(int n) => "made {n}"

        int Swap(mut List<int> t) {
            t = List<int>()
            t.Add(5)
            return 7
        }

        void SetAll(mut List<string> t, string s) {
            for i in 0..<t.Count {
                t[i] = s
            }
        }

        List<int> Grown(mut List<int> t) {
            t.Add(9)
            return t
        }

        int Shown(string what, int n) {
            print("{what} {n}")
            return n
        }

        mut xs = List<int>()
        xs.Add(1)
        xs.Add(2)
        xs.Add(3)
        ys = xs
        xs[0] = 10
        print(xs)
        print(ys)
        xs[2] = xs[0] + xs[1]
        print(xs)
        mut ss = List<string>()
        ss.Add(Made(1))
        ss.Add(Made(2))
        ss[0] = ss[1]
        ss[1] = ss[1]
        print(ss)
        mut ls = List<List<string>>()
        ls.Add(ss)
        ls.Add(List<string>())
        ls[1] = ls[0]
        ls[0] = ls[0]
        ss[0] = "changed"
        print(ls)
        xs[0] = Swap(mut xs)
        print(xs)
        SetAll(mut ss, Made(9))
        print(ss)
        byte b = 1
        xs.Add(8)
        xs[b] = 9
        print(xs)
        mut List<string>? maybe = List<string>()
        if maybe != null {
            maybe.Add("a")
            maybe[0] = Made(3)
        }
        print(maybe)
        xs[Shown("index", 0)] = Shown("value", 4)
        print(xs)

        // `==` on two lists, by their counts and items, `double` items by IEEE's `==` (section 4, the owner, 2026-10-30, "By items, as Rust")
        print(xs == ys)
        mut same = List<int>()
        same.Add(4)
        same.Add(9)
        print(xs == same)
        print(xs != same)
        print(ls[0] == ls[1])
        double z = 0.0
        nan = z / z
        mut ds = List<double>()
        ds.Add(nan)
        print(ds == ds)
        mut zs = List<double>()
        zs.Add(z)
        mut ns = List<double>()
        ns.Add(-z)
        print(zs == ns)
        print(Grown(mut same) == same)
        print(List<List<int>>() == List<List<int>>())
        mut dds = List<List<double>>()
        dds.Add(ds)
        print(dds == dds)
        """;

    private static readonly string[] ListWritesPrint =
    [
        "[10, 2, 3]",
        "[1, 2, 3]", // the copy keeps its items (M3)
        "[10, 2, 12]",
        "[made 2, made 2]", // an item written over another and over itself
        "[[made 2, made 2], [made 2, made 2]]",
        "[7]", // the value first: `Swap` gave the variable a new list, and the write wrote it (M4)
        "[made 9, made 9]",
        "[7, 9]",
        "[made 3]",
        "value 4", // the value first, then the index (M4)
        "index 0",
        "[4, 9]",
        "false", // `==` on two lists
        "true",
        "false",
        "true",
        "false", // NaN unequal to NaN, as Rust's `Vec<f64>`
        "true", // -0.0 equal to 0.0
        "true", // a list a call changed, compared with the result the call gave
        "true",
        "false", // NaN unequal to NaN in the lists among the items too
    ];

    // a list and a map in a field, written through a path from a `mut` variable (M4, M5, M9) by each `mut` member, an item and an entry
    // written, `Add`, `Clear` and `Remove`: beside a copy taken before (M3), in a `mut` parameter, after a value that replaced the variable
    // (M4), nested, in a data type that holds itself, whose block another variable holds, with a key `Remove` reads from that block,
    // grown past its first block, and in a nullable field known not null (N3), of a plain value and of a block
    private const string Fields = """
        data Store(List<string> Names, Map<string, string> Etags, int Hits)
        data Shelf(Store Main, string Label)
        data Node(string Name, List<string> Tags, Map<string, int> Counts, Node? Next)
        data Pocket(List<string>? Items, Map<string, int>? Counts)
        data Bag(List<int>? Xs, Bag? Next)

        void Tag(mut Store store, string name) {
            store.Names.Add(name)
            store.Etags[name] = "e-{name}"
        }

        string Reset(mut Store store) {
            store = Store(List<string>(), Map<string, string>(), 0)
            return "after"
        }

        mut s = Store(List<string>(), Map<string, string>(), 0)
        copy = s
        s.Names.Add("a")
        s.Etags["a"] = "e1"
        print(copy)
        print(s)
        Tag(mut s, "b")
        print(s.Names)
        print(s.Etags["b"] ?? "none")
        before = s
        s.Names.Clear()
        print(s.Names)
        print(before.Names)
        s.Names.Add("c")
        s.Etags["k"] = Reset(mut s)
        print(s.Etags)
        print(s.Names)
        mut shelf = Shelf(Store(List<string>(), Map<string, string>(), 0), "top")
        kept = shelf
        shelf.Main.Names.Add("x")
        shelf.Main.Etags["x"] = "y"
        print(shelf)
        print(kept)
        top = shelf
        shelf.Main.Names[0] = "z"
        shelf.Main.Etags.Clear()
        print(shelf)
        print(top)
        mut n = Node("a", List<string>(), Map<string, int>(), null)
        held = n
        n.Tags.Add("t")
        n.Counts["c"] = 1
        print(n.Tags)
        print(held.Tags)
        print(n.Counts)
        print(held.Counts)
        again = n
        n.Tags.Clear()
        print(n.Tags)
        print(again.Tags)
        old = n
        print(n.Counts.Remove("c") ?? 0)
        print(n.Counts)
        print(old.Counts)
        n.Counts[n.Name] = 3
        named = n
        print(n.Counts.Remove(n.Name) ?? 0)
        print(n.Counts)
        print(named.Counts)
        mut built = Node("k{n.Tags.Count}", List<string>(), Map<string, int>(), null)
        built.Counts[built.Name] = 4
        other = built
        print(built.Counts.Remove(built.Name) ?? 0)
        print(built.Counts)
        print(other.Counts)
        mut i = 0
        while i < 40 {
            s.Names.Add("n{i}")
            s.Etags["n{i}"] = "v{i}"
            i = i + 1
        }
        print(s.Names.Count)
        print(s.Etags["n39"] ?? "none")
        print(s.Etags["k"] ?? "none")
        written = s
        s.Names[0] = "w"
        print(s.Names[0])
        print(written.Names[0])
        mut p = Pocket(List<string>(), Map<string, int>())
        pc = p
        if p.Items != null {
            p.Items.Add("i")
            p.Items.Add("j")
        }
        if p.Counts != null {
            p.Counts["c"] = 2
        }
        print(p)
        print(pc)
        if p.Items != null {
            p.Items.Clear()
        }
        print(p.Items)
        if p.Counts != null {
            p.Counts.Clear()
        }
        print(p.Counts)
        mut bag = Bag(List<int>(), null)
        stash = bag
        if bag.Xs != null {
            bag.Xs.Add(3)
        }
        print(bag.Xs)
        print(stash.Xs)
        prior = bag
        if bag.Xs != null {
            bag.Xs[0] = 4
        }
        print(bag.Xs)
        print(prior.Xs)
        """;

    private static readonly string[] FieldsPrint =
    [
        "Store { Names = [], Etags = [], Hits = 0 }", // the copy keeps what it had (M3)
        "Store { Names = [a], Etags = [a: e1], Hits = 0 }",
        "[a, b]", // through a `mut` parameter
        "e-b",
        "[]",
        "[a, b]", // a `Clear` beside a copy
        "[k: after]", // the value first, then the map of the variable it replaced (M4)
        "[]",
        "Shelf { Main = Store { Names = [x], Etags = [x: y], Hits = 0 }, Label = top }", // nested
        "Shelf { Main = Store { Names = [], Etags = [], Hits = 0 }, Label = top }",
        "Shelf { Main = Store { Names = [z], Etags = [], Hits = 0 }, Label = top }", // an item written and a map's `Clear`, nested
        "Shelf { Main = Store { Names = [x], Etags = [x: y], Hits = 0 }, Label = top }",
        "[t]", // a data type that holds itself, whose block another variable holds
        "[]",
        "[c: 1]",
        "[]",
        "[]",
        "[t]",
        "1", // a `Remove`
        "[]",
        "[c: 1]",
        "3", // a `Remove` whose key is read from the block the path copies, which the other variable keeps
        "[]",
        "[a: 3]",
        "4", // the same with a key made while the program runs, which holds a block of its own
        "[]",
        "[k0: 4]",
        "40", // grown past the first block
        "v39",
        "after",
        "w", // an item written beside a copy
        "n0",
        "Pocket { Items = [i, j], Counts = [c: 2] }", // a nullable field known not null
        "Pocket { Items = [], Counts = [] }",
        "[]",
        "[]",
        "[3]", // the same in a block another variable holds
        "[]",
        "[4]",
        "[3]",
    ];

    // every way out of a block a map is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, a copy written to, a lambda's environment, a list of maps, a map
    // of maps whose entry is written over, a map of strings whose value and key a write over a key lets go of, and entries kept, made
    // and copied
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
            mut held = Map<string, Map<int, int>>()
            held["{round}"] = Make(100)
            held["b"] = Make(100)
            kept = held
            out = held.Remove("{round}") ?? Map<int, int>()
            held.Remove("b")
            for k, v in kept {
                held[k] = v
            }
            mut ents = List<Entry<string, Map<int, int>>>()
            for e in kept {
                total = total + e.Value.Count - 100
                ents.Add(e)
                ents.Add(Entry(e.Key, Make(10)))
                ents.Add(e with { Key = "{round} {e.Key}" })
            }
            other = held
            held.Clear()
            held["c"] = Make(100)
            held.Clear()
            mut ls = List<Map<int, int>>()
            ls.Add(Make(10))
            ls.Add(Make(10))
            ls[0] = ls[1]
            ls[1] = Make(100)
            total = total + Early(1000) + Seven(q.A) + Seven(b) - Seven(a) + h() + Seven(ms[0]) + Seven(nested[0] ?? Map<int, int>()) + (words["a"] ?? "").Bytes.Count - (words["{round}"] ?? "").Bytes.Count - 36
            total = total + Seven(out) + Seven(ls[1]) - 14 + other.Count + kept.Count - 4 + ents.Count - 6
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
    /// The runtime of this machine's target whose system gives the pieces of a mebibyte the allocator carves its blocks of up to 262144
    /// bytes from, and refuses every other request: a block of its own, which a larger request takes, is refused.
    /// </summary>
    private static CheckedRuntime PiecesOnly() => Edited((TargetFile, "pub byte* Memory(long bytes) {", """
        pub byte* Memory(long bytes) {
            if bytes != 1048576 {
                return null
            }

            return Granted(bytes)
        }

        byte* Granted(long bytes) {
        """));

    /// <summary>
    /// The runtime of this machine's target whose system refuses a request that would take what it gave and was not given back past
    /// 64 mebibytes; its own `Memory`, `Release` and `Resize` run behind the count, a `Resize` counted by what it adds, with the edits
    /// <paramref name="more"/> beside them.
    /// </summary>
    private static CheckedRuntime Bytes(params (string Path, string Old, string New)[] more) => Edited([.. Counting, .. more]);

    /// <summary>The edits of <see cref="Bytes"/>: the target's `Memory`, `Release` and `Resize` behind the count.</summary>
    private static (string Path, string Old, string New)[] Counting =>
    [
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
            """),
        (TargetFile, "pub byte* Resize(byte* p, long bytes, long grown) {", """
            pub byte* Resize(byte* p, long bytes, long grown) {
                mut long total = 0
                raw {
                    live = live + grown - bytes
                    total = live
                }

                mut byte* q = null
                if total <= 67108864 {
                    q = Resized(p, bytes, grown)
                }

                if q == null {
                    raw {
                        live = live - grown + bytes
                    }
                }

                return q
            }

            byte* Resized(byte* p, long bytes, long grown) {
            """),
    ];

    /// <summary>The edit that has the thread keep no freed block of more than 262144 bytes, so that each one's `Free` gives it back to the system.</summary>
    private static readonly (string Path, string Old, string New) KeptNone = ("std/std.kz", "keptBytes + bytes <= 33554432", "keptBytes + bytes <= 0");

    /// <summary>
    /// The runtime of this machine's target whose `Memory` and `Resize` count the bytes the system gives, by a `Memory` and by the
    /// growth of a `Resize`, none of them counted back by `Release`, and whose `Memory` refuses a request that takes them past
    /// <paramref name="limit"/>; a `Resize` is counted and not refused, since `Reallocate` takes a kept block after a refused one.
    /// </summary>
    private static CheckedRuntime Taking(long limit) => Edited(
        (TargetFile, "pub byte* Memory(long bytes) {", $$"""
            thread static mut long taken = 0

            pub byte* Memory(long bytes) {
                mut long total = 0
                raw {
                    total = taken + bytes
                }

                if total > {{limit}} {
                    return null
                }

                byte* p = Granted(bytes)
                if p != null {
                    raw {
                        taken = total
                    }
                }

                return p
            }

            byte* Granted(long bytes) {
            """),
        (TargetFile, "pub byte* Resize(byte* p, long bytes, long grown) {", """
            pub byte* Resize(byte* p, long bytes, long grown) {
                byte* q = Resized(p, bytes, grown)
                if q != null {
                    raw {
                        taken = taken + grown - bytes
                    }
                }

                return q
            }

            byte* Resized(byte* p, long bytes, long grown) {
            """));

    /// <summary>
    /// The edits that make the allocator take every block from pages of its own and make them unreachable when the block is freed, so
    /// that a read or a write of a freed block ends the process with the system's signal; nothing is used again. The thread keeps no
    /// freed block: a kept block stays readable so that a request can take it, so the fence gives every block back to the system.
    /// </summary>
    private static (string Path, string Old, string New)[] Fence => Builder.HostTarget == Target.Windows
        ?
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            ("std/std.kz", "keptBytes + bytes <= 33554432", "keptBytes + bytes <= 0"),
            (TargetFile, "VirtualFree(Ptr<void>(p), 0, 32768)", "VirtualFree(Ptr<void>(p), ulong(bytes), 16384)"), // MEM_DECOMMIT: the pages stay reserved
        ]
        :
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            ("std/std.kz", "keptBytes + bytes <= 33554432", "keptBytes + bytes <= 0"),
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
        Ran(BuildAndRun(MapMembers, toolchain.Runtime, "map-members"), 0, MapMembersPrint, "");
        Ran(BuildAndRun(Searches, toolchain.Runtime, "list-searches"), 0, SearchesPrint, "");
        Ran(BuildAndRun(Edits, toolchain.Runtime, "list-edits"), 0, EditsPrint, "");
        Ran(BuildAndRun(Sorts, toolchain.Runtime, "list-sorts"), 0, SortsPrint, "");
        Ran(BuildAndRun(Pieces, toolchain.Runtime, "string-pieces"), 0, PiecesPrint, "");
        Ran(BuildAndRun(ListWrites, toolchain.Runtime, "list-writes"), 0, ListWritesPrint, "");
        Ran(BuildAndRun(Fields, toolchain.Runtime, "field-collections"), 0, FieldsPrint, "");
    }

    [Fact]
    public void An_integer_shows_the_digits_dotnet_writes_in_a_print_a_hole_a_list_and_a_data_value()
    {
        // both signs of every length, each side of each power of ten, the ends of the range, and a xorshift sequence whose groups of
        // four and of two digits take every pair of the runtime's table
        var longs = new List<long> { long.MinValue, long.MaxValue, 0 };
        for (var p = 1L; ; p *= 10)
        {
            longs.AddRange([p - 1, p, p + 1, -p, 1 - p, p * 9]);
            if (p > long.MaxValue / 10)
            {
                break;
            }
        }

        var ulongs = new List<ulong> { ulong.MaxValue, 0 };
        for (var p = 1UL; ; p *= 10)
        {
            ulongs.AddRange([p - 1, p, p + 1]);
            if (p <= ulong.MaxValue / 9)
            {
                ulongs.Add(p * 9);
            }

            if (p > ulong.MaxValue / 10)
            {
                break;
            }
        }

        var x = 88172645463325252UL;
        for (var i = 0; i < 400; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            longs.Add((long)x >> (int)(x % 61));
            ulongs.Add(x >> (int)(x % 61));
        }

        static string Literal(long v) => v == long.MinValue ? "-9223372036854775807 - 1" : v.ToString(CultureInfo.InvariantCulture);
        var text = $$"""
            data Pair(long L, ulong U)

            List<long> ls = [{{string.Join(", ", longs.Select(Literal))}}]
            List<ulong> us = [{{string.Join(", ", ulongs.Select(u => u.ToString(CultureInfo.InvariantCulture)))}}]
            for v in ls {
                print(v)
                print("<{v}>")
                print([v])
                print(Pair(v, 0))
            }
            for u in us {
                print(u)
                print("<{u}>")
                print([u])
                print(Pair(0, u))
            }
            """;
        string[] Shown(string digits, string pair) => [digits, $"<{digits}>", $"[{digits}]", $"Pair {{ {pair} }}"];
        var lines = longs.SelectMany(v => Shown(v.ToString(CultureInfo.InvariantCulture), $"L = {v.ToString(CultureInfo.InvariantCulture)}, U = 0"))
            .Concat(ulongs.SelectMany(u => Shown(u.ToString(CultureInfo.InvariantCulture), $"L = 0, U = {u.ToString(CultureInfo.InvariantCulture)}")))
            .ToArray();
        Ran(BuildAndRun(text, toolchain.Runtime, "integer-texts"), 0, lines, "");
    }

    [Fact]
    public void No_block_is_read_or_written_after_it_is_freed()
    {
        var fenced = Fenced;
        Ran(BuildAndRun(Values, fenced, "list-values-fenced"), 0, ValuesPrint, "");
        Ran(BuildAndRun(Sharing, fenced, "list-sharing-fenced"), 0, SharingPrint, "");
        Ran(BuildAndRun(Strings, fenced, "string-values-fenced", "raw"), 0, StringsPrint, "");
        Ran(BuildAndRun(Maps, fenced, "map-values-fenced"), 0, MapsPrint, "");
        Ran(BuildAndRun(MapMembers, fenced, "map-members-fenced"), 0, MapMembersPrint, "");
        Ran(BuildAndRun(Searches, fenced, "list-searches-fenced"), 0, SearchesPrint, "");
        Ran(BuildAndRun(Edits, fenced, "list-edits-fenced"), 0, EditsPrint, "");
        Ran(BuildAndRun(Sorts, fenced, "list-sorts-fenced"), 0, SortsPrint, "");
        Ran(BuildAndRun(Pieces, fenced, "string-pieces-fenced"), 0, PiecesPrint, "");
        Ran(BuildAndRun(ListWrites, fenced, "list-writes-fenced"), 0, ListWritesPrint, "");
        Ran(BuildAndRun(Fields, fenced, "field-collections-fenced"), 0, FieldsPrint, "");
        // every way out of a block, as the budget's program takes them, in rounds few enough for a mapping per block
        Ran(BuildAndRun(Rounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "list-rounds-fenced"), 0, ["20", "80020"], "");
        Ran(BuildAndRun(StringRounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "string-rounds-fenced"), 0, ["20", "61460"], "");
        Ran(BuildAndRun(MapRounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "map-rounds-fenced"), 0, ["20", "280"], "");
        // lists of more than 262144 bytes made and dropped, blocks the thread keeps and gives out again outside the fence: a `Where`
        // over 100000 items in five rounds
        const string where = "mut xs = List<long>()\nfor i in 0..<100000 {\n    xs.Add(long(i))\n}\nmut long total = 0\nfor r in 0..<5 {\n    total = total + long(xs.Where(x => x % 3 == 0).Count)\n}\nprint(total)\n";
        Ran(BuildAndRun(where, fenced, "list-where-fenced"), 0, ["166670"], "");

        // the positive control: a write into a freed block under the same allocator ends the process, and so does one into a freed
        // block of more than 262144 bytes, which the thread keeps outside the fence
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
        const string write = "    byte* p = Allocate(16)\n    raw {\n        std.Free(p, 16)\n        p[0] = 1\n    }\n";
        Crashed(BuildAndRun("print(1)\n", Edited([.. Fence, ("runtime/common/runtime.kz", start, start + write)]), "list-fence-control"));
        const string large = "    byte* p = Allocate(300000)\n    raw {\n        std.Free(p, 300000)\n        p[0] = 1\n    }\n";
        Crashed(BuildAndRun("print(1)\n", Edited([.. Fence, ("runtime/common/runtime.kz", start, start + large)]), "list-fence-control-large"));
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

        // and a map of 1000 entries held each round, a block of 34864 bytes, 2048 slots, in the size class of 65536: 196 megabytes
        const string maps = "Map<int, int> Make(int n) {\n    mut m = Map<int, int>()\n    for i in 0..<n {\n        m[i] = i\n    }\n    return m\n}\n\nmut all = List<Map<int, int>>()\nmut round = 0\nwhile round < 3000 {\n    round = round + 1\n    all.Add(Make(1000))\n}\nprint(all.Count)\n";
        var full = BuildAndRun(maps, Budget(), "map-rounds-held");
        Assert.Equal(1, full.Code);
        Assert.StartsWith("error: allocation-failed (", full.Error, StringComparison.Ordinal);
    }

    // lists of 70000 items made and dropped for 200 rounds, which grow through blocks of 262160 and 524304 bytes, and the same lists
    // all held
    private const string LargeRounds = "long Fill(int n) {\n    mut xs = List<int>()\n    mut i = 0\n    while i < n {\n        xs.Add(i)\n        i = i + 1\n    }\n    return long(xs.Count)\n}\n\nmut round = 0\nmut long total = 0\nwhile round < 200 {\n    round = round + 1\n    total = total + Fill(70000)\n}\nprint(total)\n";
    private const string LargeHeld = "mut all = List<List<int>>()\nmut round = 0\nwhile round < 200 {\n    round = round + 1\n    mut xs = List<int>()\n    mut i = 0\n    while i < 70000 {\n        xs.Add(i)\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n";

    [Fact]
    public void A_block_of_more_than_262144_bytes_that_the_thread_does_not_keep_goes_back_to_the_system()
    {
        // the target's own `Release`: a write into a block of 33554432 bytes, more than the thread keeps with its header, after its
        // `Free` ends the process with the system's signal
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
        const string write = "    byte* p = Allocate(33554432)\n    raw {\n        p[0] = 1\n        std.Free(p, 33554432)\n        p[0] = 2\n    }\n";
        Crashed(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + write)), "list-large-freed"));
        const string notFreed = "    byte* p = Allocate(33554432)\n    raw {\n        p[0] = 1\n        p[0] = 2\n    }\n";
        Ran(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + notFreed)), "list-large-not-freed"), 0, ["1"], ""); // the positive control: the block not freed

        // the lists of 70000 items, with the thread keeping no freed block, so that each `Free` of a large block gives it back: 150
        // mebibytes in 200 rounds were none of them given back
        Ran(BuildAndRun(LargeRounds, Bytes(KeptNone), "list-large-rounds"), 0, ["14000000"], "");
        var run = BuildAndRun(LargeHeld, Bytes(KeptNone), "list-large-held"); // the positive control: the same lists, all of them held
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);

        // strings of 524288 bytes joined from halves of 262144, blocks of 524304 and 262160 bytes with their counts, with the thread
        // keeping none of them: 150 mebibytes in 200 rounds were none of them given back
        const string fill = "string Fill(int n) {\n    mut s = \"x\"\n    mut i = 0\n    while i < n {\n        s = \"{s}{s}\"\n        i = i + 1\n    }\n    return s\n}\n\n";
        Ran(BuildAndRun(fill + "mut round = 0\nmut long total = 0\nwhile round < 200 {\n    round = round + 1\n    total = total + long(Fill(19).Bytes.Count)\n}\nprint(total)\n", Bytes(KeptNone), "string-large-rounds"), 0, ["104857600"], "");
        var strings = BuildAndRun(fill + "mut all = List<string>()\nmut round = 0\nwhile round < 200 {\n    round = round + 1\n    all.Add(Fill(19))\n}\nprint(all.Count)\n", Bytes(KeptNone), "string-large-held");
        Assert.Equal(1, strings.Code); // the positive control: the same strings, all of them held
        Assert.StartsWith("error: allocation-failed (", strings.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_freed_block_of_more_than_262144_bytes_is_given_out_again()
    {
        // the lists of 70000 items within the system's 16 pieces of memory: the first round asks the system for a block of 262176
        // bytes with its header, which grows to 524320, and each round after it takes that block back from the thread; blocks given
        // back at their `Free` would ask the system for one in each of the 200 rounds
        Ran(BuildAndRun(LargeRounds, Budget(), "list-large-reused"), 0, ["14000000"], "");
        var run = BuildAndRun(LargeHeld, Budget(), "list-large-reused-held"); // the positive control: the same lists, all of them held
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_thread_keeps_the_freed_blocks_of_more_than_262144_bytes_that_fit_within_33554432_bytes()
    {
        // five blocks of 8388544 bytes, 8388560 with their headers, freed in turn: the first four take 33554240 bytes and stay the
        // thread's, so a write into the fourth after its `Free` ends nothing; the fifth would take the kept bytes past 33554432 and goes
        // back to the system, where a write into it ends the process with the system's signal
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
        const string blocks = "    byte* a = Allocate(8388544)\n    byte* b = Allocate(8388544)\n    byte* c = Allocate(8388544)\n    byte* d = Allocate(8388544)\n    byte* e = Allocate(8388544)\n    raw {\n        std.Free(a, 8388544)\n        std.Free(b, 8388544)\n        std.Free(c, 8388544)\n        std.Free(d, 8388544)\n        std.Free(e, 8388544)\n";
        Ran(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + blocks + "        d[0] = 1\n    }\n")), "list-kept-within"), 0, ["1"], "");
        Crashed(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + blocks + "        e[0] = 1\n    }\n")), "list-kept-past"));
    }

    [Fact]
    public void A_kept_block_is_given_out_only_to_a_request_of_at_least_half_its_bytes()
    {
        // the owner, 2026-10-30, "Only if at least half used": a block of 33554416 bytes, 33554432 with its header, freed and kept,
        // which fills the 33554432 the thread keeps. A request of 16777184 bytes, 16777200 with its header, less than half of the kept
        // block's, takes memory of its own from the system, which its `Free` gives back, as the kept block leaves no room for it, so a
        // write into it after its `Free` ends the process with the system's signal
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
        static string Request(long size) => $"    byte* a = Allocate(33554416)\n    raw {{\n        std.Free(a, 33554416)\n    }}\n    byte* q = Allocate({size})\n    raw {{\n        std.Free(q, {size})\n        q[0] = 1\n    }}\n";
        Crashed(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + Request(16777184))), "list-kept-below-half"));
        // the positive control: a request of 16777200 bytes, 16777216 with its header, half of them, takes the kept block, which its
        // `Free` keeps again, so a write into it after its `Free` ends nothing
        Ran(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + Request(16777200))), "list-kept-half"), 0, ["1"], "");
    }

    [Fact]
    public void A_block_of_more_than_262144_bytes_grows_by_the_targets_Resize()
    {
        // two lists of `long`s within 64 mebibytes, the first of 2^21 items in a block of 16 mebibytes; the second grows from 16
        // mebibytes to 32. Grown in place, as Linux's `mremap` grows it, the two take 48 mebibytes; copied into a new block, as on
        // Windows, the old and the new block of the second are held together beside the first, 64 mebibytes and 96 bytes with their
        // headers before the pieces the small blocks are carved from and the blocks the thread keeps
        const string fill = "mut a = List<long>()\nfor i in 0..<2097152 {\n    a.Add(long(i))\n}\nmut b = List<long>()\nfor i in 0..<2097153 {\n    b.Add(long(i))\n}\nmut long total = 0\nfor i in 0..<b.Count {\n    total = total + b[i]\n}\nprint(a.Count)\nprint(b.Count)\nprint(total)\n";
        var run = BuildAndRun(fill, Bytes(), "list-grown-in-place");
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Equal(1, run.Code);
            Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);
        }
        else
        {
            Ran(run, 0, ["2097152", "2097153", "2199024304128"], "");
        }

        // the positive control: the first list one item longer, in a block of 32 mebibytes, beside the second's 32, outgrows them
        var over = BuildAndRun(fill.Replace("0..<2097152 {", "0..<2097153 {", StringComparison.Ordinal), Bytes(), "list-grown-over");
        Assert.Equal(1, over.Code);
        Assert.StartsWith("error: allocation-failed (", over.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_of_more_than_262144_bytes_that_outgrows_its_room_takes_a_kept_block_before_the_targets_Resize()
    {
        // the owner, 2026-11-01, "Kept block first": a list of 2^21 `long`s made and dropped for 20 rounds, its block growing from
        // 262176 bytes with its header to 16777248, the bytes the system gives counted with none given back. On Linux the first round
        // takes 17825824 bytes, the mebibyte the small blocks are carved from among them; in the second the block of 8388640 takes
        // the kept one of 16777248, which it uses at least half of, and the growths below it, too small for it, take 4194336 by
        // `Resize`; the third takes 1048608 and the fourth 262176 so, and each round after them none, its growths all served by kept
        // blocks, 23330944 bytes in all. Grown by `Resize` before the kept blocks, each round takes 16777248 bytes again, past the
        // 24 mebibytes in its second, and the first `Memory` of the third is refused. On Windows, whose `Resize` gives none, each growth
        // of the first round takes a block of its own, 34341088 bytes with the first mebibyte by the sizes above, and each round after it
        // the kept blocks of its sizes, within 40 mebibytes
        const string fill = "long Fill(int n) {\n    mut xs = List<long>()\n    mut i = 0\n    while i < n {\n        xs.Add(long(i))\n        i = i + 1\n    }\n    return long(xs.Count)\n}\n\nmut round = 0\nmut long total = 0\nwhile round < 20 {\n    round = round + 1\n    total = total + Fill(2097152)\n}\nprint(total)\n";
        long limit = Builder.HostTarget == Target.Windows ? 41943040 : 25165824;
        Ran(BuildAndRun(fill, Taking(limit), "list-grown-kept"), 0, ["41943040"], "");

        // the positive control: the same lists, all of them held, take memory of their own in the second round, past the limit, and a
        // `Memory` after it is refused
        var held = BuildAndRun("mut all = List<List<long>>()\nmut round = 0\nwhile round < 20 {\n    round = round + 1\n    mut xs = List<long>()\n    mut i = 0\n    while i < 2097152 {\n        xs.Add(long(i))\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n", Taking(limit), "list-grown-kept-held");
        Assert.Equal(1, held.Code);
        Assert.StartsWith("error: allocation-failed (", held.Error, StringComparison.Ordinal);
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

    [Fact]
    public void A_string_member_whose_block_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // M9, X3, X5: a literal's `Split` asks for the block of its list, 16 bytes of counts and two pieces of 16; `Replace` and a
        // change of case ask for the block of their result, 16 bytes of counts and its bytes, the 7 of `STRASSE` for the 7 of `straße`
        var none = EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value);
        Ran(BuildAndRun("print(\"a,b\".Split(\",\"))\n", none, "split-refused"), 1, [], "error: allocation-failed (48 bytes)\n");
        Ran(BuildAndRun("print(\"abc\".Replace(\"b\", \"xy\"))\n", none, "replace-refused"), 1, [], "error: allocation-failed (20 bytes)\n");
        Ran(BuildAndRun("print(\"abc\".ToUpperAscii())\n", none, "upper-refused"), 1, [], "error: allocation-failed (19 bytes)\n");
        Ran(BuildAndRun("print(\"ABC\".ToLowerAscii())\n", none, "lower-refused"), 1, [], "error: allocation-failed (19 bytes)\n");
        Ran(BuildAndRun("print(\"straße\".ToUpper())\n", none, "unicode-upper-refused"), 1, [], "error: allocation-failed (23 bytes)\n");
        const string granted = "print(\"a,b\".Split(\",\"))\nprint(\"abc\".Replace(\"b\", \"xy\"))\nprint(\"abc\".ToUpperAscii())\nprint(\"ABC\".ToLowerAscii())\nprint(\"straße\".ToUpper())\n";
        Ran(BuildAndRun(granted, toolchain.Runtime, "string-members-granted"), 0, ["[a, b]", "axyc", "ABC", "abc", "STRASSE"], ""); // the positive control

        // X6: the list `Join` reads takes a block, so this system gives the pieces small blocks are carved from and refuses a block of
        // its own; a result of 262145 bytes asks for one, 16 bytes of counts and its bytes
        const string join = "mut s = \"ab\"\nmut i = 0\nwhile i < 16 {\n    s = \"{s}{s}\"\n    i = i + 1\n}\n\nt = string.Join(\",\", [s, s])\nprint(t.Bytes.Count)\n";
        Ran(BuildAndRun(join, PiecesOnly(), "join-refused"), 1, [], "error: allocation-failed (262161 bytes)\n");
        Ran(BuildAndRun(join, toolchain.Runtime, "join-granted"), 0, ["262145"], ""); // the positive control

        // X5: the second of two pieces copied out of a string's block refused, `StringOf` refusing 2 bytes or more in the library's
        // text: the piece made before it and the list are given back, in the fenced runtime, and the run ends with the piece's bytes
        const string piece = "x = \"a\"\nprint(\"{x},bb\".Split(\",\"))\n";
        Ran(BuildAndRun(piece, Edited([.. Fence, ("std/std.kz", "    byte* text = StringBlock(count)\n", "    mut byte* text = null\n    if count < 2 {\n        text = StringBlock(count)\n    }\n\n")]), "split-piece-refused"), 1, [], "error: allocation-failed (18 bytes)\n");
        Ran(BuildAndRun(piece, Fenced, "split-piece-granted"), 0, ["[a, bb]"], ""); // the positive control

        // X4: a trim of a string made while the program runs copies the 3 bytes it keeps, `StringOf` refusing 2 bytes or more as
        // above, which reports 16 bytes of counts and the 3, a trim of whitespace and one of a `char` alike
        var copies = Edited([.. Fence, ("std/std.kz", "    byte* text = StringBlock(count)\n", "    mut byte* text = null\n    if count < 2 {\n        text = StringBlock(count)\n    }\n\n")]);
        const string trims = "x = \"a\"\nprint(\" {x}bc \".Trim())\nprint(\"xx{x}bc\".TrimStart('x'))\n";
        Ran(BuildAndRun(trims, copies, "trim-refused"), 1, [], "error: allocation-failed (19 bytes)\n");
        Ran(BuildAndRun("x = \"a\"\nprint(\"xx{x}bc\".TrimStart('x'))\n", copies, "trim-char-refused"), 1, [], "error: allocation-failed (19 bytes)\n");
        Ran(BuildAndRun(trims, Fenced, "trims-granted"), 0, ["abc", "abc"], ""); // the positive control
    }

    [Fact]
    public void A_Join_whose_buffer_of_texts_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // X6, O11: the texts of 100 `int` items take a buffer of 100 strings of 16 bytes from the runtime's `Allocate`, which ends
        // the process itself where the system refuses it, the runtime refusing 1000 to 4095 bytes as for the sorts; a list of
        // strings of as many items is its own texts and takes no buffer
        var refusing = RefusingSmallBlocks;
        const string texts = "mut xs = List<int>()\nfor i in 0..<100 {\n    xs.Add(i)\n}\n\nprint(string.Join(\",\", xs).Bytes.Count)\n";
        Ran(BuildAndRun(texts, refusing, "join-texts-refused"), 1, [], "error: allocation-failed (1600 bytes)\n");
        Ran(BuildAndRun(texts, toolchain.Runtime, "join-texts-granted"), 0, ["289"], ""); // the positive control
        const string strings = "mut xs = List<string>()\nfor i in 0..<100 {\n    xs.Add(\"a\")\n}\n\nprint(string.Join(\",\", xs).Bytes.Count)\n";
        Ran(BuildAndRun(strings, refusing, "join-strings-granted"), 0, ["199"], "");
    }

    /// <summary>
    /// The runtime of this machine's target whose `Allocate` refuses 1000 to 4095 bytes in its text, below the 4096 bytes `print` takes,
    /// while the lists' own blocks do not go through it.
    /// </summary>
    private static CheckedRuntime RefusingSmallBlocks => Edited(("runtime/common/runtime.kz", "    byte* p = std.Allocate(size)\n", "    mut byte* p = null\n    if size < 1000 || size >= 4096 {\n        p = std.Allocate(size)\n    }\n\n"));

    [Fact]
    public void A_cut_or_a_decode_whose_block_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // M9, X2: a cut of 2 bytes copied out of a string's block, `StringOf` refusing 2 bytes or more in the library's text, in the
        // fenced runtime, reports 16 bytes of counts and the cut's; H7: a decode of two digits asks for a list of one byte
        const string cut = "x = \"a\"\nprint(\"{x}bcd\".Substring(1, 2))\n";
        Ran(BuildAndRun(cut, Edited([.. Fence, ("std/std.kz", "    byte* text = StringBlock(count)\n", "    mut byte* text = null\n    if count < 2 {\n        text = StringBlock(count)\n    }\n\n")]), "cut-refused"), 1, [], "error: allocation-failed (18 bytes)\n");
        Ran(BuildAndRun(cut, Fenced, "cut-granted"), 0, ["bc"], ""); // the positive control
        const string decode = "match Hex.Decode(\"00\") {\n    Bytes b => print(b)\n    HexError e => print(e)\n}\n";
        Ran(BuildAndRun(decode, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "decode-refused"), 1, [], "error: allocation-failed (17 bytes)\n");
        Ran(BuildAndRun(decode, toolchain.Runtime, "decode-granted"), 0, ["[0]"], ""); // the positive control
    }

    [Fact]
    public void A_sort_whose_buffer_or_keys_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // M9, X8: the runtime's `Allocate` refusing 1000 to 4095 bytes (<see cref="RefusingSmallBlocks"/>): a stable sort of 300 `long`
        // items asks for a buffer of half of them, 150 of 8 bytes, and one of 301 items for half of them rounded up, 151 of 8 bytes, as
        // Rust's stable sort asks for at the least; `SortBy` of 100 items with `long` keys for the pairs of a key and a position, 100 of
        // 16 bytes; an unstable sort asks for nothing
        var refusing = RefusingSmallBlocks;
        const string list = "mut xs = List<long>()\nfor i in 0..<300 {\n    xs.Add(long(299 - i))\n}\n\n";
        const string sort = list + "xs.Sort()\nprint(xs[0])\n";
        Ran(BuildAndRun(sort, refusing, "sort-refused"), 1, [], "error: allocation-failed (1200 bytes)\n");
        Ran(BuildAndRun(sort, toolchain.Runtime, "sort-granted"), 0, ["0"], ""); // the positive control
        const string odd = "mut xs = List<long>()\nfor i in 0..<301 {\n    xs.Add(long(300 - i))\n}\n\nxs.Sort()\nprint(xs[0])\n";
        Ran(BuildAndRun(odd, refusing, "sort-refused-odd"), 1, [], "error: allocation-failed (1208 bytes)\n");
        Ran(BuildAndRun(odd, toolchain.Runtime, "sort-granted-odd"), 0, ["0"], "");
        const string keys = list + "mut ys = xs[0..<100]\nys.SortBy(y => y * 2)\nprint(ys[0])\n";
        Ran(BuildAndRun(keys, refusing, "keys-refused"), 1, [], "error: allocation-failed (1600 bytes)\n");
        Ran(BuildAndRun(keys, toolchain.Runtime, "keys-granted"), 0, ["200"], "");
        Ran(BuildAndRun(list + "xs.SortUnstable()\nprint(xs[0])\n", refusing, "unstable-granted"), 0, ["0"], "");
    }

    [Fact]
    public void A_Split_Replace_or_Join_past_the_limit_reports_the_bytes_it_counted()
    {
        // M9, T15: the limit of the largest `int` lowered to 3 in the library's text, `Replace`'s and `Join`'s on the bytes of the
        // result, so that results of 4 and 5 bytes ask past it, and `ListAllocate`'s on the items, so that a `Split` into 4 pieces asks
        // past it with 4 pieces of 16 bytes; a change of case keeps the string's length, which a string holds below the limit
        const string replace = "print(\"abc\".Replace(\"b\", \"xy\"))\n";
        const string join = "print(string.Join(\",\", [\"ab\", \"cd\"]))\n";
        const string split = "print(\"a,b,c,d\".Split(\",\"))\n";
        const string limit = "if size > 2147483647 {\n        asked = size\n        return null\n    }\n\n    if size == 0 {\n        ";
        Ran(BuildAndRun(replace, Edited(("std/std.kz", limit + "made = 0", limit.Replace("2147483647", "3") + "made = 0")), "replace-limit"), 1, [], "error: allocation-failed (4 bytes)\n");
        Ran(BuildAndRun(join, Edited(("std/std.kz", limit + "return null", limit.Replace("2147483647", "3") + "return null")), "join-limit"), 1, [], "error: allocation-failed (5 bytes)\n");
        Ran(BuildAndRun(split, Edited(("std/std.kz", "if count > 2147483647 {\n        asked = count * item", "if count > 3 {\n        asked = count * item")), "split-limit"), 1, [], "error: allocation-failed (64 bytes)\n");
        Ran(BuildAndRun(replace + join + split, toolchain.Runtime, "split-replace-join-below-limit"), 0, ["axyc", "ab,cd", "[a, b, c, d]"], ""); // the positive control
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
    [InlineData("-1", "minus")]
    [InlineData("2", "count")]
    [InlineData("100000", "far")]
    [InlineData("-2147483648", "smallest")]
    public void A_write_at_an_index_below_0_or_at_Count_or_beyond_raises_index_out_of_range_at_its_line(string index, string name)
    {
        // T14, M9 (section 4, the owner, 2026-10-30, "As C# and Rust"): the write before it is the positive control, and the value is
        // made before the index is read (M4), so its `print` runs
        var text = $"int Seven() {{\n    print(7)\n    return 7\n}}\n\nmut xs = List<int>()\nxs.Add(4)\nxs.Add(5)\nxs[1] = 6\nprint(xs[1])\nxs[{index}] = Seven()\nprint(xs)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-write-" + name), 1, ["6", "7"], "error: index-out-of-range at program.kz:11\n");
    }

    [Theory]
    [InlineData("Insert", "-1, 9", "minus")]
    [InlineData("Insert", "3, 9", "past")]
    [InlineData("Insert", "-2147483648, 9", "smallest")]
    [InlineData("RemoveAt", "-1", "minus")]
    [InlineData("RemoveAt", "2", "count")]
    [InlineData("RemoveAt", "2147483647", "largest")]
    public void An_Insert_or_a_RemoveAt_outside_the_list_raises_index_out_of_range_at_its_line(string member, string arguments, string name)
    {
        // X11: the call before it is the positive control, at the edge inside the list: an `Insert` at `Count`, a `RemoveAt` of the last item
        var inside = member == "Insert" ? "xs.Insert(2, 6)\nxs.RemoveAt(2)\n" : "xs.Add(6)\nprint(xs.RemoveAt(2))\n";
        var text = $"mut xs = List<int>()\nxs.Add(4)\nxs.Add(5)\n{inside}print(xs)\nxs.{member}({arguments})\nprint(xs)\n";
        var printed = member == "Insert" ? new[] { "[4, 5]" } : ["6", "[4, 5]"];
        Ran(BuildAndRun(text, toolchain.Runtime, $"list-{member.ToLowerInvariant()}-{name}"), 1, printed, "error: index-out-of-range at program.kz:7\n");
    }

    [Fact]
    public void An_Insert_past_the_limit_reports_the_bytes_of_the_items_after_it()
    {
        // X11, M9: as `Add`, with the limit of the largest `int` lowered to 3 in the library's text, so that a fourth `Insert` asks for
        // 4 times the 16 bytes of a string
        const string text = "mut xs = List<string>()\nxs.Insert(0, \"a\")\nxs.Insert(0, \"b\")\nxs.Insert(1, \"c\")\nxs.Insert(1, \"d\")\nprint(xs)\n";
        Ran(BuildAndRun(text, Edited(("std/std.kz", "2147483647", "3")), "list-insert-limit"), 1, [], "error: allocation-failed (64 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-insert-below-limit"), 0, ["[b, d, c, a]"], ""); // the positive control
    }

    [Fact]
    public void A_write_into_a_list_without_items_raises_index_out_of_range()
    {
        Ran(BuildAndRun("mut xs = List<string>()\nxs[0] = \"a\"\nprint(xs)\n", toolchain.Runtime, "list-write-empty"), 1, [], "error: index-out-of-range at program.kz:2\n");
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

    /// <summary>
    /// What .NET's decoder reads of <paramref name="bytes"/>: "ok", or the count of the bytes in front of the first sequence it cannot
    /// read, which is Rust's `valid_up_to`, what `string.FromUtf8` puts into `InvalidUtf8`.
    /// </summary>
    private static string Valid(byte[] bytes) =>
        System.Text.Unicode.Utf8.ToUtf16(bytes, new char[bytes.Length], out var read, out _, replaceInvalidSequences: false)
            == System.Buffers.OperationStatus.Done
            ? "ok"
            : read.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void FromUtf8_finds_the_first_sequence_that_is_not_UTF_8_wherever_it_stands_among_the_words_its_ASCII_skip_reads()
    {
        // T15: the runtime reads the first 16 bytes of a run of ASCII byte by byte and skips the rest as Rust 1.82's
        // `run_utf8_validation` does, two words of 8 bytes at a time from an address that is a multiple of 8, then byte by byte.
        // Each case is 0 to 7 bytes of ASCII and a letter of two bytes, which put the run that follows at every distance from such
        // an address, a run of 0 to 63 bytes of ASCII, then a byte that begins no sequence (128, 255), a sequence cut short at the
        // end, a letter of three bytes, or nothing, and 20 bytes of ASCII after the first two and the letter: so the first byte of
        // 128 or more stands among the first 16 bytes, in the first word of a pair, in the second, past the last pair and among the
        // bytes read one at a time after it. What each case gives is what .NET's decoder reads of the same bytes, the count of the
        // bytes in front of the first sequence it cannot read, which is Rust's `valid_up_to` (<see cref="Valid"/>).

        // the positive control: the decoder reads as Rust 1.82's `str::from_utf8` read these on 2026-10-29 (the table above)
        Assert.Equal("1", Valid([0x61, 0xE2, 0x82, 0x61]));
        Assert.Equal("2", Valid([0x68, 0x69, 0xC3, 0x28, 0x21]));
        Assert.Equal("5", Valid([0xE2, 0x82, 0xAC, 0xC3, 0xA4, 0x80, 0xBF, 0x7A]));
        Assert.Equal("0", Valid([0xED, 0xA0, 0x80]));
        Assert.Equal("0", Valid([0xF0, 0x90, 0x80]));
        Assert.Equal("ok", Valid([0xF4, 0x8F, 0xBF, 0xBF]));

        byte[][] ends = [[128], [255], [226, 130], [226, 130, 172], []];
        var lines = new List<string>();
        for (var lead = 0; lead < 8; lead++)
        {
            for (var run = 0; run < 64; run++)
            {
                foreach (var end in ends)
                {
                    byte[] after = end.Length is 1 or 3 ? [.. Enumerable.Repeat((byte)67, 20)] : [];
                    lines.Add(Valid([.. Enumerable.Repeat((byte)65, lead), 195, 169, .. Enumerable.Repeat((byte)66, run), .. end, .. after]));
                }
            }
        }

        // the floor: 2560 cases, of which the 1536 that end in a byte that begins no sequence or in a cut one give the error
        Assert.Equal(2560, lines.Count);
        Assert.Equal(1024, lines.Count(l => l == "ok"));

        const string text = """
            void Show(Bytes b) {
                match string.FromUtf8(b) {
                    InvalidUtf8 e => print(e.At)
                    else => print("ok")
                }
            }

            for lead in 0..<8 {
                for run in 0..<64 {
                    for end in 0..<5 {
                        mut b = Bytes()
                        for i in 0..<lead {
                            b.Add(65)
                        }

                        b.Add(195)
                        b.Add(169)
                        for j in 0..<run {
                            b.Add(66)
                        }

                        if end == 0 {
                            b.Add(128)
                        } else if end == 1 {
                            b.Add(255)
                        } else if end == 2 {
                            b.Add(226)
                            b.Add(130)
                        } else if end == 3 {
                            b.Add(226)
                            b.Add(130)
                            b.Add(172)
                        }

                        if end == 0 || end == 1 || end == 3 {
                            for k in 0..<20 {
                                b.Add(67)
                            }
                        }

                        Show(b)
                    }
                }
            }

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "utf8-words"), 0, [.. lines], "");
        Ran(BuildAndRun(text, Fenced, "utf8-words-fenced"), 0, [.. lines], "");

        // no pair of words is read past the bytes: under the fence a part of 4064 bytes is a block that ends where its page ends,
        // the system maps it right below the block made before it (on Linux), which the fence makes inaccessible when it is freed,
        // and after the letter of two bytes and 16 bytes of ASCII the words start 8 bytes past an address that is a multiple of 16,
        // so that a pair read where fewer than 16 bytes are left reads into the freed page
        const string edge = """
            mut source = Bytes()
            source.Add(195)
            source.Add(169)
            while source.Count < 4064 {
                source.Add(65)
            }

            mut freed = source[0..<4064]
            held = source[0..<4064]
            freed = Bytes()
            print(freed.Count)
            match string.FromUtf8(held) {
                string s => print(s.Bytes.Count)
                InvalidUtf8 e => print(e.At)
            }

            """;
        Ran(BuildAndRun(edge, Fenced, "utf8-words-edge-fenced"), 0, ["0", "4064"], "");
    }

    [Fact]
    public void FromUtf8_reads_a_letter_with_three_bytes_after_it_in_place_as_it_reads_one_nearer_the_end()
    {
        // T15: a letter with three bytes after it is read in place, its bytes held to the ranges `Utf8Sequence` holds them to, and a
        // letter nearer the end through `Utf8Sequence`. Each case is a first byte of 128 to 255, a second byte at an end of the ranges
        // a second byte takes (127, 128, 143, 144, 159, 160, 191, 192), a third byte that continues a sequence or does not (128, 127,
        // 192) and a fourth (191, 192), with two bytes of ASCII after them, so that the letter has three bytes after it; then the same
        // first three bytes, the first two and the first alone at the end of the bytes, nearer it. What each case gives is what .NET's
        // decoder reads of the same bytes (<see cref="Valid"/>).
        byte[] seconds = [127, 128, 143, 144, 159, 160, 191, 192];
        byte[] thirds = [128, 127, 192];
        byte[] fourths = [191, 192];
        var cases = new List<byte[]>();
        for (var first = 128; first < 256; first++)
        {
            foreach (var second in seconds)
            {
                foreach (var third in thirds)
                {
                    foreach (var fourth in fourths)
                    {
                        cases.Add([(byte)first, second, third, fourth, 65, 65]);
                    }
                }
            }
        }

        for (var first = 128; first < 256; first++)
        {
            foreach (var second in seconds)
            {
                foreach (var third in thirds)
                {
                    cases.Add([(byte)first, second, third]);
                }

                cases.Add([(byte)first, second]);
            }

            cases.Add([(byte)first]);
        }

        string[] lines = [.. cases.Select(Valid)];

        // the floor: 10368 cases. With three bytes after the letter, 24 are UTF-8 whole, the letters of four bytes whose third byte
        // continues them and whose fourth is 191; nearer the end, 450, the letters of two bytes that end the bytes or stand before
        // 127, and of three bytes that end them
        Assert.Equal(10368, lines.Length);
        Assert.Equal(24, lines[..6144].Count(l => l == "ok"));
        Assert.Equal(450, lines[6144..].Count(l => l == "ok"));

        const string text = """
            void Show(Bytes b) {
                match string.FromUtf8(b) {
                    InvalidUtf8 e => print(e.At)
                    else => print("ok")
                }
            }

            mut seconds = Bytes()
            seconds.Add(127)
            seconds.Add(128)
            seconds.Add(143)
            seconds.Add(144)
            seconds.Add(159)
            seconds.Add(160)
            seconds.Add(191)
            seconds.Add(192)
            mut thirds = Bytes()
            thirds.Add(128)
            thirds.Add(127)
            thirds.Add(192)
            mut fourths = Bytes()
            fourths.Add(191)
            fourths.Add(192)
            for first in 128..<256 {
                for s in 0..<seconds.Count {
                    for t in 0..<thirds.Count {
                        for u in 0..<fourths.Count {
                            mut b = Bytes()
                            b.Add(byte(first))
                            b.Add(seconds[s])
                            b.Add(thirds[t])
                            b.Add(fourths[u])
                            b.Add(65)
                            b.Add(65)
                            Show(b)
                        }
                    }
                }
            }

            for first in 128..<256 {
                for s in 0..<seconds.Count {
                    for t in 0..<thirds.Count {
                        mut b = Bytes()
                        b.Add(byte(first))
                        b.Add(seconds[s])
                        b.Add(thirds[t])
                        Show(b)
                    }

                    mut c = Bytes()
                    c.Add(byte(first))
                    c.Add(seconds[s])
                    Show(c)
                }

                mut d = Bytes()
                d.Add(byte(first))
                Show(d)
            }

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "utf8-letters"), 0, lines, "");
        Ran(BuildAndRun(text, Fenced, "utf8-letters-fenced"), 0, lines, "");

        // no letter is read in place where fewer than three bytes follow it: under the fence a part of 4064 bytes ends where its
        // page ends, below the page of the part freed before it, as above, and its last three bytes are a letter of three bytes,
        // whose read in place would read the byte past the part
        const string edge = """
            mut source = Bytes()
            while source.Count < 4061 {
                source.Add(65)
            }

            source.Add(226)
            source.Add(130)
            source.Add(172)
            mut freed = source[0..<4064]
            held = source[0..<4064]
            freed = Bytes()
            print(freed.Count)
            match string.FromUtf8(held) {
                string s => print(s.Bytes.Count)
                InvalidUtf8 e => print(e.At)
            }

            """;
        Ran(BuildAndRun(edge, Fenced, "utf8-letters-edge-fenced"), 0, ["0", "4064"], "");
    }

    /// <summary>
    /// The runtime of this machine's target whose <c>Utf8Invalid</c> ends the process with the exit code 70 where it would read a
    /// word at an address that is no multiple of 8. The read through a pointer is emitted with no alignment written, which LLVM's
    /// language reference reads as the ABI alignment of the type ("An omitted `align` argument means that the operation has the
    /// ABI alignment for the target", LLVM 23, the `load` instruction), 8 for a <c>ulong</c>, and "Overestimating the alignment
    /// results in undefined behavior"; a word read at any address reads right on x86-64, so only such a check shows one.
    /// </summary>
    private static CheckedRuntime WordsAligned => Edited(("runtime/common/runtime.kz", "                            ulong u = *Ptr<ulong>(bytes + at)\n", """
                                    if (long(bytes + at) & 7) != 0 {
                                        WriteError("error: a word read at an address that is no multiple of 8\n")
                                        sys.Exit(70)
                                    }

                                    ulong u = *Ptr<ulong>(bytes + at)

        """));

    [Fact]
    public void FromUtf8_reads_a_word_only_at_an_address_that_is_a_multiple_of_8()
    {
        // T15: the letters of two, three and four bytes stand at every position from 0 to 15, each followed by a run of 1 to 40 bytes
        // of ASCII, so that the walk finds its address that is a multiple of 8 again after each letter; the runtime that ends the
        // process at a word read anywhere else (<see cref="WordsAligned"/>) prints what the runtime does, the positive control
        var lines = new List<string>();
        for (var lead = 0; lead < 16; lead++)
        {
            for (var run = 1; run <= 40; run++)
            {
                lines.Add((lead + 9 + 3 * run).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(640, lines.Count);
        const string text = """
            long Count(Bytes b) {
                match string.FromUtf8(b) {
                    string s => return s.Bytes.Count
                    InvalidUtf8 => return -1
                }
            }

            for lead in 0..<16 {
                for run in 1..40 {
                    mut b = Bytes()
                    for i in 0..<lead {
                        b.Add(65)
                    }

                    for k in 0..<3 {
                        if k == 0 {
                            b.Add(195)
                            b.Add(169)
                        } else if k == 1 {
                            b.Add(226)
                            b.Add(130)
                            b.Add(172)
                        } else {
                            b.Add(240)
                            b.Add(159)
                            b.Add(152)
                            b.Add(128)
                        }

                        for j in 0..<run {
                            b.Add(66)
                        }
                    }

                    print(Count(b))
                }
            }

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "utf8-aligned-plain"), 0, [.. lines], "");
        Ran(BuildAndRun(text, WordsAligned, "utf8-aligned"), 0, [.. lines], "");
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
    public void An_AddRange_past_the_limit_reports_the_bytes_of_the_items_after_it()
    {
        // M9, X9: the limit of the largest `int` lowered to 3 in the library's `ListReserve`, so that `AddRange` of three items to a list
        // of one asks past it with the bytes of 4 `long`s, as an `Add` past the limit does; a block the system refuses ends the same way
        // through the emitter's `Refused`, as every growth's does
        const string text = "mut xs = List<long>()\nxs.Add(1)\nxs.AddRange([2, 3, 4])\nprint(xs.Count)\n";
        Ran(BuildAndRun(text, Edited(("std/std.kz", "if count + more > 2147483647 {\n        asked = (count + more) * item", "if count + more > 3 {\n        asked = (count + more) * item")), "add-range-limit"), 1, [], "error: allocation-failed (32 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "add-range-below-limit"), 0, ["4"], ""); // the positive control
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
    public void A_list_literal_whose_block_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // M11, M9: the one block of a literal of three `long` items, 16 bytes of counts and room for the three
        const string text = "List<long> xs = [1, 2, 3]\nprint(xs)\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "literal-refused"), 1, [], "error: allocation-failed (40 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "literal-granted"), 0, ["[1, 2, 3]"], ""); // the positive control
    }

    [Fact]
    public void A_map_block_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // the first block of a `Map<long, long>`: 48 bytes of counts and keys, 16 of the control bytes of its 8 slots, padded, and 8
        // entries of a hash, a key and a value
        const string text = "mut m = Map<long, long>()\nm[1] = 1\nprint(m[1] ?? 0)\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "map-refused"), 1, [], "error: allocation-failed (256 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "map-granted"), 0, ["1"], ""); // the positive control
    }

    [Fact]
    public void A_first_map_on_a_system_that_gives_no_randomness_ends_the_program_with_random_failed()
    {
        // M9 (the owner, 2026-10-30, "End with an error, as Rust"): the target's `Random` reports that it filled no bytes, as a
        // Linux whose `getrandom` and `/dev/urandom` both fail or a Windows without `ProcessPrng` would, and the first map ends the program before its block
        var refused = Edited((TargetFile, "pub bool Random(byte* bytes, long count) {", """
            pub bool Random(byte* bytes, long count) {
                return false
            }

            bool Given(byte* bytes, long count) {
            """));
        const string text = "mut m = Map<long, long>()\nm[1] = 1\nprint(m[1] ?? 0)\n";
        Ran(BuildAndRun(text, refused, "map-no-random"), 1, [], "error: random-failed\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "map-random"), 0, ["1"], ""); // the positive control
        Ran(BuildAndRun("print(1)\n", refused, "no-map-no-random"), 0, ["1"], ""); // a program without a map asks for no randomness
    }

    [Fact]
    public void On_linux_a_first_map_where_the_system_refuses_getrandom_takes_its_keys_from_dev_urandom()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows' `Random` calls `ProcessPrng` alone, with no file to fall back to
        }

        // M9 (the owner, 2026-10-30, "Rust's full path"): each `getrandom` of the runtime is a system call of a number the kernel does
        // not have, 100000, which it refuses with ENOSYS (seen on x86_64), as a kernel before 3.17 refuses `getrandom`; the keys come
        // from `/dev/urandom`
        (string Path, string Old, string New)[] enosys =
        [
            (TargetFile, "    long getrandom(byte* bytes, long count, uint options)\n", "    long getrandom(byte* bytes, long count, uint options)\n    long syscall(long number, ...)\n"),
            (TargetFile, "getrandom(bytes, count, 4)", "syscall(100000, bytes, count, 4)"),
            (TargetFile, "getrandom(bytes, count, 1)", "syscall(100000, bytes, count, 1)"),
        ];
        const string text = "mut m = Map<long, long>()\nm[1] = 1\nprint(m[1] ?? 0)\n";
        Ran(BuildAndRun(text, Edited(enosys), "map-urandom"), 0, ["1"], "");

        // the control: the same runtime with a file that is not there in the place of `/dev/urandom` ends where Rust panics
        Ran(BuildAndRun(text, Edited([.. enosys, (TargetFile, @"""/dev/urandom\0""", @"""/dev/no-urandom\0""")]), "map-no-urandom"), 1, [], "error: random-failed\n");
    }

    [Fact]
    public void A_map_finds_each_key_it_holds_and_no_other_through_its_growth()
    {
        // 20000 entries grow the block from 8 slots to 32768, through groups of 8 control bytes and probes past full groups; half the
        // keys asked for are missing, and each probe for one ends at a group with a slot without an entry
        const string text = "mut m = Map<int, int>()\nfor i in 0..<20000 {\n    m[i * 2] = i\n}\nmut long found = 0\nmut missing = 0\nfor i in 0..<40000 {\n    v = m[i] ?? -1\n    if v < 0 {\n        missing = missing + 1\n    } else {\n        found = found + long(v)\n    }\n}\nprint(found)\nprint(missing)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "map-many"), 0, ["199990000", "20000"], "");
        Ran(BuildAndRun(text, Fenced, "map-many-fenced"), 0, ["199990000", "20000"], "");
    }

    [Fact]
    public void A_slot_holds_an_entry_by_its_control_byte_not_by_the_bytes_a_block_held_before()
    {
        // a new block's slots are as the allocator gave them, as hashbrown leaves its buckets: the second map takes the block the
        // first gave back, whose slots still hold its five entries, and holds the one entry written into it
        const string text = "void Fill() {\n    mut m = Map<int, int>()\n    for i in 0..<5 {\n        m[i] = i + 10\n    }\n}\n\nFill()\nmut n = Map<int, int>()\nn[7] = 1\nprint(n)\nprint(n[3] ?? -1)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "map-used-block"), 0, ["[7: 1]", "-1"], "");

        // the positive control: a new block that keeps the control bytes the first map left in it holds the first map's entries too
        var stale = BuildAndRun(text, Edited(("std/std.kz", "                p[48 + i] = 255\n", "")), "map-used-block-stale");
        Assert.False(stale.Output.SequenceEqual(["[7: 1]", "-1"]), string.Join(" | ", stale.Output));
    }

    [Fact]
    public void A_key_is_hashed_by_SipHash_1_3_under_keys_the_system_gives_each_process()
    {
        // the hash Rust's `HashMap` takes by default: the two vectors under keys of 0 are CPython's `siphash13` of the same bytes, the
        // third is under the reference implementation's keys, from the same code in Python, which gives the reference's SipHash-2-4
        // vectors with two and four rounds
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
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

    [Fact]
    public void A_key_of_every_count_of_bytes_from_0_to_24_has_the_SipHash_1_3_of_the_reference()
    {
        // SipHash13 reads each whole word of 8 bytes in one read and the bytes after the last one four, two and one at a time, as Rust's
        // SipHasher13 reads them (`load_int_le` and `u8to64_le`): the bytes (i * 37 + 200) % 256, half of them 128 or more, of every count
        // from 0 to 24, under keys of 0 and under the reference implementation's keys, against SipHash-1-3 in Python, which gives
        // CPython's `hash` of the same bytes under keys of 0 (`PYTHONHASHSEED=0`) and the three vectors of the test above
        ulong[] zero =
        [
            15130871412783076140UL, 12449551661063549815UL, 18410336655117930453UL, 14036337907613256389UL, 8188850183964244334UL,
            3177195191231020720UL, 11112996053577049341UL, 12494225358345731408UL, 3712373850883526462UL, 12892607951876773325UL,
            12632183819240295938UL, 2608151670788947609UL, 15991896908241025576UL, 3217457963781418757UL, 5175628169687003164UL,
            324940683844405139UL, 4078673874090109879UL, 15226221049046578814UL, 11307112605383110513UL, 9713625952335163347UL,
            15212928445191759854UL, 10622023044703524569UL, 13280180098544721076UL, 4160088133271530178UL, 14255749407410980665UL,
        ];
        ulong[] reference =
        [
            12370263754033579228UL, 7382509910418742475UL, 2494721049683813306UL, 11620750396190661065UL, 5010687366671536750UL,
            14218513396531960822UL, 11621134531688062575UL, 17244704406219703104UL, 12416751392242888934UL, 421987831323517835UL,
            10723776196378657725UL, 13352898446040941761UL, 6124211783992107166UL, 18243523408008742371UL, 119414829251180199UL,
            5538372403305982465UL, 1478129454238627121UL, 2867072306180843416UL, 1984877967642593061UL, 8806513419543075307UL,
            16461867054457208225UL, 6477134911588130379UL, 7484306140281614234UL, 7636386497915317409UL, 11166856279661420457UL,
        ];
        const string start = "pub void Start(int count, byte* values) {\n    sys.Start(count, values)\n";
        const string hash = "    byte* p = Allocate(24)\n    raw {\n        for i in 0..<24 {\n            p[i] = byte((i * 37 + 200) % 256)\n        }\n    }\n    for n in 0..<25 {\n        WriteNumber(std.SipHash13(0, 0, p, long(n)))\n        WriteError(\" \")\n        WriteNumber(std.SipHash13(506097522914230528UL, 1084818905618843912UL, p, long(n)))\n        WriteError(\"\\n\")\n    }\n";
        var runtime = Edited(("std/std.kz", "\nulong SipHash13(", "\npub ulong SipHash13("), ("runtime/common/runtime.kz", start, start + hash));
        var expected = string.Concat(zero.Zip(reference, (z, r) => $"{z} {r}\n"));
        Ran(BuildAndRun("print(1)\n", runtime, "map-siphash-counts"), 0, ["1"], expected);
    }

    [Fact]
    public void Two_strings_are_equal_where_every_byte_is_compared_a_word_at_a_time_and_the_bytes_after()
    {
        // T18: the runtime's `Same` compares 8 bytes at a time and the bytes after the last 8 one at a time: a string of every count from 0
        // to 20 against the same count of bytes with one byte changed at each place, which never equals it (a count of 1 each), and against
        // an interpolation of the same bytes, which does (a count of 1000 each); then the same of 0 to 20 letters of two bytes each, every
        // byte 128 or more, against one letter changed at each place, which changes its second byte alone
        const string text = "s = \"abcdefghijklmnopqrst\"\nmut long n = 0\nfor count in 0..<21 {\n    a = s.Substring(0, count)\n    for k in 0..<count {\n        b = \"{s.Substring(0, k)}#{s.Substring(k + 1, count - k - 1)}\"\n        if a == b {\n            n = n + 1\n        }\n    }\n\n    if a == \"{s.Substring(0, count)}\" {\n        n = n + 1000\n    }\n}\nprint(n)\nt = \"àáâãäåæçèéêëìíîïðñòó\"\nmut long m = 0\nfor count in 0..<21 {\n    a = t.Substring(0, 2 * count)\n    for k in 0..<count {\n        b = \"{t.Substring(0, 2 * k)}ÿ{t.Substring(2 * k + 2, 2 * (count - k - 1))}\"\n        if a == b {\n            m = m + 1\n        }\n    }\n\n    if a == \"{t.Substring(0, 2 * count)}\" {\n        m = m + 1000\n    }\n}\nprint(m)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "string-same-words"), 0, ["21000", "21000"], "");
    }

    [Theory]
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
