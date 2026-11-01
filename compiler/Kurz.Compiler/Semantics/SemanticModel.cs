using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>What a call is, as the checker read it.</summary>
public enum CallKind
{
    /// <summary>A call of a function of the file by name (F1), one of the C calling convention among them (R16); <see cref="CallInfo.Function"/> is the one picked (F13).</summary>
    Function,

    /// <summary>A call of a declaration of a block of C functions by name (R16); <see cref="CallInfo.Function"/> is the declaration.</summary>
    CFunction,

    /// <summary>A call of a value of function type (F11) or of a C function type (R17): a variable, a parameter, a field or another value.</summary>
    Value,

    /// <summary><c>print(value)</c> (A1).</summary>
    Print,

    /// <summary>A conversion written as a call of a number or time type (T10); <see cref="CallInfo.Target"/> is that type.</summary>
    Conversion,

    /// <summary><c>List&lt;T&gt;()</c> (M9).</summary>
    ListNew,

    /// <summary><c>Map&lt;K, V&gt;()</c> (M9).</summary>
    MapNew,

    /// <summary>A data value made by the type's name (D2); <see cref="CallInfo.Data"/> is the type.</summary>
    DataNew,

    /// <summary>A list's <c>Add</c> (M9).</summary>
    ListAdd,

    /// <summary>A list's <c>Where</c> (M9).</summary>
    ListWhere,

    /// <summary><c>Ptr&lt;T&gt;(x)</c> (R14); <see cref="CallInfo.Target"/> is the pointer type.</summary>
    PointerConversion,

    /// <summary><c>SizeOf&lt;T&gt;()</c> (R14); <see cref="CallInfo.Target"/> is the type whose size it is.</summary>
    SizeOf,

    /// <summary><c>string.FromRaw(p, n)</c> or <c>List&lt;T&gt;.FromRaw(p, n)</c> (R15); <see cref="CallInfo.Target"/> is the string or list type it makes.</summary>
    FromRaw,

    /// <summary><c>string.Allocate(n)</c> (R15), the address of room for the bytes of a string that <c>string.FromRaw</c> takes over.</summary>
    StringAllocate,

    /// <summary><c>List&lt;T&gt;.Allocate(n)</c> (R15), the address of room for the items of a list that <c>List&lt;T&gt;.FromRaw</c> takes over; <see cref="CallInfo.Target"/> is the list type.</summary>
    ListAllocate,

    /// <summary><c>string.FromUtf8(b)</c> (T15), a string of the bytes or the case <c>InvalidUtf8</c>.</summary>
    FromUtf8,

    /// <summary><c>string.FromUtf8Lossy(b)</c> (T15), a string of the bytes with U+FFFD for each maximal part of a sequence that is not UTF-8, as Rust counts them.</summary>
    FromUtf8Lossy,

    /// <summary><c>Plan.From(n)</c> (D18), the value of the <c>enum</c> whose number is <c>n</c> or the case <c>Invalid</c>; <see cref="CallInfo.Target"/> is the enum.</summary>
    EnumFrom,

    /// <summary>A list's <c>Clear</c> (M9).</summary>
    ListClear,

    /// <summary>A list's <c>IndexOf(item)</c> (X9).</summary>
    ListIndexOf,

    /// <summary>A list's <c>IndexOf(value)</c> of a run of items, a list of its own type (X9).</summary>
    ListIndexOfRun,

    /// <summary>A list's <c>AddRange(collection)</c> (X9).</summary>
    ListAddRange,

    /// <summary>A list's <c>Reverse()</c> (X9).</summary>
    ListReverse,

    /// <summary>A list's <c>Sort()</c>, in place and stable (X8).</summary>
    ListSort,

    /// <summary>A list's <c>SortUnstable()</c> (X8).</summary>
    ListSortUnstable,

    /// <summary>A list's <c>SortBy(keySelector)</c>, by a key made once for each item (X8).</summary>
    ListSortBy,

    /// <summary>A string's <c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c> or <c>IndexOf</c>, of a <c>string</c> or of a <c>char</c>, which the argument's type names (X1).</summary>
    StringSearch,

    /// <summary>A string's <c>Trim</c>, <c>TrimStart</c>, <c>TrimEnd</c>, <c>TrimAscii</c>, <c>TrimStartAscii</c> or <c>TrimEndAscii</c>, or <c>Trim(c)</c>, <c>TrimStart(c)</c> or <c>TrimEnd(c)</c> of a <c>char</c> (X4).</summary>
    StringTrim,

    /// <summary>A string's <c>Split(separator)</c>, of a <c>string</c> or of a <c>char</c>, which the argument's type names (X5).</summary>
    StringSplit,

