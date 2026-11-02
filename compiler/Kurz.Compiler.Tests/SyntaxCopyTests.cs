using System.Collections;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// T33: the copy of a function's syntax that each copy of a generic function that reads its type is checked on. The checker's model
/// keys a node by its reference, so a node the copy shares with the parser's tree, or with another copy, would carry the types of
/// whichever copy was checked last.
/// </summary>
public sealed class SyntaxCopyTests
{
    /// <summary>A function whose body holds every kind of expression, statement, pattern and type syntax a body can hold.</summary>
    private const string Program = """
        data Point(int X, int Y)

        int Every<T>(T value, mut List<int> xs, int step = 1) {
            a = 1
            mut int b = 2
            b = a + -b
            xs[0] = 3
            c = 1.5
            d = 5s
            e = 'x'
            g = true
            h = "text {a} and {b}"
            n = Point(1, 2)
            m = n with { X = 4 }
            k = n.X
            lambda = (int x) => x * 2
            other = (int x) => {
                return x
            }
            r = 0..3
            s = 0..<3
            p = (a)
            int? maybe = null
            List<int?> list = [1, null]
            Map<string, int | bool> map = Map<string, int | bool>()
            (int) => int f = lambda
            if g {
                print(1)
            } else if b > 0 {
                print(2)
            } else {
                print(3)
            }

            while b < 10 {
                b = b + 1
                if b == 5 {
                    continue
                }

                break
            }

            for i in xs {
                print(i)
            }

            for key, entry in map {
                print(key)
            }

            match T.Kind {
                TypeKind.Int => print(value)
                else => print(0)
            }

            label = match maybe {
                int v => v
                null => 0
            }

            raw {
                print(1)
            }

            print("{h} {m} {k} {other(1)} {r} {s} {p} {list} {f(1)} {label} {c} {d} {e} {step}")
            if a > 100 {
                throw 1
            }

            return a
        }
        """;

    private static FunctionDeclaration Parse()
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("copy.kz", Program), diagnostics);
        Assert.Empty(diagnostics.Items);
        return tree.Statements.OfType<FunctionDeclaration>().Single();
    }

    [Fact]
    public void A_copy_shares_no_node_with_its_original()
    {
        var original = Parse();
        var copy = SyntaxCopy.Copy(original);
        var before = Nodes(original);
        var after = Nodes(copy);

        // the walk reached the whole body, every kind of node the program holds, and the copy has the same shape
        Assert.True(before.Count > 240, $"the walk reached {before.Count} nodes");
        var kinds = before.Select(n => n.GetType().Name).Distinct().Count();
        Assert.True(kinds > 45, $"the walk reached {kinds} kinds of node");
        Assert.Equal(before.Select(n => n.GetType()), after.Select(n => n.GetType()));
        Assert.Equal(original.ToString(), copy.ToString(), StringComparer.Ordinal);
        var shared = before.Intersect(after, ReferenceEqualityComparer.Instance).ToList();
        Assert.True(shared.Count == 0, $"shared: {string.Join(", ", shared.Select(n => n!.GetType().Name).Distinct())}");
    }

    [Fact]
    public void The_walk_shows_every_expression_once()
    {
        var original = Parse();
        var seen = new List<Expression>();
        SyntaxCopy.Walk(original, seen.Add);
        var expressions = Nodes(original).OfType<Expression>().ToList();
        Assert.Equal(expressions.Count, seen.Count);
        Assert.Empty(expressions.Except(seen, ReferenceEqualityComparer.Instance));
    }

    /// <summary>Every syntax record under the declaration, in a fixed order: the nodes, and the arms, arguments, parameters and holes that hold them.</summary>
    private static List<object> Nodes(object root)
    {
        var found = new List<object>();
        Visit(root, found);
        return found;
    }

    private static void Visit(object? value, List<object> found)
    {
        switch (value)
        {
            case null or string or Token or IntegerLiteralValue or RealLiteralValue or UnitLiteralValue or TextSegment:
                return; // values, which no model keys
            case IEnumerable items:
                foreach (var item in items)
                {
                    Visit(item, found);
                }

                return;
        }

        var type = value.GetType();
        if (type.Namespace != typeof(Node).Namespace || type.IsEnum || type.IsPrimitive)
        {
            return;
        }

        found.Add(value);
        foreach (var property in type.GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract"))
        {
            Visit(property.GetValue(value), found);
        }
    }
}
