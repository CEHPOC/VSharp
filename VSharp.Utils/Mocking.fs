namespace VSharp

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Reflection
open System.Reflection.Emit
open System.Xml.Serialization
open Microsoft.FSharp.Collections
open VSharp

[<CLIMutable>]
[<Serializable>]
[<XmlInclude(typeof<typeRepr>)>]
type methodRepr = {
    declaringType : typeRepr
    token : int
}
with
    member x.Decode() =
        let declaringType = Serialization.decodeType x.declaringType
        declaringType.GetMethods() |> Seq.find (fun m -> m.MetadataToken = x.token)

    static member Encode(m : MethodBase) : methodRepr = {
        declaringType = Serialization.encodeType m.DeclaringType
        token = m.MetadataToken
    }

[<CLIMutable>]
[<Serializable>]
[<XmlInclude(typeof<structureRepr>)>]
[<XmlInclude(typeof<referenceRepr>)>]
[<XmlInclude(typeof<pointerRepr>)>]
[<XmlInclude(typeof<arrayRepr>)>]
[<XmlInclude(typeof<enumRepr>)>]
[<XmlInclude(typeof<methodRepr>)>]
type typeMockRepr = {
    name : string
    baseClass : typeRepr
    interfaces : typeRepr array
    baseMethods : methodRepr array
    methodImplementations : obj array array
}
with
    static member NullRepr = {name = null; baseClass = Serialization.encodeType null; interfaces = [||]; baseMethods = [||]; methodImplementations = [||]}

