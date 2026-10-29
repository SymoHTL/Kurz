using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// The checker: names, scopes and types of a program the parser read without an error, against the
/// rules of chapters 1 to 3, 5, 6, 8, 9 and 10 of the reference and the raw forms of chapter 11, with
/// the grants a program is given (R7). It reports the compile errors of
/// the error table those chapters name, each at the line E1 gives it, and <c>not supported</c>
/// for a construct it does not read, at the construct's line and nothing else: a class and the
/// other types of chapter 7, chapter 4's <c>null</c>, chapter 11's <c>weak</c>, and
/// the rest of the list in the section "What the checker does not read" of <c>compiler/README.md</c>,
/// which is the one complete list. A value of a construct it does not read has the error type, about
/// which nothing is reported, so that the one line of the construct carries the one diagnostic
/// (E1); what stands inside such a construct is still read, with its diagnostics dropped, so that
/// the variables it uses count as used. <c>compiler/README.md</c> lists the readings the checker
/// takes where the reference leaves a choice.
/// </summary>
public sealed partial class Checker
{
    private readonly DiagnosticBag diagnostics;
    private readonly Stack<List<Diagnostic>> sinks = new();
    private readonly Dictionary<string, List<FunctionSymbol>> functions = new(StringComparer.Ordinal);
    private readonly List<FunctionSymbol> duplicates = [];
    private readonly List<FunctionSymbol> refused = [];
    private readonly Dictionary<string, DataSymbol> dataTypes = new(StringComparer.Ordinal);
    private readonly List<DataSymbol> dataDeclared = [];
    private readonly List<DataSymbol> refusedTypes = [];
    private readonly HashSet<string> unsupportedTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> unsupportedBases = new(StringComparer.Ordinal);
    private readonly HashSet<string> unreadData = new(StringComparer.Ordinal);
    private readonly HashSet<string> unreadFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionSymbol> cFunctions = new(StringComparer.Ordinal); // R16: the C declarations a call can pick, by their one name
    private readonly List<FunctionSymbol> cDeclared = []; // R16: every C declaration in source order, a redeclared one among them
    private readonly HashSet<FunctionSymbol> modifiedFunctions = [];
    private readonly List<FunctionSymbol> unreadOverloads = [];
    private readonly List<(HashSet<string> Types, HashSet<string> Functions)> nestedNames = [];
    private readonly HashSet<string> reservedTopLevel = new(StringComparer.Ordinal);
    private readonly HashSet<string> stateNames = new(StringComparer.Ordinal); // R13: the names of the state outside a function, declared with the functions and types
    private readonly List<(VariableDeclaration Declaration, bool Named)> stateDeclarations = [];
    private readonly Dictionary<string, VariableSymbol> statics = new(StringComparer.Ordinal); // R13: that state, which every function reaches, at the error type while its declaration is not checked
    private readonly Dictionary<DataSymbol, List<DataSymbol>> baseCycles = [];
    private readonly Dictionary<string, KurzType> topLevel = new(StringComparer.Ordinal);
    private readonly List<VariableSymbol> declared = [];
    private Frame frame = null!;
    private List<TypeParameterSymbol>? typeContext;
    private bool inElseArm;
    private bool realKeepsOwnType;
    private int blockDepth;
    private int rawDepth; // R7, R12: the `raw` blocks and `naked` bodies around the code being checked
    private bool readingConstant; // F12, R13: a default value or the initial value of state is being read, in which a read of state or of raw memory is no constant
    private bool stateRead; // R13: the value being read as a constant reads state
    private List<(PointerType Pointer, int Line, string What)>? pendingPointees; // R10: the pointers the fields of data types name, judged after every field is resolved
    private readonly bool allowRaw;
    private readonly bool allowNative;
    private readonly SemanticModel model = new();
    private readonly IReadOnlyDictionary<string, Checker> folders; // F10: the folders a `use` of their name can bring in, the runtime's `sys` alone
    private readonly Dictionary<string, Checker> used = new(StringComparer.Ordinal); // F10: the folders the file's `use` lines brought in
    private readonly HashSet<UseStatement> uses = new(ReferenceEqualityComparer.Instance); // F10: those `use` lines
    private readonly List<FunctionSymbol> folderCalls = []; // design record, section 2: the functions of another folder the file's qualified calls picked

    private Checker(DiagnosticBag diagnostics, IReadOnlyCollection<string> grants, IReadOnlyDictionary<string, Checker> folders)
    {
        this.diagnostics = diagnostics;
        allowRaw = grants.Contains("raw");
        allowNative = grants.Contains("native");
        this.folders = folders;
    }

    /// <summary>The types of the variables the top level declares, by name, for a test to read.</summary>
    public IReadOnlyDictionary<string, KurzType> TopLevelVariables => topLevel;

    /// <summary>Every variable the program declares, in the order the checker met them: parameters, locals, loop variables and match bindings.</summary>
    public IReadOnlyList<VariableSymbol> Variables => declared;

    /// <summary>What the checker found out, by node, for a code generator (<see cref="SemanticModel"/>).</summary>
    public SemanticModel Model => model;

    /// <summary>Checks a program the parser read without an error, in a package granted nothing (R7), and adds what it finds to <paramref name="diagnostics"/>.</summary>
    public static Checker Check(ProgramSyntax program, DiagnosticBag diagnostics) => Check(program, diagnostics, []);

    /// <summary>
    /// Checks a program the parser read without an error, in a package granted <paramref name="grants"/>, <c>raw</c> and <c>native</c> as a
    /// project file or a case's header line gives them (R7, R16), and adds what it finds to <paramref name="diagnostics"/>.
    /// </summary>
    public static Checker Check(ProgramSyntax program, DiagnosticBag diagnostics, IReadOnlyCollection<string> grants) =>
        Check(program, diagnostics, grants, new Dictionary<string, Checker>());

    /// <summary>
    /// As above, where a `use` at the top of the file brings in a folder <paramref name="folders"/> names, by the name it has there, and the
    /// file calls the folder's `pub` functions qualified by that name, `sys.Write(...)`: the `use` is F10's and the `pub` F9's, while the
    /// qualified call is stated by the design record's section 2 alone, where the runtime's `runtime/common/` reaches the directory of the
    /// build's target so, as the folder `sys` (the owner, 2026-10-23, "Rust's sys"); a `use` of any other name is <c>not supported</c>.
    /// </summary>
    public static Checker Check(ProgramSyntax program, DiagnosticBag diagnostics, IReadOnlyCollection<string> grants, IReadOnlyDictionary<string, Checker> folders)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(folders);
        if (grants.FirstOrDefault(g => g is not ("raw" or "native")) is { } unknown)
        {
            throw new ArgumentException($"`{unknown}` is no grant: R7 and R16 name `raw` and `native`", nameof(grants));
        }