    /// <summary>A string's <c>Replace</c> of a <c>string</c> by a <c>string</c>, of a <c>char</c> by a <c>string</c> or of a <c>char</c> by a <c>char</c>, which the arguments' types name (X5).</summary>
    StringReplace,

    /// <summary>A string's <c>ToLower</c>, <c>ToUpper</c>, <c>ToLowerAscii</c> or <c>ToUpperAscii</c> (X3).</summary>
    StringCase,

    /// <summary>A string's <c>Substring(startIndex, length)</c> (X2).</summary>
    StringSubstring,

    /// <summary><c>long.Parse(s)</c>, or the same call on another number type, the number or the case <c>ParseError</c> (X7).</summary>
    Parse,

    /// <summary><c>string.Join(separator, values)</c> of a list of any item type that has a text, with a <c>string</c> or a <c>char</c> separator, which the argument's type names (X6).</summary>
    StringJoin,

    /// <summary>A map's <c>ContainsKey(key)</c> (M9).</summary>
    MapContainsKey,

    /// <summary>A map's <c>Remove(key)</c> (M9), the value of the key's entry, moved out, or <c>null</c>.</summary>
    MapRemove,

    /// <summary>A map's <c>Clear</c> (M9).</summary>
    MapClear,

    /// <summary><c>File.Open(path)</c> (S2), a file opened to read or the case <c>IoError</c> (S1).</summary>
    FileOpen,

    /// <summary><c>File.Create(path)</c> (S2), a file created or emptied to write or the case <c>IoError</c>.</summary>
    FileCreate,

    /// <summary><c>File.ReadAll(path)</c> (S6).</summary>
    FileReadAll,

    /// <summary><c>File.WriteAll(path, bytes)</c> (S6).</summary>
    FileWriteAll,

    /// <summary><c>File.Delete(path)</c> (S7).</summary>
    FileDelete,

    /// <summary><c>File.Rename(from, to)</c> (S7).</summary>
    FileRename,

    /// <summary><c>File.Size(path)</c> (S7).</summary>
    FileSize,

    /// <summary><c>File.Exists(path)</c> (S7).</summary>
    FileExists,

    /// <summary><c>File.Modified(path)</c> (S15).</summary>
    FileModified,

    /// <summary><c>Directory.Create(path)</c> (S8).</summary>
    DirectoryCreate,

    /// <summary><c>Directory.List(path)</c> (S8).</summary>
    DirectoryList,

    /// <summary><c>Directory.Delete(path)</c> (S8).</summary>
    DirectoryDelete,

    /// <summary>A file's or a stream's <c>Read(mut into, max)</c> (S3, S9).</summary>
    FileRead,

    /// <summary>A file's or a stream's <c>Write(bytes)</c> (S4, S9).</summary>
    FileWrite,

    /// <summary>A file's, a listener's or a stream's <c>Close()</c> (S5, S9).</summary>
    FileClose,

    /// <summary>A file's <c>Lines()</c>, the collection of a <c>for</c> loop (S16).</summary>
    FileLines,

    /// <summary><c>TcpListener.Bind(address)</c> (S9).</summary>
    TcpBind,

    /// <summary><c>TcpStream.Connect(address)</c> (S9).</summary>
    TcpConnect,

    /// <summary>A listener's <c>Accept()</c> (S9).</summary>
    TcpAccept,

    /// <summary><c>Environment.Args()</c> (S13).</summary>
    EnvironmentArgs,

    /// <summary><c>Environment.Variable(name)</c> (S14).</summary>
    EnvironmentVariable,

    /// <summary><c>Sha256.Hash(bytes)</c>, <c>Md5.Hash(bytes)</c> or <c>Crc32.Hash(bytes)</c> (H1); <see cref="CallInfo.Target"/> is the hasher's type.</summary>
    Hash,

    /// <summary><c>Hmac.Sha256(key, message)</c> (H1).</summary>
    HmacHash,

    /// <summary><c>Hex.Encode(bytes)</c> (H2).</summary>
    HexEncode,

    /// <summary><c>Hex.Decode(s)</c> (H7), the bytes of the digits or the case <c>HexError</c>.</summary>
    HexDecode,

    /// <summary><c>Sha256.New()</c>, <c>Md5.New()</c> or <c>Crc32.New()</c> (H3); <see cref="CallInfo.Target"/> is the hasher's type.</summary>
    HasherNew,

    /// <summary><c>Hmac.NewSha256(key)</c> (H3).</summary>
    HmacNew,

    /// <summary>A hasher's <c>Update(bytes)</c> (H3); <see cref="CallInfo.Target"/> is the hasher's type.</summary>
    HasherUpdate,

    /// <summary>A hasher's <c>Finish()</c> (H3); <see cref="CallInfo.Target"/> is the hasher's type.</summary>
    HasherFinish,

