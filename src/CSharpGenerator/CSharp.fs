﻿namespace CSharpGenerator

open JsonParser
open CSharpGenerator.Types
open CSharpGenerator.Arguments
open System

module private stringValidators =
    let valueExists input =
        input
        |> Option.Some
        |> Option.filter (fun x -> not (System.String.IsNullOrWhiteSpace(x)))
        |> Option.map (fun x -> x.Trim())

type CSharp =
    static member CreateFile(input: string) = CSharp.CreateFile(input, Settings())

    static member CreateFile(input: string, settings: Settings) =

        let rootObject =
            settings.RootObjectName
            |> stringValidators.valueExists
            |> Option.defaultValue "Root"

        let classPrefixExists = settings.ClassPrefix |> stringValidators.valueExists
        let classSuffixExists = settings.ClassSuffix |> stringValidators.valueExists

        let (classPrefix, classSuffix) =
            if classSuffixExists.IsNone && classPrefixExists.IsNone then (String.Empty, "Model")
            else
                (classPrefixExists |> Option.defaultValue String.Empty,
                 classSuffixExists |> Option.defaultValue String.Empty)

        let unresolvedBaseType = BaseType.Object |> CSType.BaseType

        let analyzeValues (left: CSType) (right: CSType) =
            match left with
            | GeneratedType x1 ->
                match right with
                | GeneratedType x2 ->
                    List.map2 (fun left right ->
                        if left = right then
                            left
                        else
                            let hasSameName = left.Name = left.Name
                            if left.Type = unresolvedBaseType && hasSameName then
                                right
                            else if right.Type = unresolvedBaseType && hasSameName then
                                left
                            else if hasSameName then
                                { Name = left.Name
                                  Type = unresolvedBaseType |> ArrType }
                            else
                                raise (Exception("Could not generate unresolved type when keys differ."))) x1.Members
                        x2.Members
                    |> (fun x ->
                    { Members = x
                      NamePrefix = classPrefix
                      NameSuffix = classSuffix })
                    |> Option.Some
                | _ -> Option.None
                |> Option.map CSType.GeneratedType
            | BaseType left ->
                match right with
                | BaseType right ->
                    match left with
                    | BaseType.ValueType left ->
                        match right with
                        | BaseType.ValueType right ->
                            if left = right then left |> Option.Some
                            else if left = right.ToNullable() then left |> Option.Some
                            else if left.ToNullable() = right.ToNullable() then right |> Option.Some
                            else Option.None
                        | _ -> Option.None
                    | _ -> Option.None
                | _ -> Option.None
                |> Option.map BaseType.ValueType
                |> Option.map CSType.BaseType
            | _ -> Option.None

        let rec baseType value: CSType Option =
            match value with
            | DateTime _ ->
                BaseType.DateTime
                |> CSType.BaseType
                |> Option.Some
            | Decimal _ ->
                BaseType.Decimal
                |> CSType.BaseType
                |> Option.Some
            | String _ ->
                BaseType.String
                |> CSType.BaseType
                |> Option.Some
            | Boolean _ ->
                BaseType.Boolean
                |> CSType.BaseType
                |> Option.Some
            | Guid _ ->
                BaseType.Guid
                |> CSType.BaseType
                |> Option.Some
            | Double _ ->
                BaseType.Double
                |> CSType.BaseType
                |> Option.Some
            | Object x ->
                x
                |> generatedType
                |> Option.Some
            | Array x ->
                x
                |> stringifyArray
                |> Option.Some
            | Null -> Option.None

        and stringifyArray (value: Value seq): CSType =
            if Seq.isEmpty value then
                unresolvedBaseType
            else
                let result =
                    value
                    |> Seq.map baseType
                    |> Seq.reduce (fun x y ->
                        if x = y then
                            y
                        else if x.IsSome && y.IsSome then
                            analyzeValues x.Value y.Value
                        else if x.IsNone && y.IsNone then
                            option.None
                        else
                            match x |> Option.defaultWith (fun () -> y.Value) with
                            | CSType.BaseType x ->
                                match x with
                                | BaseType.ValueType x ->
                                    x.ToNullable()
                                    |> BaseType.ValueType
                                    |> CSType.BaseType
                                    |> Option.Some
                                | _ -> option.None
                            | _ -> option.None)

                result |> Option.defaultValue unresolvedBaseType
            |> CSType.ArrType

        and generatedType (records: Record seq): CSType =
            records
            |> Seq.map (fun x ->
                { Name = x.Key
                  Type =
                      x.Value
                      |> baseType
                      |> Option.defaultValue unresolvedBaseType })
            |> Seq.toList
            |> (fun x ->
            { Members = x
              NameSuffix = classSuffix
              NamePrefix = classPrefix })
            |> (fun x ->
            if x.Members.IsEmpty then unresolvedBaseType
            else CSType.GeneratedType x)

        let namespaceFormatter =
            settings.NameSpace
            |> stringValidators.valueExists
            |> Option.map (fun x -> sprintf "namespace %s { %s }" x)
            |> Option.defaultValue (sprintf "%s")

        let data =
            (input, settings.Casing
             |> CasingRule.fromString
             |> Option.defaultValue CasingRule.Pascal)
            ||> Json.parse
            
        data |>baseType 
        |> Option.map (fun x ->
            match x with
            | GeneratedType x -> x.ClassDeclaration
            | ArrType x -> x.FormatArray
            | BaseType x -> match x with
                            | ReferenceType x -> x.FormatProperty
                            | ValueType x -> x.FormatProperty
            <| rootObject
            |> namespaceFormatter
        ) |> Option.defaultValue String.Empty