        var checker = new Checker(diagnostics, grants, folders);
        checker.Run(program);
        return checker;
    }

    /// <summary>The functions of other folders that the file's qualified calls picked (design record, section 2), each once.</summary>
    public IReadOnlyList<FunctionSymbol> FolderCalls => folderCalls.Distinct().ToList();

    private enum Flow
    {
        /// <summary>The statement can end and the next one run.</summary>
        Completes,

        /// <summary>Every path leaves the block: <c>return</c>, <c>throw</c>, <c>break</c>, <c>continue</c>, or branches that all do.</summary>
        Ends,
    }

    /// <summary>A function, a lambda or the top-level code while its body is checked.</summary>
    private sealed class Frame
    {
        public Frame? Outer { get; init; }

        public Scope? CapturedScope { get; init; }

        public bool IsLambda { get; init; }

        public bool IsTopLevel { get; init; }

        /// <summary>The result the body has to produce; null while a lambda's is still to be inferred from its first <c>return</c>.</summary>
        public KurzType? ReturnType { get; set; }

        public int LoopDepth { get; set; }

        public int Line { get; init; }

        public List<TypeParameterSymbol> TypeParameters { get; init; } = [];

        /// <summary>F16: the names of the top-level variables, which a top-level function neither reads nor declares.</summary>
        public HashSet<string>? Reserved { get; init; }

        public Scope Scope { get; set; } = new();

        /// <summary>The variables of the functions around this lambda that its body reads (F11, F15).</summary>
        public HashSet<VariableSymbol> Captures { get; } = [];
    }

    private sealed class Scope
    {
        public Scope? Parent { get; init; }

        public Dictionary<string, VariableSymbol> Variables { get; } = new(StringComparer.Ordinal);
    }

    // ---- diagnostics ------------------------------------------------------------------------------

    private void Add(Diagnostic diagnostic)
    {
        if (sinks.Count > 0)
        {
            sinks.Peek().Add(diagnostic);
        }
        else
        {
            diagnostics.Add(diagnostic);
        }
    }

    private void Report(string id, int line, string message) => Add(Diagnostic.Error(id, line, message));

    private void NotSupported(int line, string construct) => Add(Diagnostic.NotSupported(line, construct));

    /// <summary>Diagnostics of what follows go into a list of their own until <see cref="Pop"/>.</summary>
    private void Push() => sinks.Push([]);

    /// <summary>Ends the capture; the diagnostics are kept when <paramref name="commit"/>, dropped otherwise.</summary>
    private List<Diagnostic> Pop(bool commit)
    {
        var list = sinks.Pop();
        if (commit)
        {
            foreach (var d in list)
            {
                Add(d);
            }
        }

        return list;
    }

    // ---- the passes ------------------------------------------------------------------------------

    private void Run(ProgramSyntax program)
    {
        // T15: the data type of the case `string.FromUtf8` returns besides its string, declared before the file's own types, so
        // that a declaration of its name is `redeclared` (V9; section 4, the owner, 2026-10-29, "Union, as Rust")
        foreach (var s in BuiltInTypes.Statements)
        {
            DeclareType((TypeDeclaration)s);
        }

        // F6: the functions and data types of the file are visible in the whole file, so their names are declared before
        // anything is checked, in source order, so that a name declared twice is `redeclared` at the later declaration (V9)
        foreach (var s in program.Statements)
        {
            switch (s)
            {
                case TypeDeclaration t:
                    DeclareType(t);
                    break;
                case FunctionDeclaration f:
                    DeclareFunction(f);
                    break;
                case ExternBlock e:
                    DeclareCFunctions(e);
                    break;
                case VariableDeclaration v when IsState(v):
                    DeclareState(v);
                    break;
                case UseStatement u when folders.TryGetValue(u.Target, out var folder):
                    used[u.Target] = folder; // F10: before the bodies are checked, which call the folder's functions
                    uses.Add(u);
                    break;
                default:
                    break;
            }
        }

        RefuseUnreadBases();
        FindBaseCycles();
        pendingPointees = [];
        foreach (var symbol in dataDeclared.Concat(refusedTypes))
        {
            ResolveData(symbol);
        }

        JudgePendingPointees();
        ResolveCFunctions();
        foreach (var f in functions.Values.SelectMany(list => list).Concat(refused))
        {
            ResolveSignature(f);
        }

        RefuseModifiedFunctions();
        CheckDuplicateFunctions();
        CheckStates();
        CheckDefaults();
        CollectReserved(program.Statements);

        foreach (var f in functions.Values.SelectMany(list => list).Concat(duplicates).Concat(refused).Concat(unreadOverloads))
        {
            Unread(f, () => CheckFunctionBody(f));
        }

        frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = 1 };
        foreach (var s in program.Statements)
        {
            CheckStatement(s);
        }

        foreach (var v in frame.Scope.Variables.Values)
        {
            topLevel[v.Name] = v.Type;
        }

        ReportUnused(frame.Scope);
    }

    /// <summary>
    /// F1 with F6: the names of the functions and types a block declares, which the block's statements see while it is
    /// checked: such a declaration is <c>not supported</c> where it stands, and inside its block its name is one the
    /// checker does not read, so that a use of it there carries no error of its own (E1); outside its block the name is
    /// not visible (V10). A function's name is no type's.
    /// </summary>
    private void PushNested(IEnumerable<Statement> statements)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var functionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in statements)
        {
            switch (s)
            {
                case TypeDeclaration t:
                    types.Add(t.Name);
                    break;
                case FunctionDeclaration f:
                    functionNames.Add(f.Name);
                    break;
                default:
                    break;
            }
        }

        nestedNames.Add((types, functionNames));
    }

    private void PopNested() => nestedNames.RemoveAt(nestedNames.Count - 1);

    /// <summary>A type's name the checker does not read: a type of chapter 7, an enum, a flags type, a data type it does not read, or one declared inside a block around the place.</summary>
    private bool IsUnreadType(string name) => unsupportedTypes.Contains(name) || nestedNames.Any(n => n.Types.Contains(name));

    /// <summary>A name the checker does not read where a value or a call stands: an unread type's, a function's with a modifier of a method, or one declared inside a block around the place.</summary>
    private bool IsUnreadName(string name) => IsUnreadType(name) || unreadFunctions.Contains(name) || nestedNames.Any(n => n.Functions.Contains(name));

    /// <summary>Runs <paramref name="check"/>, with its diagnostics dropped where <paramref name="f"/> is a function the checker does not read (K7, M6).</summary>
    private void Unread(FunctionSymbol f, Action check)
    {
        var unread = modifiedFunctions.Contains(f);
        if (unread)
        {
            Push();
        }

        check();
        if (unread)
        {
            Pop(commit: false);
        }
    }

    /// <summary>
    /// F16: the names of the variables at the outermost level of the top-level code, which live for the whole program; a loop
    /// variable, a variable inside a block of the top-level code and a parameter of a lambda there reserve no name, since no
    /// function can mean them (the owner, 2026-10-21). That the name an arm binds reserves none either, and that the lambda is
    /// one of the top-level code, so that a lambda's parameter inside a top-level function stays a declaration there, is the
    /// record's reading.
    /// </summary>
    private void CollectReserved(IEnumerable<Statement> statements)
    {
        foreach (var s in statements)
        {
            switch (s)
            {
                case VariableDeclaration v when IsState(v):
                    break; // R13: state that every function reaches, so it reserves no name and is no variable of the top-level code
                case VariableDeclaration v:
                    reservedTopLevel.Add(v.Name);
                    break;
                case AssignOrDeclare a:
                    reservedTopLevel.Add(a.Name);
                    break;
                default:
                    break;
            }
        }
    }

    // ---- types and data --------------------------------------------------------------------------

    private void DeclareType(TypeDeclaration d)
    {
        var clash = IsDeclaredName(d.Name);
        if (clash)
        {
            Report(ErrorIds.Redeclared, d.Line, $"the name `{d.Name}` is declared already (V9)"); // a built-in name among them: `data List` could never be named (M9, T17)
        }

        if (d.Kind != TypeDeclarationKind.Data)
        {
            if (clash)
            {
                return;
            }

            var what = d.Kind switch
            {
                TypeDeclarationKind.Class => "a class (chapter 7)",
                TypeDeclarationKind.Interface => "an interface (K6, K10)",
                TypeDeclarationKind.Enum => "an enum (D12)",
                _ => "a flags type (D13)",
            };
            NotSupported(d.Line, what);
            unsupportedTypes.Add(d.Name);
            unsupportedBases[d.Name] = d.Kind switch
            {
                TypeDeclarationKind.Class => "a class a data type names (chapter 7)",
                TypeDeclarationKind.Interface => "an interface a data type names (K10)",
                TypeDeclarationKind.Enum => "an enum a data type names (D12)",
                _ => "a flags type a data type names (D13)",
            };
            return;
        }

        if ((d.Modifiers & Modifiers.Extern) != 0)
        {
            if (!clash)
            {
                // §2, R17: a data type in C's layout, `extern "C" data Point(int X, int Y)`, which chapter 11 names and the checker
                // does not read; a use of it carries no error of its own (E1)
                NotSupported(d.Line, "a data type in C's layout, `extern \"C\" data` (R17)");
                unsupportedTypes.Add(d.Name);
                unsupportedBases[d.Name] = "a data type in C's layout that a data type names (R17)";
            }

            return;
        }

        var symbol = new DataSymbol(d.Name, d);
        model.RecordDataType(d, symbol);
        foreach (var p in d.TypeParameters)
        {
            symbol.TypeParameters.Add(new TypeParameterSymbol(p.Name, p.Line) { HasLimit = p.Limit is not null });
        }

        if (clash)
        {
            refusedTypes.Add(symbol); // its fields are checked all the same; no type names it
            return;
        }

        dataTypes[d.Name] = symbol;
        dataDeclared.Add(symbol);
    }

    /// <summary>
    /// Chapter 7, K10, K17, T19: a data type whose bases hold the explicit form of inheritance, a generic data type, or a class,
    /// an interface, an enum or a flags type is not read: that base is <c>not supported</c> at its line, and the type,
    /// with every data type whose base it is, joins the types the checker does not read, so that a use of any of them
    /// has the error type and carries nothing more (E1); the type's own fields would lack the base's (D7).
    /// </summary>
    private void RefuseUnreadBases()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var symbol in dataDeclared.ToList())
            {
                foreach (var b in symbol.Declaration.Bases)
                {
                    var construct = UnreadBase(b);
                    if (construct is null)
                    {
                        continue;
                    }

                    if (construct.Length > 0)
                    {
                        NotSupported(b.Line, construct);
                    }

                    dataTypes.Remove(symbol.Name);
                    dataDeclared.Remove(symbol);
                    unsupportedTypes.Add(symbol.Name);
                    unreadData.Add(symbol.Name);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);
    }

    /// <summary>The construct a base names that the checker does not read; an empty text for a data type it does not read, whose own base carried the diagnostic; null for a base it reads.</summary>
    private string? UnreadBase(BaseSpecifier b) => b switch
    {
        { Arguments: not null } => "the explicit form of inheritance (K17)",
        { Type: NamedType n } when dataTypes.TryGetValue(n.Name, out var data) && data.IsGeneric => "a generic data type as a base (T19)",
        { Type: NamedType n } when unreadData.Contains(n.Name) => "",
        { Type: NamedType n } when unsupportedBases.TryGetValue(n.Name, out var construct) => construct,
        _ => null,
    };

    /// <summary>The base D6 gives a data type of the file: the first data type among its bases that the checker reads, as <see cref="ResolveData"/> takes it.</summary>
    private DataSymbol? BaseOf(DataSymbol symbol) => symbol.Declaration.Bases
        .Select(b => b.Type is NamedType n && UnreadBase(b) is null && dataTypes.TryGetValue(n.Name, out var data) ? data : null)
        .FirstOrDefault(data => data is not null);

    /// <summary>
    /// D6: a data type that is its own base, directly or through others, is <c>base-cycle</c> at the base that closes the cycle. That
    /// base is the base of the declaration that completes the cycle where the files of a program are read in the order of their paths
    /// (the owner, 2026-10-21): in the one file the checker reads, the member declared last. Each file is read from its top, a type
    /// that names itself as its base is a cycle of one, a cycle is one error, and a type that only derives from a cycle gets none (the
    /// record's reading). Each closing member is kept with its cycle, from itself round to itself.
    /// </summary>
    private void FindBaseCycles()
    {
        var order = dataDeclared.Select((symbol, index) => (symbol, index)).ToDictionary(p => p.symbol, p => p.index);
        var walked = new HashSet<DataSymbol>();
        foreach (var start in dataDeclared)
        {
            var path = new List<DataSymbol>();
            for (var d = start; d is not null && !walked.Contains(d); d = BaseOf(d))
            {
                var at = path.IndexOf(d);
                if (at >= 0)
                {
                    var members = path.Skip(at).ToList();
                    var closing = members.MaxBy(m => order[m])!;
                    var round = members.Skip(members.IndexOf(closing)).Concat(members.Take(members.IndexOf(closing))).Append(closing).ToList();
                    baseCycles[closing] = round;
                    break;
                }

                path.Add(d);
            }

            walked.UnionWith(path);
        }
    }

    /// <summary>Resolves the base (D6), then the fields, the base's first (D7); refuses what a <c>data</c> type does not have. Each type once, whatever the outcome.</summary>
    private void ResolveData(DataSymbol symbol)
    {
        if (symbol.Resolved)
        {
            return; // resolved already, as the base of another, or being resolved, as a base of itself
        }

        symbol.Resolved = true;
        var d = symbol.Declaration;
        typeContext = symbol.TypeParameters;
        foreach (var p in d.TypeParameters)
        {
            if (p.Limit is not null)
            {
                NotSupported(p.Line, "the limit of a type parameter (T19)"); // not resolved: the one diagnostic of the construct (E1)
            }
        }

        // D6 gives a data type one base, the data type among the bases wherever it stands; a second one is text no rule gives a
        // meaning (E4); what the checker does not read among them left the type unread before (RefuseUnreadBases), unless the
        // type is a redeclared one, whose bases report it here
        DataSymbol? taken = null;
        foreach (var b in d.Bases)
        {
            if (UnreadBase(b) is { } construct)
            {
                if (construct.Length > 0)
                {
                    NotSupported(b.Line, construct);
                }

                continue;
            }

            if (b.Type is not NamedType named || !dataTypes.TryGetValue(named.Name, out var baseSymbol))
            {
                var baseType = Resolve(b.Type); // a name that is no type is `unknown-name` there (V10)
                if (!Types.IsError(baseType))
                {
                    Report(ErrorIds.TypeMismatch, b.Line, $"the base of a data type is a data type (D6), not `{baseType}`");
                }

                continue;
            }

            if (taken is not null)
            {
                Report(ErrorIds.Syntax, b.Line, $"a data type has one base, and `{named.Name}` is a second one (D6)");
                continue;
            }

            taken = baseSymbol;
            if (named.TypeArguments.Count > 0)
            {
                Report(ErrorIds.Syntax, b.Line, $"`{named.Name}` takes no type arguments (T19; E4 for another count)"); // the base is taken all the same, so that its fields carry no second error (E1)
            }

            if (baseCycles.TryGetValue(symbol, out var cycle))
            {
                Report(ErrorIds.BaseCycle, b.Line, $"the base `{named.Name}` closes a cycle of bases, {string.Join(" : ", cycle.Select(m => m.Name))}: a data type cannot be its own base (D6)"); // no base is set, so the walk up the bases ends
            }
            else
            {
                ResolveData(baseSymbol);
                typeContext = symbol.TypeParameters;
                symbol.Base = baseSymbol;
                symbol.Fields.AddRange(baseSymbol.Fields);
            }
        }

        if (d.EqualBy.Count > 0)
        {
            NotSupported(d.Line, "equal by (K8)");
        }

        if (d.PrimaryConstructor is { Count: 0 })
        {
            Report(ErrorIds.Syntax, d.Line, $"a data type without fields is written as the bare name `data {d.Name}`, with no `()` (D8)");
        }

        foreach (var p in d.PrimaryConstructor ?? [])
        {
            if (p.Mut)
            {
                Report(ErrorIds.Syntax, p.Line, $"`mut` on the field `{p.Name}` of a data type has no meaning: the variable that holds the value decides (D4)");
            }

            var type = p.Type is null ? Types.Error : Resolve(p.Type);
            if (p.Type is null)
            {
                Report(ErrorIds.Syntax, p.Line, $"the field `{p.Name}` of a data type names its type (D1)");
            }
            else if (Types.IsVoid(type))
            {
                Report(ErrorIds.Syntax, p.Line, "`void` is no type of a field (D1, F4)");
                type = Types.Error;
            }

            if (symbol.Field(p.Name) is not null)
            {
                Report(ErrorIds.Redeclared, p.Line, $"the field `{p.Name}` is declared already (V9)");
            }

            symbol.Fields.Add(new FieldSymbol(p.Name, type, p.Default is not null, p.Mut, p.Line)); // kept, so that the constructor's arity stays what is written
        }

        if (d.Members is { Count: > 0 } members)
        {
            // its members are not declared: a member that is no field, of a value of the type or of one derived from it, or after the
            // type's name, is `not supported` where it is used (MemberOf, CheckMember), so nothing more is reported (E1)
            NotSupported(members[0].Line, "the body of a data type (D16)");
        }

        typeContext = null;
    }

    private KurzType Resolve(TypeSyntax type)
    {
        switch (type)
        {
            case Syntax.VoidType:
                return Types.Void;
            case NamedType n:
                return ResolveNamed(n);
            case NullableType n:
                NotSupported(n.Line, "a nullable type (N1)");
                return Types.Error;
            case Syntax.UnionType u:
                {
                    var cases = u.Cases.Select(Resolve).ToList();
                    return cases.Any(Types.IsError) ? Types.Error : Types.Union(cases);
                }

            case Syntax.FunctionType f:
                {
                    var parameters = f.Parameters.Select(Resolve).ToList();
                    var result = Resolve(f.Result);
                    return parameters.Any(Types.IsError) || Types.IsError(result) ? Types.Error : new FunctionType(parameters, result);
                }

            case WeakType w:
                NotSupported(w.Line, "weak (R3, R5)");
                return Types.Error;
            case Syntax.PointerType p:
                return PointerTo(Resolve(p.Pointee), p.Line, "a pointer"); // R10, R14
            case ExternFunctionType f:
                return ResolveCFunctionType(f); // R17
            default:
                throw new InvalidOperationException(type.GetType().Name);
        }
    }

    private KurzType ResolveNamed(NamedType n)
    {
        if (Types.ByName.TryGetValue(n.Name, out var primitive))
        {
            if (n.TypeArguments.Count > 0)
            {
                Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` takes no type arguments (T17)");
                return Types.Error;
            }

            return primitive;
        }

        if (n.Name == "Bytes")
        {
            if (n.TypeArguments.Count > 0)
            {
                Report(ErrorIds.Syntax, n.Line, "`Bytes` takes no type arguments: it is `List<byte>` (M9, T17)");
                return Types.Error;
            }

            return new ListType(Types.Byte); // M9: the second name of a list of bytes (section 4, the owner, 2026-10-29)
        }

        if (n.Name == "List")
        {
            if (n.TypeArguments.Count != 1)
            {
                Report(n.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, n.Line, "`List` takes one type argument (M9, T17)");
                return Types.Error;
            }

            var element = Resolve(n.TypeArguments[0]);
            return Types.IsError(element) ? Types.Error : new ListType(element);
        }

        if (n.Name == "Map")
        {
            NotSupported(n.Line, "Map (M9)");
            return Types.Error;
        }

        var parameter = TypeParameter(n.Name);
        if (parameter is not null)
        {
            return new TypeParameterType(parameter);
        }

        if (dataTypes.TryGetValue(n.Name, out var data))
        {
            if (n.TypeArguments.Count == 0 && data.IsGeneric)
            {
                Report(ErrorIds.CannotInfer, n.Line, $"`{n.Name}` takes {data.TypeParameters.Count} type arguments, and a type names them (T19, T27)");
                return Types.Error;
            }

            if (n.TypeArguments.Count != data.TypeParameters.Count)
            {
                Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` takes {data.TypeParameters.Count} type arguments (T19)");
                return Types.Error;
            }

            var arguments = n.TypeArguments.Select(Resolve).ToList();
            return arguments.Any(Types.IsError) ? Types.Error : new DataType(data, arguments);
        }

        if (IsUnreadType(n.Name))
        {
            return Types.Error;
        }

        Report(ErrorIds.UnknownName, n.Line, $"no type named `{n.Name}` is visible here (V10)");
        return Types.Error;
    }

    private TypeParameterSymbol? TypeParameter(string name)
    {
        if (typeContext is not null)
        {
            return typeContext.FirstOrDefault(p => p.Name == name);
        }

        for (var f = frame; f is not null; f = f.Outer)
        {
            var found = f.TypeParameters.FirstOrDefault(p => p.Name == name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // ---- functions -------------------------------------------------------------------------------

    /// <summary>F6, V9: the name of a function, declared before any signature is resolved; a name a data type or a built-in has is <c>redeclared</c> and no call can pick the function.</summary>
    private void DeclareFunction(FunctionDeclaration d)
    {
        var symbol = new FunctionSymbol(d.Name, d);
        model.RecordFunction(d, symbol);
        foreach (var p in d.TypeParameters)
        {
            symbol.TypeParameters.Add(new TypeParameterSymbol(p.Name, p.Line) { HasLimit = p.Limit is not null });
        }

        if (dataTypes.ContainsKey(d.Name) || unsupportedTypes.Contains(d.Name) || IsBuiltInName(d.Name) || cFunctions.ContainsKey(d.Name) || stateNames.Contains(d.Name))
        {
            Report(ErrorIds.Redeclared, d.Line, $"the name `{d.Name}` is declared already (V9)"); // a C declaration's among them, which has one name (R16)
            refused.Add(symbol); // its signature, defaults and body are checked all the same
            return;
        }

        if (!functions.TryGetValue(d.Name, out var list))
        {
            list = [];
            functions[d.Name] = list;
        }

        list.Add(symbol);
    }

    /// <summary>F1, F4, T19: the parameter and result types of a function, resolved once every data type of the file is declared.</summary>
    private void ResolveSignature(FunctionSymbol symbol)
    {
        var d = symbol.Declaration!;
        typeContext = symbol.TypeParameters;
        foreach (var p in d.TypeParameters)
        {
            if (p.Limit is not null)
            {
                NotSupported(p.Line, "the limit of a type parameter (T19)"); // not resolved: the one diagnostic of the construct (E1)
            }
        }

        var unsupportedModifiers = d.Modifiers & ~(Modifiers.Pub | Modifiers.Naked | Modifiers.Extern);
        if (unsupportedModifiers != Modifiers.None)
        {
            NotSupported(d.Line, (unsupportedModifiers & Modifiers.Thread) != 0 ? "`thread static` before a function, which R13 states before state alone (R13)"
                : $"the modifier `{unsupportedModifiers.ToString().ToLowerInvariant()}` of a method (K7, M6)");
            modifiedFunctions.Add(symbol); // not read: its signature, defaults and body are read with their diagnostics dropped (E1)
        }

        Unread(symbol, () => ResolveTypes(symbol, d));
        typeContext = null;
        if (symbol.TakesC && !modifiedFunctions.Contains(symbol))
        {
            CheckCSignature(symbol, d); // R12, R16
        }
    }

    private void ResolveTypes(FunctionSymbol symbol, FunctionDeclaration d)
    {
        symbol.ReturnType = Resolve(d.ReturnType);
        foreach (var p in d.Parameters)
        {
            var type = p.Type is null ? Types.Error : Resolve(p.Type);
            if (p.Type is null)
            {
                Report(ErrorIds.Syntax, p.Line, $"the parameter `{p.Name}` of a function needs a type (F1)");
            }
            else if (Types.IsVoid(type))
            {
                Report(ErrorIds.Syntax, p.Line, "`void` is no type of a parameter (F4)");
                type = Types.Error;
            }

            if (symbol.Parameters.Any(q => q.Name == p.Name))
            {
                Report(ErrorIds.Redeclared, p.Line, $"the parameter `{p.Name}` is declared already (V9)");
            }

            symbol.Parameters.Add(new ParameterSymbol(p.Name, type, p.Mut, p.Default is not null, p.Line));
        }
    }

    /// <summary>
    /// K7, M6: a function of the file with a modifier of a method is not read, and neither is a call of its name, whichever
    /// function of the name the call would pick: the modifier's meaning is not applied, so the call could carry an error
    /// the program does not have (E1). The other functions of the name are checked all the same.
    /// </summary>
    private void RefuseModifiedFunctions()
    {
        foreach (var name in modifiedFunctions.Select(f => f.Name).Distinct(StringComparer.Ordinal).ToList())
        {
            if (functions.Remove(name, out var list))
            {
                unreadOverloads.AddRange(list);
                unreadFunctions.Add(name);
            }
        }
    }

    /// <summary>
    /// F12: two functions of one name whose parameters do not differ in number or in type at some
    /// place; names, defaults and <c>mut</c> make no difference. The later one is the error and is
    /// taken out of the overloads a call can pick, so that the call carries no second error (E1); its
    /// body is checked all the same.
    /// </summary>
    private void CheckDuplicateFunctions()
    {
        foreach (var list in functions.Values)
        {
            for (var i = 1; i < list.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (SameParameters(list[j], list[i]))
                    {
                        Report(ErrorIds.DuplicateFunction, list[i].Line, $"`{list[i].Name}` is declared already with these parameter types (F12)");
                        duplicates.Add(list[i]);
                        list.RemoveAt(i);
                        i--;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>O10: a function whose success case is <c>void</c> needs no <c>return</c> with a value.</summary>
    private static bool SuccessIsVoid(KurzType type) => type is VoidType || type is UnionType { Cases: [VoidType, ..] };

    /// <summary>F12 names the parameters only: the type parameters are matched by their position, and their count makes no difference.</summary>
    private static bool SameParameters(FunctionSymbol a, FunctionSymbol b)
    {
        if (a.Parameters.Count != b.Parameters.Count)
        {
            return false;
        }

        var placeholders = Enumerable.Range(0, Math.Max(a.TypeParameters.Count, b.TypeParameters.Count))
            .Select(i => (KurzType)new TypeParameterType(new TypeParameterSymbol("#" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 0)))
            .ToList();
        var mapA = a.TypeParameters.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => placeholders[x.i]);
        var mapB = b.TypeParameters.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => placeholders[x.i]);
        return a.Parameters.Zip(b.Parameters).All(pair => Types.Same(Types.Substitute(pair.First.Type, mapA), Types.Substitute(pair.Second.Type, mapB)));
    }

    /// <summary>F12, D11: a default value is a constant expression that fits its parameter, checked with no variable in sight.</summary>
    private void CheckDefaults()
    {
        foreach (var f in functions.Values.SelectMany(list => list).Concat(duplicates).Concat(refused).Concat(unreadOverloads))
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = f.Line, TypeParameters = f.TypeParameters };
            foreach (var (p, symbol) in f.Declaration!.Parameters.Zip(f.Parameters))
            {
                if (p.Default is not null)
                {
                    Unread(f, () => CheckDefault(p.Default, symbol.Type, p.Line));
                }
            }
        }

        foreach (var d in dataDeclared.Concat(refusedTypes))
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = d.Line, TypeParameters = d.TypeParameters };
            var written = d.Declaration.PrimaryConstructor ?? [];
            var own = d.Fields.Skip(d.Fields.Count - written.Count); // the type's own fields follow the base's, one per written parameter (D7)
            foreach (var (p, field) in written.Zip(own))
            {
                if (p.Default is not null)
                {
                    CheckDefault(p.Default, field.Type, p.Line);
                }
            }
        }

        foreach (var f in cDeclared)
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = f.Line };
            foreach (var (p, symbol) in f.CDeclaration!.Parameters.Zip(f.Parameters))
            {
                if (p.Default is not null)
                {
                    Unread(f, () => CheckDefault(p.Default, symbol.Type, p.Line)); // R16 with F12, D11
                }
            }
        }
    }

    private void CheckDefault(Expression value, KurzType type, int parameterLine)
    {
        var reading = readingConstant;
        readingConstant = true; // a read of state or of raw memory is no constant, and `default-not-constant` is its one error (R13, E1)
        var t = CheckExpression(value, type);
        readingConstant = reading;
        if (t.IsError || Types.IsError(type))
        {
            return;
        }

        if (t.Constant is null)
        {
            Report(ErrorIds.DefaultNotConstant, parameterLine, "a default value is a constant expression: a literal, a data value without fields, or literals that T6 folds (F12)");
        }
        else if (!Types.Assignable(t.Type, type))
        {
            Report(Types.MismatchId(t.Type, type), value.Line, $"the default is a `{t.Type}` where a `{type}` is required (T1)");
        }
    }

    private void CheckFunctionBody(FunctionSymbol f)
    {
        var d = f.Declaration!;
        if ((d.Modifiers & Modifiers.Naked) != 0)
        {
            CheckNakedBody(f); // R12
            return;
        }

        frame = new Frame { ReturnType = f.ReturnType, Line = d.Line, TypeParameters = f.TypeParameters, Reserved = reservedTopLevel };
        foreach (var (p, node) in f.Parameters.Zip(d.Parameters))
        {
            DeclareParameter(p.Name, p.Type, p.Mut, p.Line, node);
        }

        var returnType = f.ReturnType;
        if (d.ExpressionBody is not null)
        {
            CheckExpressionBody(d.ExpressionBody, returnType, d.Line);
        }
        else if (d.BlockBody is not null)
        {
            var flow = CheckBlock(d.BlockBody);
            if (flow == Flow.Completes && !SuccessIsVoid(returnType) && !Types.IsError(returnType))
            {
                Report(ErrorIds.MissingReturn, d.Line, $"the end of `{d.Name}` can be reached without a `return` (F3)");
            }
        }
        else
        {
            Report(ErrorIds.Syntax, d.Line, "a function outside a type body has a body (F2, F3)");
        }

        ReportUnused(frame.Scope);
    }

    /// <summary>The <c>=> expression</c> of a function or a lambda against its result type.</summary>
    private void CheckExpressionBody(Expression body, KurzType returnType, int line)
    {
        if (SuccessIsVoid(returnType))
        {
            // F2 writes the body as `=> expression` and F4 gives a `void` function no value: a value standing there is text no rule
            // gives a meaning, E4's `syntax` (the reading in compiler/README.md); a call or a postfix `else` is what such a body is
            var t = CheckExpression(body, returnType is UnionType ? returnType : null, keepCases: returnType is UnionType);
            if (!Types.IsVoid(t.Type) && !t.IsError && Unwrap(body) is not CallExpression and not ElseExpression && !Types.Assignable(t.Type, returnType))
            {
                Report(ErrorIds.Syntax, line, "the expression body of a `void` function is a call or a call with `else` (E4, F2, F4)");
            }

            return;
        }

        var typed = CheckExpression(body, returnType, keepCases: returnType is UnionType);
        if (typed.IsError || Types.IsError(returnType))
        {
            return;
        }

        if (Types.IsVoid(typed.Type))
        {
            Report(ErrorIds.Syntax, line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(typed.Type, returnType))
        {
            Report(Types.MismatchId(typed.Type, returnType), body.Line, $"the body is a `{typed.Type}` where the result is a `{returnType}` (F2, T1)"); // E1: at the value's line, as for every value that does not fit
        }
    }

    /// <summary>Records the type of an expression the checker read without <see cref="CheckExpression"/>, and of each expression inside the parentheses around it.</summary>
    private void RecordThrough(Expression e, Typed typed)
    {
        model.RecordType(e, typed);
        while (e is ParenthesizedExpression p)
        {
            e = p.Inner;
            model.RecordType(e, typed);
        }
    }

    /// <summary>The expression inside every pair of parentheses around it.</summary>
    private static Expression Unwrap(Expression e)
    {
        while (e is ParenthesizedExpression p)
        {
            e = p.Inner;
        }

        return e;
    }

    private VariableSymbol DeclareParameter(string name, KurzType type, bool mut, int line, Parameter node)
    {
        if (frame.Reserved is not null && frame.Reserved.Contains(name))
        {
            Report(ErrorIds.Redeclared, line, $"`{name}` is a top-level variable, whose name a top-level function cannot declare (F16)");
        }
        else if (Lookup(name).Variable is not null || IsDeclaredName(name))
        {
            Report(ErrorIds.Redeclared, line, $"`{name}` is visible already (V9)"); // a function's, a type's or `print`'s name, as for a local
        }

        var symbol = new VariableSymbol(name, type, mut, VariableKind.Parameter, line);
        frame.Scope.Variables[name] = symbol;
        model.RecordDeclaration(node, symbol);
        return symbol;
    }

    // ---- scopes ----------------------------------------------------------------------------------

    /// <summary>The variable a name means here, and whether it belongs to a function around the lambda being checked (F11, F15).</summary>
    private (VariableSymbol? Variable, bool Captured) Lookup(string name)
    {
        var f = frame;
        var scope = f.Scope;
        var captured = false;
        while (true)
        {
            for (var s = scope; s is not null; s = s.Parent)
            {
                if (s.Variables.TryGetValue(name, out var v))
                {
                    for (var g = frame; captured && g != f; g = g.Outer!)
                    {
                        g.Captures.Add(v); // every lambda between the use and the function that declares the variable captures it
                    }

                    return (v, captured);
                }
            }

            if (!f.IsLambda || f.Outer is null)
            {
                return statics.TryGetValue(name, out var state) ? (state, false) : (null, false); // R13: state outside a function, which every function reaches
            }

            captured = true;
            scope = f.CapturedScope;
            f = f.Outer;
        }
    }

    /// <summary>V9's reading: a function's and a type's name is visible in the whole file (F6), and so are the built-in names.</summary>
    private bool IsDeclaredName(string name) =>
        functions.ContainsKey(name) || dataTypes.ContainsKey(name) || cFunctions.ContainsKey(name) || stateNames.Contains(name) || IsUnreadName(name) || IsBuiltInName(name);

    /// <summary>The names the reference gives: <c>print</c> (A1), the built-in types (T2, L19), <c>List</c> and <c>Map</c> (M9).</summary>
    private static bool IsBuiltInName(string name) => name == "print" || Types.ByName.ContainsKey(name) || name is "List" or "Map" or "Bytes";

    /// <summary>The data types every file sees without declaring them: <c>InvalidUtf8</c>, the case of <c>string.FromUtf8</c> that is not a string (T15).</summary>
    private static readonly ProgramSyntax BuiltInTypes = Parser.Parse(SourceText.FromString("<built-in>", "data InvalidUtf8(int At)\n"), new DiagnosticBag());

    private void PushScope() => frame.Scope = new Scope { Parent = frame.Scope };

    private void PopScope()
    {
        ReportUnused(frame.Scope);
        frame.Scope = frame.Scope.Parent!;
    }

    /// <summary>V5: a variable never read, at its declaration; a parameter, a loop variable and a binding are exempt (V13).</summary>
    private void ReportUnused(Scope scope)
    {
        foreach (var v in scope.Variables.Values.Where(v => v.Kind == VariableKind.Local && !v.Read && !Types.IsError(v.Type)).OrderBy(v => v.Line))
        {
            Report(ErrorIds.UnusedVariable, v.Line, $"`{v.Name}` is never read (V5)");
        }
    }

    private void Declare(string name, KurzType type, bool mut, VariableKind kind, int line, object node)
    {
        var symbol = new VariableSymbol(name, type, mut, kind, line);
        frame.Scope.Variables[name] = symbol;
        declared.Add(symbol);
        model.RecordDeclaration(node, symbol);
    }

    /// <summary>Declares the variable, or, where the name is <c>redeclared</c>, binds it in this scope to the error type, so that its uses carry no further error and change no variable outside (E1).</summary>
    private void DeclareChecked(string name, KurzType type, bool mut, VariableKind kind, int line, object node)
    {
        if (!Redeclares(name, line))
        {
            Declare(name, type, mut, kind, line, node);
        }
        else
        {
            DeclareUnreadable(name, kind, line, node);
        }
    }

    private void DeclareUnreadable(string name, VariableKind kind, int line, object node)
    {
        Declare(name, Types.Error, true, kind, line, node);
        frame.Scope.Variables[name].Read = true;
    }

    /// <summary>V9, F16: whether declaring <paramref name="name"/> here is <c>redeclared</c>, reported at <paramref name="line"/>.</summary>
    private bool Redeclares(string name, int line)
    {
        if (frame.Reserved is not null && frame.Reserved.Contains(name))
        {
            Report(ErrorIds.Redeclared, line, $"`{name}` is a top-level variable, whose name a top-level function cannot declare (F16)");
            return true;
        }

        if (Lookup(name).Variable is not null || IsDeclaredName(name))
        {
            Report(ErrorIds.Redeclared, line, $"`{name}` is visible already (V9)");
            return true;
        }

        return false;
    }

    // ---- statements ------------------------------------------------------------------------------

    private Flow CheckBlock(Block block)
    {
        PushScope();
        PushNested(block.Statements);
        blockDepth++;
        var flow = Flow.Completes;
        foreach (var s in block.Statements)
        {
            if (CheckStatement(s) == Flow.Ends)
            {
                flow = Flow.Ends;
            }
        }

        blockDepth--;
        PopNested();
        PopScope();
        return flow;
    }

    private Flow CheckStatement(Statement statement)
    {
        var depth = expressionDepth;
        expressionDepth = 0; // a statement in a lambda's block or in an arm has expressions of its own (M7)
        var flow = CheckStatementCore(statement);
        expressionDepth = depth;
        return flow;
    }

    private Flow CheckStatementCore(Statement statement)
    {
        switch (statement)
        {
            case Block b:
                return CheckBlock(b);
            case VariableDeclaration v:
                CheckVariableDeclaration(v);
                return Flow.Completes;
            case AssignOrDeclare a:
                CheckAssignOrDeclare(a);
                return Flow.Completes;
            case Assignment a:
                CheckAssignment(a);
                return Flow.Completes;
            case ExpressionStatement e:
                CheckExpressionStatement(e);
                return Flow.Completes;
            case IfStatement i:
                {
                    CheckCondition(i.Condition);
                    var then = CheckBlock(i.Then);
                    var other = i.Else is null ? Flow.Completes : CheckStatement(i.Else);
                    return then == Flow.Ends && i.Else is not null && other == Flow.Ends ? Flow.Ends : Flow.Completes;
                }

            case WhileStatement w:
                CheckCondition(w.Condition);
                frame.LoopDepth++;
                CheckBlock(w.Body);
                frame.LoopDepth--;
                return Flow.Completes;
            case ForInStatement f:
                CheckForIn(f);
                return Flow.Completes;
            case MatchStatement m:
                {
                    var result = CheckMatch(m.Subject, m.Arms, isExpression: false, expected: null, m.Line);
                    return result.Exhaustive && result.AllArmsEnd ? Flow.Ends : Flow.Completes;
                }

            case BreakStatement b:
                if (frame.LoopDepth == 0)
                {
                    Report(ErrorIds.BreakOutsideLoop, b.Line, "`break` with no loop around it (C7)");
                }

                return Flow.Ends;
            case ContinueStatement c:
                if (frame.LoopDepth == 0)
                {
                    Report(ErrorIds.BreakOutsideLoop, c.Line, "`continue` with no loop around it (C7)");
                }

                return Flow.Ends;
            case ReturnStatement r:
                CheckReturn(r);
                return Flow.Ends;
            case ThrowStatement t:
                // O6, O7: a throw is an exception, which ends the actor and is no case of the function's result, so nothing is
                // listed for it (O3 lists the cases a call returns) and the top level may throw (O9 is about cases)
                if (t.Value is not null)
                {
                    var thrown = CheckExpression(t.Value, null);
                    if (Types.IsVoid(thrown.Type))
                    {
                        Report(ErrorIds.Syntax, t.Line, "a call with a `void` success used as a value (O10)");
                    }
                }
                else if (!inElseArm)
                {
                    Report(ErrorIds.ThrowNeedsValue, t.Line, "a bare `throw` stands only in an arm of `else` (O7)");
                }

                return Flow.Ends;
            case RawBlock r:
                return CheckRawBlock(r);

            case UseStatement u:
                if (!uses.Contains(u))
                {
                    NotSupported(u.Line, "use (F10)");
                }

                return Flow.Completes;
            case ExternBlock e:
                // R16: a block stands at the top level of a file alone, as the parser holds it; its declarations were declared and resolved in the first passes
                if (!allowNative)
                {
                    Report(ErrorIds.NativeNotAllowed, e.Line, "a block of C functions needs the grant `allow native`, which this package does not have (R16)");
                }

                return Flow.Completes;
            case AsmStatement a:
                CheckAsm(a, naked: false);
                return Flow.Completes;
            case FunctionDeclaration f:
                if (blockDepth > 0)
                {
                    NotSupported(f.Line, "a function declared inside a block (F1, F6)"); // the top level's are declared in the first passes
                    Push();
                    ReadNestedFunction(f); // what stands inside is read with its diagnostics dropped, so that the variables it reads count as used (E1)
                    Pop(commit: false);
                }

                return Flow.Completes;
            case TypeDeclaration t:
                if (blockDepth > 0)
                {
                    NotSupported(t.Line, "a type declared inside a block (D1, F6)");
                    Push();
                    ReadNestedType(t);
                    Pop(commit: false);
                }

                return Flow.Completes;
            case FieldDeclaration f:
                // the parser reads a field and a constructor in a type body only (K7, K14), whose members the checker does not read (D16, chapter 7)
                NotSupported(f.Line, "a field of a type body (K7)");
                return Flow.Completes;
            case ConstructorDeclaration c:
                NotSupported(c.Line, "a constructor of a type body (K14)");
                return Flow.Completes;
            default:
                throw new InvalidOperationException(statement.GetType().Name);
        }
    }

    /// <summary>R11: the values, places and blocks of an <c>asm</c>'s operands, read with their diagnostics dropped, so that the variables they use count as used (E1).</summary>
    private void ReadAsmLoosely(AsmStatement a)
    {
        Push();
        foreach (var operand in a.Operands)
        {
            foreach (var e in new[] { operand.Value, operand.Output })
            {
                if (e is not null)
                {
                    CheckExpression(e, null);
                }
            }

            if (operand.Body is not null)
            {
                CheckBlock(operand.Body);
            }
        }

        Pop(commit: false);
    }

    /// <summary>F1 with F6: the body of a function declared inside a block, read as a lambda's is, with its parameters error-typed, for the variables around it that it reads.</summary>
    private void ReadNestedFunction(FunctionDeclaration f)
    {
        var outer = frame;
        frame = new Frame
        {
            IsLambda = true,
            Outer = outer,
            CapturedScope = outer.Scope,
            ReturnType = Types.Error,
            Line = f.Line,
            TypeParameters = [.. f.TypeParameters.Select(p => new TypeParameterSymbol(p.Name, p.Line)), .. outer.TypeParameters],
            Reserved = outer.Reserved,
        };
        foreach (var p in f.Parameters)
        {
            frame.Scope.Variables[p.Name] = new VariableSymbol(p.Name, Types.Error, p.Mut, VariableKind.Parameter, p.Line);
        }

        if (f.ExpressionBody is not null)
        {
            CheckExpression(f.ExpressionBody, null);
        }
        else if (f.BlockBody is not null)
        {
            CheckBlock(f.BlockBody);
        }

        frame = outer;
    }

    /// <summary>F1 with F6: the defaults, field values and methods of a type declared inside a block, read for the variables around it that they read.</summary>
    private void ReadNestedType(TypeDeclaration t)
    {
        foreach (var p in t.PrimaryConstructor ?? [])
        {
            if (p.Default is not null)
            {
                CheckExpression(p.Default, null);
            }
        }

        foreach (var member in t.Members ?? [])
        {
            switch (member)
            {
                case FunctionDeclaration f:
                    ReadNestedFunction(f);
                    break;
                case FieldDeclaration { Value: not null } field:
                    CheckExpression(field.Value, null);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>C3: a condition is a <c>bool</c>.</summary>
    private void CheckCondition(Expression condition)
    {
        var t = CheckExpression(condition, Types.Bool);
        if (Types.IsVoid(t.Type))
        {
            Report(ErrorIds.Syntax, condition.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!t.IsError && t.Type is not PrimitiveType { Kind: Primitive.Bool })
        {
            Report(ErrorIds.TypeMismatch, condition.Line, $"a condition is a `bool`, not a `{t.Type}` (C3)");
        }
    }

    private void CheckVariableDeclaration(VariableDeclaration v)
    {
        if (IsState(v) && frame.IsTopLevel && frame.Scope.Parent is null)
        {
            return; // R13: state outside a function, declared and checked before any function's body (CheckStates); a `static` without `mut` has no meaning there, as on any variable (below)
        }

        var other = v.Modifiers & ~Modifiers.Mut;
        if (other != Modifiers.None)
        {
            Report(ErrorIds.Syntax, v.Line, $"`{other.ToString().ToLowerInvariant()}` has no meaning on a variable (V2, V4)");
        }

        if (v.Value is null)
        {
            DeclareUnreadable(v.Name, VariableKind.Local, v.Line, v); // a `static mut` without a value, which has no meaning here: its uses carry nothing more (E1)
            return;
        }

        KurzType? declared = null;
        if (v.Type is not null)
        {
            declared = Resolve(v.Type);
            if (Types.IsVoid(declared))
            {
                Report(ErrorIds.Syntax, v.Line, "`void` is no type of a variable (F4)");
                declared = Types.Error;
            }
        }

        if (Redeclares(v.Name, v.Line))
        {
            DeclareUnreadable(v.Name, VariableKind.Local, v.Line, v); // bound before the value is read: the uses of a redeclared name, in the value too, carry no further error (E1)
            CheckExpression(v.Value, declared, keepCases: declared is UnionType);
            return;
        }

        var t = CheckExpression(v.Value, declared, keepCases: declared is UnionType);
        var type = declared ?? t.Type;
        if (Types.IsVoid(t.Type))
        {
            Report(ErrorIds.Syntax, v.Line, "a call with a `void` success used as a value (O10)");
            type = Types.Error;
        }
        else if (declared is not null && !Types.Assignable(t.Type, declared))
        {
            Report(Types.MismatchId(t.Type, declared), v.Value.Line, $"a `{t.Type}` where a `{declared}` is required (T1)");
        }

        Declare(v.Name, type, v.Mut, VariableKind.Local, v.Line, v);
    }

    /// <summary>V8: an assignment where the name is visible, a declaration where it is not; F16 inside a top-level function.</summary>
    private void CheckAssignOrDeclare(AssignOrDeclare a)
    {
        var (variable, captured) = Lookup(a.Name);
        if (variable is not null)
        {
            RequireRawForState(variable, a.Line);
            if (captured)
            {
                Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{a.Name}` and cannot assign it (F11)");
            }
            else if (!variable.Mut)
            {
                Report(ErrorIds.AssignImmutable, a.Line, $"`{a.Name}` was declared without `mut` (V7)");
            }
            else
            {
                changes[a] = new Change(variable, a.Name, a.Line, $"an assignment changes `{a.Name}`"); // M7, in an arm inside an expression
            }

            var value = CheckExpression(a.Value, variable.Type, keepCases: variable.Type is UnionType);
            if (Types.IsVoid(value.Type))
            {
                Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
            }
            else if (!Types.Assignable(value.Type, variable.Type))
            {
                Report(Types.MismatchId(value.Type, variable.Type), a.Value.Line, $"a `{value.Type}` where a `{variable.Type}` is required (T1)");
            }

            variable.Changed = true;
            model.RecordAssignment(a, variable);
            return;
        }

        if (frame.Reserved is not null && frame.Reserved.Contains(a.Name))
        {
            // F16: the declaration on the left is the one error; the read on the right reports nothing more
            Report(ErrorIds.Redeclared, a.Line, $"`{a.Name}` is a top-level variable, whose name a top-level function cannot declare (F16)");
            DeclareUnreadable(a.Name, VariableKind.Local, a.Line, a);
            CheckExpression(a.Value, null);
            return;
        }

        if (IsDeclaredName(a.Name))
        {
            Report(ErrorIds.Redeclared, a.Line, $"`{a.Name}` is visible already (V9)");
            CheckExpression(a.Value, null);
            return;
        }

        var t = CheckExpression(a.Value, null);
        var type = t.Type;
        if (Types.IsVoid(type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
            type = Types.Error;
        }

        Declare(a.Name, type, false, VariableKind.Local, a.Line, a);
    }

    /// <summary>M4, D4: an assignment into a path of fields from a variable; a path through an index is not read (M9's map is not read).</summary>
    private void CheckAssignment(Assignment a)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(a.Target);
        while (root is MemberAccess m)
        {
            if (m.Conditional)
            {
                NotSupported(m.Line, "`?.` (N5)");
                CheckLoosely(a.Target, a.Value);
                return;
            }

            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        if (root is UnaryExpression { Operator: TokenKind.Star } or IndexExpression)
        {
            CheckWriteThrough(a, root, members); // R10, or an index into a list (M9)
            return;
        }

        if (members.Count == 0)
        {
            Report(ErrorIds.Syntax, a.Line, "the left side of an assignment is a name or a path (V8, M4)");
            CheckExpression(a.Value, null);
            return;
        }

        KurzType current;
        VariableSymbol? changed = null;
        if (root is NameExpression name)
        {
            var (variable, captured) = Lookup(name.Name);
            if (variable is null)
            {
                CheckName(name); // reports what the name is
                CheckExpression(a.Value, null);
                return;
            }

            variable.Read = true; // V6: an assignment into a path reads the variable
            model.RecordName(name, variable);
            model.RecordType(name, new Typed(variable.Type));
            RequireRawForState(variable, a.Line);
            if (Types.IsError(variable.Type))
            {
                // a value the checker does not read, a class instance among them: whether the path changes is its chapter's question (K1, K2)
            }
            else if (captured)
            {
                Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{name.Name}` and cannot change it (F11)");
            }
            else if (!variable.Mut)
            {
                Report(ErrorIds.AssignImmutable, a.Line, $"`{name.Name}` was declared without `mut`: nothing reachable through it changes (D4, M4)");
            }
            else
            {
                changed = variable;
            }

            variable.Changed = true;
            current = variable.Type;
        }
        else
        {
            var t = CheckExpression(root, null);
            if (!t.IsError)
            {
                Report(ErrorIds.AssignImmutable, a.Line, "a path that is assigned starts at a `mut` variable (M4)");
            }

            current = Types.Error;
        }

        FieldSymbol? last = null;
        foreach (var m in members)
        {
            if (Types.IsError(current))
            {
                break;
            }

            if (current is DataType d && d.Symbol.Field(m.Member) is { } field)
            {
                last = field;
                current = SubstituteData(d, field.Type);
                model.RecordType(m, new Typed(current));
                continue;
            }

            // a member that is no field of a data value, of a data value or of any other: MemberOf reports a member the type does not have,
            // and one it has, a list's `Count` (M9), is assigned nowhere (M4)
            var member = MemberOf(current, m.Member, m.Line);
            if (!member.IsError)
            {
                Report(ErrorIds.AssignImmutable, m.Line, $"`{m.Member}` is no field of a data value and cannot be assigned (M4)");
            }

            current = Types.Error;
            last = null;
        }

        if (changed is not null && last is not null && !Types.IsError(current))
        {
            var path = string.Join(".", members.Select(m => m.Member).Prepend(changed.Name));
            changes[a] = new Change(changed, path, a.Line, $"an assignment changes `{path}`"); // M7, in an arm inside an expression
        }

        var value = CheckExpression(a.Value, Types.IsError(current) ? null : current, keepCases: current is UnionType);
        if (last is null || Types.IsError(current) || value.IsError)
        {
            return;
        }

        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, current))
        {
            Report(Types.MismatchId(value.Type, current), a.Value.Line, $"a `{value.Type}` where a `{current}` is required (T1)");
        }
    }

    /// <summary>Reads the parts of a statement the checker does not read, with their diagnostics dropped, so that the variables they use count as used (E1).</summary>
    private void CheckLoosely(params Expression[] expressions)
    {
        Push();
        foreach (var e in expressions)
        {
            CheckExpression(e, null);
        }

        Pop(commit: false);
    }

    private void CheckExpressionStatement(ExpressionStatement e)
    {
        switch (Unwrap(e.Expression))
        {
            case CallExpression c:
                RecordThrough(e.Expression, CheckWhole(e.Expression, () => CheckCall(c, null, keepCases: false)));
                break;
            case ElseExpression el:
                RecordThrough(e.Expression, CheckWhole(e.Expression, () => CheckElse(el, isStatement: true, expected: null)));
                break;
            case MatchExpression m:
                RecordThrough(e.Expression, new Typed(CheckMatch(m.Subject, m.Arms, isExpression: false, expected: null, m.Line).Type));
                break;
            default:
                // F5 lists what a statement is, and a value on its own is none: text no rule gives a meaning, E4's `syntax` (the reading in compiler/README.md)
                if (!CheckExpression(e.Expression, null).IsError)
                {
                    Report(ErrorIds.Syntax, e.Line, "a statement is a declaration, an assignment, a call or a statement form; a value on its own is none (F5, E4)");
                }

                break;
        }
    }

    private void CheckForIn(ForInStatement f)
    {
        KurzType element;
        if (Unwrap(f.Collection) is RangeExpression range)
        {
            element = CheckRange(range);
            RecordThrough(f.Collection, new Typed(element)); // a range has the type of its numbers (C8)
        }
        else
        {
            var t = CheckExpression(f.Collection, null);
            if (Types.IsVoid(t.Type))
            {
                this.Report(ErrorIds.Syntax, f.Collection.Line, "a call with a `void` success used as a value (O10)");
            }

            element = t.Type switch
            {
                ListType l => l.Element,
                ErrorType or VoidType => Types.Error,
                _ => Report(f.Collection.Line, t.Type),
            };
        }

        PushScope();
        DeclareChecked(f.Variable, element, false, VariableKind.Loop, f.VariableLine, f);
        frame.LoopDepth++;
        CheckBlock(f.Body);
        frame.LoopDepth--;
        PopScope();

        KurzType Report(int line, KurzType type)
        {
            this.Report(ErrorIds.TypeMismatch, line, $"a `for` loop walks a list or a range, not a `{type}` (C6, C8)");
            return Types.Error;
        }
    }

    private void CheckReturn(ReturnStatement r)
    {
        var expected = frame.ReturnType;
        if (expected is null)
        {
            // a lambda whose result is inferred from its first return (F7); O4 keeps a call's cases only as the subject of a match and in a
            // written union type, so a call returned here unwraps (O2), and the result inferred from its value lists no case (O3): the reading
            // in compiler/README.md
            if (r.Value is null)
            {
                frame.ReturnType = Types.Void;
                return;
            }

            var t = CheckExpression(r.Value, null);
            if (Types.IsVoid(t.Type))
            {
                Report(ErrorIds.Syntax, r.Line, "a call with a `void` success used as a value (O10)");
                frame.ReturnType = Types.Error;
                return;
            }

            frame.ReturnType = t.Type;
            return;
        }

        if (Types.IsVoid(expected))
        {
            if (r.Value is not null && !CheckExpression(r.Value, null).IsError)
            {
                Report(ErrorIds.TypeMismatch, r.Line, "a `void` function returns no value (F3, F4)");
            }

            return;
        }

        if (r.Value is null)
        {
            if (!Types.IsError(expected) && !SuccessIsVoid(expected))
            {
                Report(ErrorIds.MissingReturn, r.Line, "a bare `return` in a function with a result (F3)");
            }

            return;
        }

        var value = CheckExpression(r.Value, expected, keepCases: expected is UnionType);
        if (value.IsError || Types.IsError(expected))
        {
            return;
        }

        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, r.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, expected))
        {
            Report(Types.MismatchId(value.Type, expected), r.Value.Line, $"a `{value.Type}` is returned where the result is a `{expected}` (F3, T1)"); // E1: at the value's line, as for every value that does not fit
        }
    }

    /// <summary>
    /// O2, O3, O9: the cases a call can leave the calling function with have to be listed in its return type; the top level
    /// has none. A case is listed as a case after the first one, the success (O3 with O1), so a return type that is no union
    /// lists none; a case after the first lists a case that is its type, one that widens into it (the owner, 2026-10-21) and one
    /// that derives from it (the record's reading); a case that is one of the cases after the first goes there, and no other fit is looked for, and a
    /// case that would otherwise go to two cases after the first is listed by none, as a value that would go to two cases
    /// stands nowhere (O3 with T8 and D6, the owner, 2026-10-21; <see cref="Types.CasesFor"/>).
    /// </summary>
    private void Propagate(IEnumerable<KurzType> cases, int line)
    {
        var list = cases.ToList();
        if (list.Count == 0)
        {
            return;
        }

        if (frame.IsTopLevel)
        {
            Report(ErrorIds.UnlistedCase, line, $"top-level code has no caller to pass `{list[0]}` to: handle every case at the call (O9)");
            return;
        }

        var returnType = frame.ReturnType;
        var listed = returnType is UnionType u ? u.Cases.Skip(1).ToList() : [];
        var unlisted = list.Select(c => (Case: c, Goes: Types.CasesFor(c, listed))).Where(c => c.Goes.Count != 1).ToList();
        if (unlisted.Count > 0 && !(returnType is not null && Types.IsError(returnType)))
        {
            var (first, goes) = unlisted[0];
            if (goes.Count > 1)
            {
                Report(ErrorIds.UnlistedCase, line, $"the case `{first}` can leave this function and would go to {goes.Count} cases of its return type after the success, {string.Join(" and ", goes.Select(g => $"`{g}`"))}: a `match` on the call picks one (O3, T8, D6)");
                return;
            }

            if (returnType is null)
            {
                Report(ErrorIds.UnlistedCase, line, $"the case `{first}` can leave this lambda, whose result is inferred from its value and lists no case (O3, F7)");
                return;
            }

            var where = returnType is UnionType ? "does not list it after its first case, the success" : "is no union and lists no case beside its success";
            Report(ErrorIds.UnlistedCase, line, $"the case `{first}` can leave this function, whose return type {where} (O3, O1)");
        }
    }
}
