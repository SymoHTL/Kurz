using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>What a call is, once the checker has read it.</summary>
public enum CallKind
{
    /// <summary>A call of a function of the file, by name (F1); <see cref="CallInfo.Function"/> is the one picked (F13).</summary>
    Function,

    /// <summary>A call of a value of function type (F11): a variable, a parameter or a field.</summary>
    Value,

    /// <summary><c>print(value)</c> (A1).</summary>
    Print,

    /// <summary>A conversion written as a call of a number or time type (T10); <see cref="CallInfo.Target"/> is that type.</summary>
    Conversion,

    /// <summary><c>List&lt;T&gt;()</c> (M9).</summary>
    ListNew,

    /// <summary>A data value made by the type's name (D2); <see cref="CallInfo.Data"/> is the type.</summary>
    DataNew,

    /// <summary>A list's <c>Add</c> (M9).</summary>
    ListAdd,

    /// <summary>A list's <c>Where</c> (M9).</summary>
    ListWhere,
}

/// <summary>
/// A call as the checker read it: what is called, the type arguments it settled on (T19, T27),
/// which parameter slot each argument fills (D11; a slot no argument fills takes its default), and
/// the whole result type, every case included, where <see cref="SemanticModel.TypeOf"/> gives the
/// call's success alone when the call stands as a value (O2).
/// </summary>
public sealed record CallInfo(
    CallKind Kind,
    FunctionSymbol? Function,
    DataSymbol? Data,
    PrimitiveType? Target,
    IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map,
    IReadOnlyList<int> Binding,
    int SlotCount,
    KurzType Result);

/// <summary>A lambda as the checker read it: its parameters, the variables of the functions around it that it reads (F11, F15) and its type.</summary>
public sealed record LambdaInfo(IReadOnlyList<VariableSymbol> Parameters, IReadOnlyList<VariableSymbol> Captures, FunctionType Type);

/// <summary>
/// What the checker found out about a program, by node: the type and constant of every expression,
/// the variable every name means, the variable every declaration makes, what every call calls, what
/// every lambda captures, the type every arm pattern names, and the symbol of every function and
/// data type of the file. A program the checker read without an error has an entry for every node
/// it typed; a node the checker did not type (one inside a construct it does not read, or in a
/// program with errors) has none, and the readers that take a node throw for it, so that a code
/// generator never works from a guess.
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<Expression, Typed> types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NameExpression, VariableSymbol> names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, VariableSymbol> declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallExpression, CallInfo> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssignOrDeclare, VariableSymbol> assignments = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LambdaExpression, LambdaInfo> lambdas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypePattern, KurzType> patterns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionDeclaration, FunctionSymbol> functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeDeclaration, DataSymbol> dataTypes = new(ReferenceEqualityComparer.Instance);

    /// <summary>The type of an expression, and its value where it is a constant; for a call that stands as a value, its success (O2).</summary>
    public Typed TypeOf(Expression expression) => types.TryGetValue(expression, out var t) ? t : throw Missing("expression", expression.Line);

    /// <summary>Whether the checker typed the expression.</summary>
    public bool HasType(Expression expression) => types.ContainsKey(expression);

    /// <summary>The variable a name reads.</summary>
    public VariableSymbol VariableOf(NameExpression name) => names.TryGetValue(name, out var v) ? v : throw Missing($"name `{name.Name}`", name.Line);

    /// <summary>The variable a declaration makes: a <see cref="VariableDeclaration"/>, an <see cref="AssignOrDeclare"/> that declares, a <see cref="ForInStatement"/>, a <see cref="Parameter"/> of a lambda, a <see cref="ParameterSymbol"/> of a function, or a <see cref="TypePattern"/> with a binding.</summary>
    public VariableSymbol DeclarationOf(object node) => declarations.TryGetValue(node, out var v) ? v : throw Missing("declaration", (node as Node)?.Line ?? 0);

    /// <summary>Whether the node declares a variable: an <see cref="AssignOrDeclare"/> that assigns does not.</summary>
    public bool Declares(object node) => declarations.ContainsKey(node);

    /// <summary>The variable an <see cref="AssignOrDeclare"/> that assigns writes (V8).</summary>
    public VariableSymbol AssignedVariableOf(AssignOrDeclare assignment) => assignments.TryGetValue(assignment, out var v) ? v : throw Missing($"assignment to `{assignment.Name}`", assignment.Line);

    public CallInfo CallOf(CallExpression call) => calls.TryGetValue(call, out var c) ? c : throw Missing("call", call.Line);

    public LambdaInfo LambdaOf(LambdaExpression lambda) => lambdas.TryGetValue(lambda, out var l) ? l : throw Missing("lambda", lambda.Line);

    /// <summary>The type an arm's pattern names (C4, O5).</summary>
    public KurzType PatternTypeOf(TypePattern pattern) => patterns.TryGetValue(pattern, out var t) ? t : throw Missing("pattern", pattern.Line);

    public FunctionSymbol FunctionOf(FunctionDeclaration declaration) => functions.TryGetValue(declaration, out var f) ? f : throw Missing($"function `{declaration.Name}`", declaration.Line);

    public DataSymbol DataTypeOf(TypeDeclaration declaration) => dataTypes.TryGetValue(declaration, out var d) ? d : throw Missing($"type `{declaration.Name}`", declaration.Line);

    internal void RecordType(Expression expression, Typed typed) => types[expression] = typed;

    internal void RecordName(NameExpression name, VariableSymbol variable) => names[name] = variable;

    internal void RecordDeclaration(object node, VariableSymbol variable) => declarations[node] = variable;

    internal void RecordCall(CallExpression call, CallInfo info) => calls[call] = info;

    internal void RecordAssignment(AssignOrDeclare assignment, VariableSymbol variable) => assignments[assignment] = variable;

    internal void RecordLambda(LambdaExpression lambda, LambdaInfo info) => lambdas[lambda] = info;

    internal void RecordPattern(TypePattern pattern, KurzType type) => patterns[pattern] = type;

    internal void RecordFunction(FunctionDeclaration declaration, FunctionSymbol symbol) => functions[declaration] = symbol;

    internal void RecordDataType(TypeDeclaration declaration, DataSymbol symbol) => dataTypes[declaration] = symbol;

    private static InvalidOperationException Missing(string what, int line) =>
        new($"the checker did not read the {what} at line {line}: the model is read for a program the checker read without an error, and inside no construct it does not read");
}
