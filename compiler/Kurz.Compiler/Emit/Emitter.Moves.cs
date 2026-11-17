using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// U12, section 6: <c>move</c> into a parameter on one thread copies nothing and changes no count (the owner, 2026-11-15, #519, comment
/// 6895076937, which gave the moved count to "the message's or the parameter's" place): the value of a variable written <c>move</c>
/// leaves its place, which is emptied, and the count it held goes with it to the parameter written <c>move</c>, which the function owns
/// and lets go of at its end; a value whose place is not emptied, inline data, a fat class reference and a function value, is counted
/// once more (section 6, assumed). Into a message or a <c>spawn</c>, the actor's heap being its own, the moved value is copied into that
/// heap and let go of, which frees it, as an argument with no word crosses (section 6, "What a <c>move</c> ends, and where it stands",
/// assumed on comment 6902210646, Q5, which narrows comment 6895076937 to a parameter; <see cref="Arguments"/>). A place a
/// <c>move</c> may have emptied is let go of through a test for the empty value, as rustc keeps a drop flag (comment 6897444564), and a
/// loop that takes its list empties the item's place in the list where the item's place has an empty value (comment 6897778812, Q1
/// (b)), so that the list lets go of every item it still holds and of no moved one, and counts any other moved item once more.
/// </summary>
public sealed partial class Emitter
{
    // the items and the counter of each loop that takes its list, by the loop's variable
    private readonly Dictionary<VariableSymbol, (string Items, string Counter, KurzType Element)> takenItems = new(ReferenceEqualityComparer.Instance);

    // the item types of the lists a loop of the program takes, by their mangled names, read from the models before any helper is built
    private HashSet<string>? takenElements;

    /// <summary>Whether a parameter of the call's slot is written <c>move</c>, so that the callee owns its argument.</summary>
    private bool TakesSlot(CallExpression c, CallInfo info, int slot) => info.Kind == CallKind.Value
        ? TypeOf(c.Callee) is FunctionType f && f.MovesAt(slot)
        : info.Function is { } function && slot < function.Parameters.Count && function.Parameters[slot].Move;

    /// <summary>
    /// The value of an argument written <c>move</c>, which the place it goes to owns: a variable's, whose place is emptied with no count
    /// changed; the item of a loop that takes its list, whose place in the list is emptied; any other value as a place takes it, a
    /// temporary taken over and a read counted once more.
    /// </summary>
    private string MovedValue(Expression e)
    {
        if (Unwrap(e) is NameExpression n && Model.IsVariable(n) && Model.VariableOf(n) is var variable)
        {
            var type = TypeOf(e);
            if (takenItems.TryGetValue(variable, out var taken))
            {
                var item = fn.Value($"load {Ir(type)}, ptr {Place(variable, n.Line)}");
                var at = fn.Value($"sext i32 {fn.Value($"load i32, ptr {taken.Counter}")} to i64");
                fn.Emit($"store {Ir(taken.Element)} {EmptyValue(taken.Element)}, ptr {fn.Value($"getelementptr {Ir(taken.Element)}, ptr {taken.Items}, i64 {at}")}");
                return item;
            }

            if (Counted(type) && EmptyValue(variable.Type) is { } empty && variable.Kind != VariableKind.Loop && slots.TryGetValue(variable, out var slot))
            {
                var value = fn.Value($"load {Ir(variable.Type)}, ptr {slot}");
                fn.Emit($"store {Ir(variable.Type)} {empty}, ptr {slot}");
                return value;
            }
        }

        return Owned(e);
    }

    /// <summary>The empty value of a place a <c>move</c> leaves: the address 0, and a string of no block, which holds nothing to let go of; null for a type whose place is not emptied, which a <c>move</c> counts once more.</summary>
    private string? EmptyValue(KurzType type) => Ir(type) switch
    {
        "ptr" => "null",
        "%kz.str" => "zeroinitializer",
        _ => null,
    };

    /// <summary>Lets go of a value a <c>move</c> may have taken, through a test for the empty address.</summary>
    private void ReleaseHeld(string value, KurzType type)
    {
        if (Ir(type) != "ptr")
        {
            Release(value, type); // a string of no block holds nothing (EmptyValue)
            return;
        }

        var held = fn.Label("held");
        var done = fn.Label("moved");
        fn.CondBranch(fn.Value($"icmp eq ptr {value}, null"), done, held);
        fn.Block(held);
        Release(value, type);
        fn.Branch(done);
        fn.Block(done);
    }

    /// <summary>Whether a <c>move</c> may have emptied the place: the slot of a variable a <c>move</c> names.</summary>
    private bool MovedSlot(string slot) => slots.Any(p => p.Value == slot && Model.IsMoved(p.Key));

    /// <summary>Whether a loop of the program takes a list of the item type, so that the list's release tests each item for the empty address.</summary>
    private bool TakenItems(KurzType element)
    {
        takenElements ??= units
            .SelectMany(u => u.Model.TakingLoops.Select(f => u.Model.TypeOf(f.Collection).Type))
            .OfType<ListType>()
            .Select(l => Lower.Mangle(l.Element))
            .ToHashSet(StringComparer.Ordinal);
        return takenElements.Contains(Lower.Mangle(element));
    }

    /// <summary>The body of a function with the parameters written <c>move</c> it owns, each let go of where the function leaves, through the test where a <c>move</c> may have taken it.</summary>
    private void OwningMoved(IEnumerable<(VariableSymbol Variable, KurzType Type)> owned, Action body)
    {
        var counted = owned.Where(o => Counted(o.Type)).ToList();
        if (counted.Count == 0)
        {
            body();
            return;
        }

        InScope(() =>
        {
            foreach (var (variable, type) in counted)
            {
                Declare(slots[variable], type);
            }

            body();
        });
    }
}