    /// <summary><c>Hmac.VerifySha256(key, message, tag)</c> (H4).</summary>
    HmacVerify,

    /// <summary><c>FixedTime.Equal(a, b)</c> (H4).</summary>
    FixedTimeEqual,
}

/// <summary>
/// A call as the checker read it: what is called, the type arguments it settled on (T19, T27), which
/// parameter slot each argument as written fills (D11; a slot no argument fills takes its default; -1
/// for an argument in the place of a C declaration's <c>...</c>, R16), and the whole result type,
/// every case included, where <see cref="SemanticModel.TypeOf"/> gives the call's success alone when
/// the call stands as a value (O2).
/// </summary>
public sealed record CallInfo(
    CallKind Kind,
    FunctionSymbol? Function,
    DataSymbol? Data,
    KurzType? Target,
    IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map,
    IReadOnlyList<int> Binding,
    int SlotCount,
    KurzType Result);

/// <summary>A lambda as the checker read it: its parameters, the variables of the functions around it that it reads (F11, F15) and its type.</summary>
public sealed record LambdaInfo(IReadOnlyList<VariableSymbol> Parameters, IReadOnlyList<VariableSymbol> Captures, FunctionType Type);

/// <summary>
/// What the checker found out about a program, by node, for a code generator: the type and constant
/// of every expression, the variable or function every name means, the variable every declaration
/// makes, what every call calls, what every lambda captures, the type every arm pattern names, and
/// the symbol of every function, C declaration and data type of the file. A program the checker
/// read without an error and without <c>not supported</c> has an entry for every node it typed; a
/// node it did not type has none, and the readers that take a node throw for it, so that a code
/// generator never works from a guess. Where the checker reads a node twice or more, a call's
/// literal argument against each function of the name and then against the one picked, the last
/// reading stands.
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<Expression, Typed> types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NameExpression, VariableSymbol> names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NameExpression, FunctionSymbol> functionNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, VariableSymbol> declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallExpression, CallInfo> calls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssignOrDeclare, VariableSymbol> assignments = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LambdaExpression, LambdaInfo> lambdas = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expression> heldCopies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypePattern, KurzType> patterns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionDeclaration, FunctionSymbol> functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ExternFunction, FunctionSymbol> cFunctions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeDeclaration, DataSymbol> dataTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MemberAccess, KurzType> chainMembers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MemberAccess> clockReads = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// <c>args</c>, the program's arguments without its name, which the top-level code sees (S13); null in a file that is no program's
    /// top-level code.
    /// </summary>
    public VariableSymbol? Arguments { get; private set; }

    /// <summary>The line of the first read of <see cref="Arguments"/>, 0 where nothing reads it.</summary>
    public int ArgumentsLine { get; private set; }

    /// <summary>The type of an expression, and its value where it is a constant; for a call that stands as a value, its success (O2).</summary>
    public Typed TypeOf(Expression expression) => types.TryGetValue(expression, out var t) ? t : throw Missing("expression", expression.Line);

    /// <summary>Whether the checker typed the expression.</summary>
    public bool HasType(Expression expression) => types.ContainsKey(expression);

    /// <summary>The variable a name reads or writes: a local, a parameter, a loop variable, a binding or state outside a function (R13).</summary>
    public VariableSymbol VariableOf(NameExpression name) => names.TryGetValue(name, out var v) ? v : throw Missing($"name `{name.Name}`", name.Line);

    /// <summary>Whether the name means a variable.</summary>
    public bool IsVariable(NameExpression name) => names.ContainsKey(name);

    /// <summary>The function a name means where it stands for no variable: a function of the C calling convention as a value of its C function type (R17), or the function a <c>sym</c> operand names (R11).</summary>
    public FunctionSymbol FunctionNameOf(NameExpression name) => functionNames.TryGetValue(name, out var f) ? f : throw Missing($"function name `{name.Name}`", name.Line);

    /// <summary>Whether the name means a function where it stands for no variable.</summary>
    public bool IsFunctionName(NameExpression name) => functionNames.ContainsKey(name);

    /// <summary>
    /// The variable a declaration makes: a <see cref="VariableDeclaration"/> (a local, or state outside a function, R13), an
    /// <see cref="AssignOrDeclare"/> that declares, a <see cref="ForInStatement"/>, a <see cref="Parameter"/> of a function or a
    /// lambda, or a <see cref="TypePattern"/> with a binding.
    /// </summary>
    public VariableSymbol DeclarationOf(object node) => declarations.TryGetValue(node, out var v) ? v : throw Missing("declaration", (node as Node)?.Line ?? (node as Parameter)?.Line ?? 0);

    /// <summary>Whether the node declares a variable: an <see cref="AssignOrDeclare"/> that assigns does not.</summary>
    public bool Declares(object node) => declarations.ContainsKey(node);

    /// <summary>The variable an <see cref="AssignOrDeclare"/> that assigns writes (V8).</summary>
    public VariableSymbol AssignedVariableOf(AssignOrDeclare assignment) => assignments.TryGetValue(assignment, out var v) ? v : throw Missing($"assignment to `{assignment.Name}`", assignment.Line);

    public CallInfo CallOf(CallExpression call) => calls.TryGetValue(call, out var c) ? c : throw Missing("call", call.Line);

    /// <summary>Whether the checker read the call.</summary>
    public bool HasCall(CallExpression call) => calls.ContainsKey(call);

    public LambdaInfo LambdaOf(LambdaExpression lambda) => lambdas.TryGetValue(lambda, out var l) ? l : throw Missing("lambda", lambda.Line);

    /// <summary>Whether the checker read the lambda.</summary>
    public bool HasLambda(LambdaExpression lambda) => lambdas.ContainsKey(lambda);

    /// <summary>
    /// Whether the expression is a read of a copied type that its expression still holds when something changes the place it was
    /// read from (M7): a read the code generator copies, so that a function value in it keeps what it holds (the owner, 2026-10-25,
    /// "Copied", at a count and a release per such read).
    /// </summary>
    public bool IsHeldCopy(Expression expression) => heldCopies.Contains(expression);

    /// <summary>The type an arm's pattern names (C4, O5).</summary>
    public KurzType PatternTypeOf(TypePattern pattern) => patterns.TryGetValue(pattern, out var t) ? t : throw Missing("pattern", pattern.Line);

    public FunctionSymbol FunctionOf(FunctionDeclaration declaration) => functions.TryGetValue(declaration, out var f) ? f : throw Missing($"function `{declaration.Name}`", declaration.Line);

    /// <summary>The symbol of a declaration of a block of C functions (R16).</summary>
    public FunctionSymbol CFunctionOf(ExternFunction declaration) => cFunctions.TryGetValue(declaration, out var f) ? f : throw Missing($"C function `{declaration.Name}`", declaration.Line);

    public DataSymbol DataTypeOf(TypeDeclaration declaration) => dataTypes.TryGetValue(declaration, out var d) ? d : throw Missing($"type `{declaration.Name}`", declaration.Line);

    /// <summary>
    /// Whether the member is read in a chain of <c>?.</c> (N5): it is read only where the chain did not stop, from the value of the type
    /// the type of its target makes nullable, and <paramref name="member"/> is its own type, which <see cref="TypeOf"/> gives nullable.
    /// </summary>
    public bool IsChainMember(MemberAccess access, out KurzType member) => chainMembers.TryGetValue(access, out member!);

    /// <summary>Whether the member is <c>timestamp.Now</c>, the read of the wall clock (S15).</summary>
    public bool IsClockRead(MemberAccess access) => clockReads.Contains(access);

    internal void RecordClockRead(MemberAccess access) => clockReads.Add(access);

    internal void RecordArguments(VariableSymbol arguments) => Arguments = arguments;

    internal void RecordArgumentsRead(int line)
    {
        if (ArgumentsLine == 0 || line < ArgumentsLine)
        {
            ArgumentsLine = line;
        }
    }

    internal void RecordType(Expression expression, Typed typed) => types[expression] = typed;

    internal void RecordName(NameExpression name, VariableSymbol variable) => names[name] = variable;

    internal void RecordFunctionName(NameExpression name, FunctionSymbol function) => functionNames[name] = function;

    internal void RecordDeclaration(object node, VariableSymbol variable) => declarations[node] = variable;

    internal void RecordCall(CallExpression call, CallInfo info) => calls[call] = info;

    internal void RecordAssignment(AssignOrDeclare assignment, VariableSymbol variable) => assignments[assignment] = variable;

    internal void RecordLambda(LambdaExpression lambda, LambdaInfo info) => lambdas[lambda] = info;

    internal void RecordHeldCopy(Expression expression) => heldCopies.Add(expression);

    internal void RecordPattern(TypePattern pattern, KurzType type) => patterns[pattern] = type;

    internal void RecordFunction(FunctionDeclaration declaration, FunctionSymbol symbol) => functions[declaration] = symbol;

    internal void RecordCFunction(ExternFunction declaration, FunctionSymbol symbol) => cFunctions[declaration] = symbol;

    internal void RecordDataType(TypeDeclaration declaration, DataSymbol symbol) => dataTypes[declaration] = symbol;

    internal void RecordChainMember(MemberAccess access, KurzType member) => chainMembers[access] = member;

    private static InvalidOperationException Missing(string what, int line) =>
        new($"the checker did not read the {what} at line {line}: the model is read for a program the checker read without an error and without `not supported`");
}
