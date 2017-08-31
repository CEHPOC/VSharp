﻿namespace VSharp

open JetBrains.Decompiler.Ast
open global.System
open System.Collections.Generic
open Types.Constructor

[<StructuralEquality;NoComparison>]
type FunctionIdentifier =
    | MetadataMethodIdentifier of JetBrains.Metadata.Reader.API.IMetadataMethod
    | DelegateIdentifier of JetBrains.Decompiler.Ast.INode
    | StandardFunctionIdentifier of Operations.StandardFunction
    override this.ToString() =
        match this with
        | MetadataMethodIdentifier mm -> mm.Name
        | DelegateIdentifier _ -> "<delegate>"
        | StandardFunctionIdentifier sf -> sf.ToString()

type StackKey = string * string  // Name and token

type LocationBinding = JetBrains.Decompiler.Ast.INode
type StackHash = int list
type TermMetadataEntry = {location : LocationBinding; stack : StackHash}
type TermMetadata = TermMetadataEntry list

[<StructuralEquality;NoComparison>]
type public Operation =
    | Operator of OperationType * bool
    | Application of FunctionIdentifier
    | Cast of TermType * TermType * bool
    member this.priority =
        match this with
        | Operator (op, _) -> Operations.operationPriority op
        | Application _ -> Operations.maxPriority
        | Cast _ -> Operations.maxPriority - 1

[<StructuralEquality;NoComparison>]
type public TermNode =
    | Nop
    | Error of Term
    | Concrete of Object * TermType
    | Constant of string * SymbolicConstantSource * TermType
    | Array of Term array               // Lower bounds
                * Term option           // Symbolic constant (or None if array has default contents)
                * SymbolicHeap          // Contents
                * Term array            // Lengths of dimensions
                * TermType              // Type
    | Expression of (Operation * Term list * TermType)
    | Struct of SymbolicHeap * TermType
    | StackRef of StackKey * (Term * TermType) list
    | HeapRef of (Term * TermType) NonEmptyList * Timestamp
    | StaticRef of string * (Term * TermType) list
    | Union of (Term * Term) list

    override this.ToString() =
        let checkExpression curChecked parentChecked priority parentPriority str =
            match curChecked, parentChecked with
            | true, _ when curChecked <> parentChecked -> sprintf "checked(%s)" str
            | false, _ when curChecked <> parentChecked -> sprintf "unchecked(%s)" str
            | _ when priority < parentPriority -> sprintf "(%s)" str
            | _ -> str

        let isCheckNeed curChecked parentChecked = if curChecked <> parentChecked then curChecked else parentChecked

        let formatIfNotEmpty (indent : String) (value : String) = if value.Length = 0 then "" else sprintf "\n%s%s" indent value

        let arrayContentsToString contents separator =
            Heap.toString "%O: %O" separator id id contents

        let rec toStr parentPriority parentChecked indent term =
            let getTerm (term : Term) = term.term in
            match term with
            | Error e -> sprintf "<ERROR: %O>" e
            | Nop -> "<VOID>"
            | Constant(name, _, _) -> name
            | Concrete(lambda, t) when Types.IsFunction t -> sprintf "<Lambda Expression %O>" t
            | Concrete(null, _) -> "null"
            | Concrete(value, _) -> value.ToString()
            | Expression(operation, operands, _) ->
                match operation with
                | Operator(operator, isChecked) when Operations.operationArity operator = 1 ->
                    assert (List.length operands = 1)
                    let operand = List.head operands in
                    let opStr = Operations.operationToString operator |> checkExpression isChecked parentChecked operation.priority parentPriority in
                    let printedOperand = toStr operation.priority (isCheckNeed isChecked parentChecked) indent operand.term in
                    sprintf (Printf.StringFormat<string->string>(opStr)) printedOperand
                | Operator(operator, isChecked) ->
                    assert (List.length operands >= 2)
                    let printedOperands = operands |> List.map (getTerm >> toStr operation.priority (isCheckNeed isChecked parentChecked) indent)
                    let sortedOperands = if Operations.isCommutative operator && not isChecked then List.sort printedOperands else printedOperands
                    sortedOperands
                        |> String.concat (Operations.operationToString operator)
                        |> checkExpression isChecked parentChecked operation.priority parentPriority
                | Cast(orig, dest, isChecked) ->
                    assert (List.length operands = 1)
                    sprintf "(%O)%s" dest (toStr operation.priority (isCheckNeed isChecked parentChecked) indent (List.head operands).term) |>
                        checkExpression isChecked parentChecked operation.priority parentPriority
                | Application f -> operands |> List.map (getTerm >> toStr -1 parentChecked indent) |> join ", " |> sprintf "%O(%s)" f
            | Struct(fields, t) ->
                let fieldsString = Heap.toString "| %O ~> %O" ("\n" + indent) id (getTerm >> toStr -1 false indent) fields in
                sprintf "STRUCT %O[%s]" t (formatIfNotEmpty indent fieldsString)
            | Array(_, None, contents, dimensions, _) ->
                sprintf "[| %s ... %s ... |]" (arrayContentsToString contents "; ") (Array.map toString dimensions |> join " x ")
            | Array(_, Some constant, contents, dimensions, _) ->
                sprintf "%O: [| %s (%s) |]" constant (arrayContentsToString contents "; ") (Array.map toString dimensions |> join " x ")
            | StackRef(key, path) -> sprintf "(StackRef (%O, %O))" key (List.map fst path)
            | HeapRef(path, _) -> sprintf "(HeapRef %s)" (path |> NonEmptyList.toList |> List.map (fst >> getTerm >> toStr -1 false indent) |> join ".")
            | StaticRef(key, path) -> sprintf "(StaticRef (%O, %O))" key (List.map fst path)
            | Union(guardedTerms) ->
                let guardedToString (guard, term) = sprintf "| %s ~> %s" (toStr -1 false indent guard.term) (toStr -1 false indent term.term)
                let printed = guardedTerms |> Seq.map guardedToString |> Seq.sort |> join ("\n" + indent)
                sprintf "UNION[%s]" (formatIfNotEmpty indent printed)
        in
        toStr -1 false "\t" this