module Mocking =

    exception UnexpectedMockCallException of string

    let storageFieldName (method : MethodInfo) = $"{method.Name}{method.MethodHandle.Value}_<Storage>"
    let counterFieldName (method : MethodInfo) = $"{method.Name}{method.MethodHandle.Value}_<Counter>"

    // TODO: properties!
    type Method(baseMethod : MethodInfo, clausesCount : int) =
        let returnValues : obj[] = Array.zeroCreate clausesCount
        let name = baseMethod.Name
        let storageFieldName = storageFieldName baseMethod
        let counterFieldName = counterFieldName baseMethod
        let mutable returnType = baseMethod.ReturnType

        do
            let hasOutParameter =
                baseMethod.GetParameters()
                |> Array.exists (fun x -> x.IsOut)

            if hasOutParameter then internalfail "Method with out parameters mocking not implemented"

        member x.BaseMethod = baseMethod
        member x.ReturnValues = returnValues

        member x.SetClauses (clauses : obj[]) =
            clauses |> Array.iteri (fun i o -> returnValues.[i] <- o)

        member x.InitializeType (typ : Type) =
            if returnType <> typeof<Void> then
                let field = typ.GetField(storageFieldName, BindingFlags.NonPublic ||| BindingFlags.Static)
                if field = null then
                    internalfail $"Could not detect field %s{storageFieldName} of mock!"
                let storage = Array.CreateInstance(returnType, clausesCount)
                Array.Copy(returnValues, storage, clausesCount)
                field.SetValue(null, storage)

        member x.Build (typeBuilder : TypeBuilder) =
            let typeIsDelegate = TypeUtils.isDelegate baseMethod.DeclaringType
            let methodAttributes = MethodAttributes.Public ||| MethodAttributes.HideBySig
            let virtualFlags = MethodAttributes.Virtual ||| MethodAttributes.NewSlot ||| MethodAttributes.Final
            let methodAttributes =
                // For delegate mock, there is no need to make method virtual,
                // cause we can not derive from delegate
                if typeIsDelegate then methodAttributes
                else methodAttributes ||| virtualFlags

            let methodBuilder =
                typeBuilder.DefineMethod(baseMethod.Name, methodAttributes, CallingConventions.HasThis)
            if baseMethod.IsGenericMethod then
                let baseGenericArgs = baseMethod.GetGenericArguments()
                let genericsBuilder = methodBuilder.DefineGenericParameters(baseGenericArgs |> Array.map (fun p -> p.Name))
                baseGenericArgs |> Array.iteri (fun i p ->
                    let constraints = p.GetGenericParameterConstraints()
                    let builder = genericsBuilder.[i]
                    let interfaceConstraints = constraints |> Array.filter (fun c -> if c.IsInterface then true else builder.SetBaseTypeConstraint c; false)
                    if interfaceConstraints.Length > 0 then
                        builder.SetInterfaceConstraints interfaceConstraints)
                let rec convertType (typ : Type) =
                    if typ.IsGenericMethodParameter then genericsBuilder.[Array.IndexOf(baseGenericArgs, typ)] :> Type
                    elif typ.IsGenericType then
                        let args = typ.GetGenericArguments()
                        let args' = args |> Array.map convertType
                        if args = args' then typ
                        else
                            typ.GetGenericTypeDefinition().MakeGenericType(args')
                    else typ
                methodBuilder.SetReturnType (convertType baseMethod.ReturnType)
                let parameters = baseMethod.GetParameters() |> Array.map (fun p -> convertType p.ParameterType)
                methodBuilder.SetParameters(parameters)
            else
                methodBuilder.SetReturnType baseMethod.ReturnType
                methodBuilder.SetParameters(baseMethod.GetParameters() |> Array.map (fun p -> p.ParameterType))
            returnType <- methodBuilder.ReturnType

            if not typeIsDelegate then
                typeBuilder.DefineMethodOverride(methodBuilder, baseMethod)

            let ilGenerator = methodBuilder.GetILGenerator()

            if returnType <> typeof<Void> then
                let storageField = typeBuilder.DefineField(storageFieldName, returnType.MakeArrayType(), FieldAttributes.Private ||| FieldAttributes.Static)
                let counterField = typeBuilder.DefineField(counterFieldName, typeof<int>, FieldAttributes.Private ||| FieldAttributes.Static)

                let normalCase = ilGenerator.DefineLabel()
                let count = returnValues.Length

                ilGenerator.Emit(OpCodes.Ldsfld, counterField)
                ilGenerator.Emit(OpCodes.Ldc_I4, count)
                ilGenerator.Emit(OpCodes.Blt, normalCase)

                ilGenerator.Emit(OpCodes.Ldstr, name)
                ilGenerator.Emit(OpCodes.Newobj, typeof<UnexpectedMockCallException>.GetConstructor([|typeof<string>|]))
                ilGenerator.Emit(OpCodes.Throw)
                // Or we can return the defaultField:
                // let defaultFieldName = baseMethod.Name + "_<Default>"
                // let defaultField = typeBuilder.DefineField(defaultFieldName, returnType, FieldAttributes.Private ||| FieldAttributes.Static)
                // ilGenerator.Emit(OpCodes.Ldsfld, defaultField)
                // ilGenerator.Emit(OpCodes.Ret)

                ilGenerator.MarkLabel(normalCase)
                ilGenerator.Emit(OpCodes.Ldsfld, storageField)
                ilGenerator.Emit(OpCodes.Ldsfld, counterField)
                ilGenerator.Emit(OpCodes.Ldelem, returnType)

                ilGenerator.Emit(OpCodes.Ldsfld, counterField)
                ilGenerator.Emit(OpCodes.Ldc_I4_1)
                ilGenerator.Emit(OpCodes.Add)
                ilGenerator.Emit(OpCodes.Stsfld, counterField)

            ilGenerator.Emit(OpCodes.Ret)

    // TODO: properties!
    type Type(repr : typeMockRepr) = // Constructor for deserialization
        let deserializeMethod (m : methodRepr) (c : obj[]) = Method(m.Decode(), c.Length)
        let methods = ResizeArray<Method>(Array.map2 deserializeMethod repr.baseMethods repr.methodImplementations)
        let methodsInfo = ResizeArray<MethodInfo>()
        let initializedTypes = HashSet<System.Type>()

        let mutable baseClass : System.Type = Serialization.decodeType repr.baseClass
        let interfaces = ResizeArray<System.Type>(Array.map Serialization.decodeType repr.interfaces)

        // Constructor for serialization
        new(name : string) =
            let repr = {
                name = name
                baseClass = Serialization.encodeType null
                interfaces = [||]
                baseMethods = [||]
                methodImplementations = [||]
            }
            Type(repr)

        static member Empty = Type(String.Empty)

        member x.AddSuperType(t : System.Type) =
            if t.IsValueType || t.IsArray || t.IsPointer || t.IsByRef then
                raise (ArgumentException("Mock supertype should be class or interface!"))
            if t.IsInterface then
                interfaces.RemoveAll(fun u -> t.IsAssignableTo u) |> ignore
                interfaces.Add t
            elif baseClass = null then baseClass <- t
            elif baseClass.IsAssignableTo t then ()
            elif t.IsAssignableTo baseClass then baseClass <- t
            else raise (ArgumentException($"Attempt to assign another base class {t.FullName} for mock with base class {baseClass.FullName}! Note that multiple inheritance is prohibited."))

        member x.AddMethod(m : MethodInfo, returnValues : obj[]) =
            let methodMock = Method(m, returnValues.Length)
            methodMock.SetClauses returnValues
            methodsInfo.Add(m)
            methods.Add(methodMock)

        member x.Id = repr.name

        [<MaybeNull>]
        member x.BaseClass with get() = baseClass
        member x.Interfaces with get() = interfaces :> seq<_>
        member x.Methods with get() = methods :> seq<_>
        member x.MethodsInfo with get() = methodsInfo :> seq<_>

        member x.Build(moduleBuilder : ModuleBuilder) =
            let typeBuilder = moduleBuilder.DefineType(repr.name, TypeAttributes.Public)

            if baseClass <> null && not (TypeUtils.isDelegate baseClass) then
                typeBuilder.SetParent baseClass
                let baseHasNoDefaultCtor = baseClass.GetConstructor Type.EmptyTypes = null
                if baseHasNoDefaultCtor then
                    // Defining non-default ctor to eliminate the default one
                    let nonDefaultCtor = typeBuilder.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, [|typeof<int32>|])
                    let body = nonDefaultCtor.GetILGenerator()
                    body.Emit(OpCodes.Ret)

            interfaces |> ResizeArray.iter typeBuilder.AddInterfaceImplementation

            methods |> ResizeArray.iter (fun methodMock -> methodMock.Build typeBuilder)
            typeBuilder.CreateType()

        member x.Serialize(encode : obj -> obj) =
            let allInterfaces =
                Seq.collect TypeUtils.getBaseInterfaces interfaces
                |> Seq.append interfaces
                |> Seq.distinct
                |> Seq.toArray
            let interfaces = ResizeArray.toArray interfaces
            let interfaceMethods = allInterfaces |> Array.collect (fun i -> i.GetMethods())
            let methodsToImplement =
                match baseClass with
                | null -> interfaceMethods
                | _ ->
                    let bindingFlags =
                        BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.FlattenHierarchy ||| BindingFlags.Instance
                    let superClassMethods = baseClass.GetMethods(bindingFlags)
                    let isDelegate = TypeUtils.isDelegate baseClass
                    let needToMock (m : MethodInfo) =
                        // If base class abstract methods, need to mock them
                        // If base class is delegate, need to mock 'Invoke' method to create delegate
                        m.IsAbstract || isDelegate && m.Name = "Invoke"
                    let neededMethods = superClassMethods |> Array.filter needToMock
                    Array.append neededMethods interfaceMethods

            let getMock m = Method(m, 0)
            let implementedMethods = ResizeArray.toArray methods
            let abstractMethods = methodsToImplement |> Array.except methodsInfo |> Array.map getMock
            let methods = Array.append abstractMethods implementedMethods

            { name = repr.name
              baseClass = Serialization.encodeType baseClass
              interfaces = interfaces |> Array.map Serialization.encodeType
              baseMethods = methods |> Array.map (fun m -> methodRepr.Encode m.BaseMethod)
              methodImplementations = methods |> Array.map (fun m -> m.ReturnValues |> Array.map encode) }

        // Is used to initialize mock clauses if it was not initialized
        member x.EnsureInitialized (decode : obj -> obj) (t : System.Type) =
            if initializedTypes.Add t then
                Seq.iter2 (fun (m : Method) (clauses : obj array) ->
                    let decodedClauses = Array.map decode clauses
                    m.SetClauses decodedClauses
                    m.InitializeType t) methods repr.methodImplementations

        // Is used to update already initialized mock type
        // In memory graph, firstly, it is allocated with default values via 'EnsureInitialized'
        // Secondly, it is mutated with deserialized values via 'Update'
        member x.Update (decode : obj -> obj) (t : System.Type) =
            Seq.iter2 (fun (m : Method) (clauses : obj array) ->
                let decodedClauses = Array.map decode clauses
                m.SetClauses decodedClauses
                m.InitializeType t) methods repr.methodImplementations

    [<CLIMutable>]
    [<Serializable>]
    type mockObject = {typeMockIndex : int}

    type Mocker(mockTypeReprs : typeMockRepr array) =
        let mockTypes : (Type * System.Type) option array = Array.zeroCreate mockTypeReprs.Length
        let moduleBuilder = lazy(
            let dynamicAssemblyName = $"VSharpTypeMocks.{Guid.NewGuid()}"
            let assemblyBuilder = AssemblyManager.DefineDynamicAssembly(AssemblyName dynamicAssemblyName, AssemblyBuilderAccess.Run)
            assemblyBuilder.DefineDynamicModule dynamicAssemblyName)

        member x.MakeMockObject (mockTypeIndex : int) =
            {typeMockIndex = mockTypeIndex}

        interface ITypeMockSerializer with
            override x.IsMockObject obj =
                match obj with
                | :? mockObject -> true
                | _ -> false
            override x.IsMockRepresentation obj =
                match obj with
                | :? mockObject -> true
                | _ -> false

            override x.Serialize obj = obj

            // In memory graph, 'Deserialize' used to allocate mock object with default values
            override x.Deserialize (decode : obj -> obj) repr =
                match repr with
                | :? mockObject as mock ->
                    let index = mock.typeMockIndex
                    match mockTypes.[index] with
                    | Some(mockType, t) when TypeUtils.isDelegate mockType.BaseClass ->
                        x.CreateDelegateFromWrapperType(mockType.BaseClass, t)
                    | Some(_, t) -> Reflection.createObject t
                    | None ->
                        let mockTypeRepr = mockTypeReprs.[index]
                        let mockType, typ = x.BuildDynamicType(mockTypeRepr)
                        mockType.EnsureInitialized decode typ
                        let baseClass = mockType.BaseClass
                        let builtObj =
                            if TypeUtils.isDelegate baseClass then x.CreateDelegateFromWrapperType(baseClass, typ)
                            else Reflection.createObject typ
                        mockTypes.[index] <- Some (mockType, typ)
                        builtObj
                | _ -> __unreachable__()

            // In memory graph, 'UpdateMock' used to fill mock object with deserialized values
            override x.UpdateMock (decode : obj -> obj) repr mockInstance =
                match repr with
                | :? mockObject as mock ->
                    let index = mock.typeMockIndex
                    let mockType, t =
                        match mockTypes.[index] with
                        | Some types -> types
                        | None -> __unreachable__()
                    let mockInstanceType =
                        match mockInstance with
                        | :? Delegate as d -> d.Method.DeclaringType
                        | _ -> mockInstance.GetType()
                    assert(t = mockInstanceType)
                    mockType.Update decode mockInstanceType
                | _ -> __unreachable__()

        member x.BuildDynamicType (repr : typeMockRepr) =
            let mockType = Type(repr)
            let moduleBuilder = moduleBuilder.Value
            mockType, mockType.Build(moduleBuilder)

        member x.CreateDelegateFromWrapperType (t : System.Type, builtMock : System.Type) =
            let invokeMethodInfo = builtMock.GetMethod("Invoke")
            Delegate.CreateDelegate(t, null, invokeMethodInfo) :> obj