and
    [<CustomEquality;NoComparison>]
    TermRef =
        {reference : Term ref}
        override this.GetHashCode() =
            Microsoft.FSharp.Core.LanguagePrimitives.PhysicalHash(this)
        override this.Equals(o : obj) =
            match o with
            | :? TermRef as other -> this.GetHashCode() = other.GetHashCode()
            | _ -> false

and
    [<CustomEquality;NoComparison>]
    Term =
        {term : TermNode; metadata : TermMetadata}
        override this.ToString() =
            this.term.ToString()
        override this.GetHashCode() =
            this.term.GetHashCode()
        override this.Equals(o : obj) =
            match o with
            | :? Term as other -> this.term.Equals(other.term)
            | _ -> false

and SymbolicConstantSource() =
    override this.GetHashCode() =
        this.GetType().GetHashCode()
    override this.Equals(o : obj) = o.GetType() = this.GetType()

and SymbolicHeap = Heap<Term, Term>

[<AutoOpen>]
module public Terms =

    module Metadata =
        let empty = List.empty
        let combine m1 m2 = List.append m1 m2 |> List.distinct
        let combine3 m1 m2 m3 = List.append3 m1 m2 m3 |> List.distinct

    let public term (term : Term) = term.term

    let public Nop = { term = Nop; metadata = Metadata.empty }
    let public Error term metadata = { term = Error term; metadata = metadata }
    let public Concrete obj typ metadata = { term = Concrete(obj, typ); metadata = metadata }
    let public Constant name source typ metadata = { term = Constant(name, source, typ); metadata = metadata }
    let public Array lower constant contents lengths typ metadata = { term = Array(lower, constant, contents, lengths, typ); metadata = metadata }
    let public Expression op args typ metadata = { term = Expression(op, args, typ); metadata = metadata }
    let public Struct fields typ metadata = { term = Struct(fields, typ); metadata = metadata }
    let public StackRef key path metadata = { term = StackRef(key, path); metadata = metadata }
    let public HeapRef path time metadata = { term = HeapRef(path, time); metadata = metadata }
    let public StaticRef key path metadata = { term = StaticRef(key, path); metadata = metadata }
    let public Union metadata gvs = { term = Union gvs; metadata = metadata }


    let public IsVoid = term >> function
        | Nop -> true
        | _ -> false

    let public IsError = term >> function
        | Error _ -> true
        | _ -> false

    let public IsConcrete = term >> function
        | Concrete _ -> true
        | _ -> false

    let public IsExpression = term >> function
        | Expression _ -> true
        | _ -> false

    let public IsArray = term >> function
        | Array _ -> true
        | _ -> false

    let public IsUnion = term >> function
        | Union _ -> true
        | _ -> false

    let public IsTrue = term >> function
        | Concrete(b, t) when Types.IsBool t && (b :?> bool) -> true
        | _ -> false

    let public IsFalse = term >> function
        | Concrete(b, t) when Types.IsBool t && not (b :?> bool) -> true
        | _ -> false

    let rec public Just predicate term =
        predicate term ||
            match term.term with
            | Union gvs -> List.forall predicate (List.map snd gvs)
            | _ -> false

    let public IsNull = term >> function
        | Concrete(null, _) -> true
        | _ -> false

    let public IsStackRef = term >> function
        | StackRef _ -> true
        | _ -> false

    let public IsHeapRef = term >> function
        | HeapRef _ -> true
        | _ -> false

    let rec public IsRef term =
        match term.term with
        | HeapRef _
        | StackRef _ -> true
        | Union gvs -> List.forall (snd >> IsRef) gvs
        | _ -> false

    let public OperationOf = term >> function
        | Expression(op, _, _) -> op
        | term -> internalfailf "expression expected, %O recieved" term

    let public ArgumentsOf = term >> function
        | Expression(_, args, _) -> args
        | term -> internalfailf "expression expected, %O recieved" term

    let rec public TypeOf term =
        match term.term with
        | Error _ -> TermType.Bottom
        | Nop -> TermType.Void
        | Concrete(_, t) -> t
        | Constant(_, _, t) -> t
        | Expression(_, _, t) -> t
        | Struct(_, t) -> t
        | StackRef _
        | StaticRef _ -> PointerType VSharp.Void // TODO: this is temporary hack, support normal typing
        | HeapRef(addrs, _) ->
            addrs |> NonEmptyList.toList |> List.last |> snd |> PointerType
        | Array(_, _, _, _, t) -> t
        | Union gvs ->
            match (List.filter (fun t -> not (Types.IsBottom t || Types.IsVoid t)) (List.map (snd >> TypeOf) gvs)) with
            | [] -> TermType.Bottom
            | t::ts ->
                let allSame = List.forall ((=) t) ts || Types.IsPointer t && List.forall Types.IsPointer ts in
                if allSame then t
                else
                    // TODO: return least common supertype!
                    __notImplemented__()


    let public IsBool =                 TypeOf >> Types.IsBool
    let public IsInteger =              TypeOf >> Types.IsInteger
    let public IsReal =                 TypeOf >> Types.IsReal
    let public IsNumeric =              TypeOf >> Types.IsNumeric
    let public IsString =               TypeOf >> Types.IsString
    let public IsFunction =             TypeOf >> Types.IsFunction
    let public IsPrimitive =            TypeOf >> Types.IsPrimitive
    let public DomainOf =               TypeOf >> Types.DomainOf
    let public RangeOf =                TypeOf >> Types.RangeOf
    let public IsRelation =             TypeOf >> Types.IsRelation

    let public CastConcrete value (t : System.Type) metadata =
        let actualType = if box value = null then t else value.GetType() in
        try
            if actualType = t then
                Concrete value (FromConcreteDotNetType t) metadata
            elif typedefof<IConvertible>.IsAssignableFrom(actualType) then
                let casted =
                    if t.IsPointer
                    then new IntPtr(Convert.ChangeType(value, typedefof<int64>) :?> int64) |> box
                    else Convert.ChangeType(value, t) in
                Concrete casted (FromConcreteDotNetType t) metadata
            elif t.IsAssignableFrom(actualType) then 
                Concrete value (FromConcreteDotNetType t) metadata
            else raise(new InvalidCastException(sprintf "Cannot cast %s to %s!" t.FullName actualType.FullName))
        with
        | _ ->
            internalfailf "cannot cast %s to %s!" t.FullName actualType.FullName

    let public MakeTrue metadata =
        Concrete (box true) Bool metadata

    let public MakeFalse metadata =
        Concrete (box false) Bool metadata

    let public True = MakeTrue Metadata.empty

    let public False = MakeFalse Metadata.empty

    let public MakeBool predicate metadata =
        if predicate then MakeTrue metadata else MakeFalse metadata

    let public MakeNull typ metadata =
        Concrete null (FromConcreteDotNetType typ) metadata

    let public MakeNumber n metadata =
        Concrete n (Numeric(n.GetType())) metadata

    let public MakeConcreteString (s : string) metadata =
        Concrete s VSharp.String metadata

    let public MakeBinary operation x y isChecked t metadata =
        assert(Operations.isBinary operation)
        Expression (Operator(operation, isChecked)) [x; y] t metadata

    let public MakeNAry operation x isChecked t metadata =
        match x with
        | [] -> raise(new ArgumentException("List of args should be not empty"))
        | [x] -> x
        | _ -> Expression (Operator(operation, isChecked)) x t metadata

    let public MakeUnary operation x isChecked t metadata =
        assert(Operations.isUnary operation)
        Expression (Operator(operation, isChecked)) [x] t metadata

    let public MakeStringKey typeName =
        MakeConcreteString typeName Metadata.empty

    let public Negate term metadata =
        assert(IsBool term)
        MakeUnary OperationType.LogicalNeg term false Bool metadata


    let (|True|_|) term = if IsTrue term then Some True else None
    let (|False|_|) term = if IsFalse term then Some False else None
    let (|Null|_|) term = if IsNull term then Some Null else None

    let (|ConcreteT|_|) = term >> function
        | Concrete(name, typ) -> Some(ConcreteT(name, typ))
        | _ -> None

    let (|ErrorT|_|) = term >> function
        | Error e -> Some(ErrorT e)
        | _ -> None

    let (|UnionT|_|) = term >> function
        | Union gvs -> Some(UnionT gvs)
        | _ -> None

    let (|GuardedValues|_|) = function
        | Union gvs -> Some(GuardedValues(List.unzip gvs))
        | _ -> None

    let (|UnaryMinus|_|) = function
        | Expression(Operator(OperationType.UnaryMinus, isChecked), [x], t) -> Some(UnaryMinus(x, isChecked, t))
        | _ -> None

    let (|UnaryMinusT|_|) = term >> (|UnaryMinus|_|)

    let (|Add|_|) = term >> function
        | Expression(Operator(OperationType.Add, isChecked), [x;y], t) -> Some(Add(x, y, isChecked, t))
        | _ -> None

    let (|Sub|_|) = term >> function
        | Expression(Operator(OperationType.Subtract, isChecked), [x;y], t) -> Some(Sub(x, y, isChecked, t))
        | _ -> None

    let (|Mul|_|) = term >> function
        | Expression(Operator(OperationType.Multiply, isChecked), [x;y], t) -> Some(Mul(x, y, isChecked, t))
        | _ -> None

    let (|Div|_|) = term >> function
        | Expression(Operator(OperationType.Divide, isChecked), [x;y], t) -> Some(Div(x, y, isChecked, t))
        | _ -> None

    let (|Rem|_|) = term >> function
        | Expression(Operator(OperationType.Remainder, isChecked), [x;y], t) -> Some(Rem(x, y, isChecked, t))
        | _ -> None

    let (|Negation|_|) = function
        | Expression(Operator(OperationType.LogicalNeg, _), [x], t) -> Some(Negation(x, t))
        | _ -> None

    let (|NegationT|_|) = term >> (|Negation|_|)

    let (|Conjunction|_|) = term >> function
        | Expression(Operator(OperationType.LogicalAnd, _), [x;y], t) -> Some(Conjunction(x, y, t))
        | _ -> None

    let (|ConjunctionList|_|) = function
        | Expression(Operator(OperationType.LogicalAnd, _), xs, t) -> Some(ConjunctionList(xs, t))
        | _ -> None

    let (|Disjunction|_|) = term >> function
        | Expression(Operator(OperationType.LogicalOr, _), [x;y], t) -> Some(Disjunction(x, y, t))
        | _ -> None

    let (|DisjunctionList|_|) = function
        | Expression(Operator(OperationType.LogicalOr, _), xs, t) -> Some(DisjunctionList(xs, t))
        | _ -> None

    let (|Xor|_|) = term >> function
        | Expression(Operator(OperationType.LogicalXor, _), [x;y], t) -> Some(Xor(x, y, t))
        | _ -> None

    let (|ShiftLeft|_|) = term >> function
        | Expression(Operator(OperationType.ShiftLeft, isChecked), [x;y], t) -> Some(ShiftLeft(x, y, isChecked, t))
        | _ -> None

    let (|ShiftRight|_|) = term >> function
        | Expression(Operator(OperationType.ShiftRight, isChecked), [x;y], t) -> Some(ShiftRight(x, y, isChecked, t))
        | _ -> None

    let rec private addConstants mapper (visited : HashSet<Term>) acc term =
        match term.term with
        | Constant(name, source, t) when visited.Add(term) ->
            match mapper acc term with
            | Some value -> value::acc
            | None -> acc
        | Array(lowerBounds, constant, contents, lengths, _) ->
            match constant with
            | Some c -> addConstants mapper visited acc c
            | None -> acc
            |> addConstantsMany mapper visited (Seq.ofArray lowerBounds)
            |> addConstantsMany mapper visited (Heap.locations contents)
            |> addConstantsMany mapper visited (Heap.values contents)
            |> addConstantsMany mapper visited lengths
        | Expression(_, args, _) ->
            addConstantsMany mapper visited args acc
        | Struct(fields, _) ->
            addConstantsMany mapper visited (Heap.values fields) acc
        | HeapRef(path, _) ->
            addConstantsMany mapper visited (NonEmptyList.toList path |> Seq.map fst) acc
        | GuardedValues(gs, vs) ->
            addConstantsMany mapper visited gs acc |> addConstantsMany mapper visited vs
        | Error e ->
            addConstants mapper visited acc e
        | _ -> acc

    and private addConstantsMany mapper visited terms acc =
        Seq.fold (addConstants mapper visited) acc terms

    let public filterMapConstants mapper terms =
        List.fold (addConstants mapper (new HashSet<Term>())) [] terms
