namespace CSharp.Factory

open System
open Common.CaseInsensitiveString
open Common.StringUtils
open CSharp.Types
open CSharp.Formatters.Formatters

module internal CSharpFactory =
    let internal UnresolvedBaseType = BaseType.Object |> CSType.BaseType

    let private getFormatter =
        function
        | ArrayType _ -> arrayProperty
        | _ -> property
        
    let validateName (name: String) = if not (Char.IsLetter name.[0]) then raise (System.ArgumentException("Member names can only start with letters."))

    let rec private GeneratedType members key (typeSet: CIString Set) settings propertyFormatter className =
        let className = className |> Option.defaultValue key
        let typeSet = typeSet.Add <| CI className
        
        let classContent =
            members
            |> Array.map (fun property ->
                let className =
                    if property.Name
                       |> CI
                       |> typeSet.Contains
                    then joinStringsWithSpaceSeparation [ className; property.Name ] |> settings.TypeCasing.apply
                    else property.Name
                    |> Option.Some
                match property.Type |> Option.defaultValue UnresolvedBaseType with
                | GeneratedType x ->
                    GeneratedType x property.Name typeSet settings propertyFormatter className
                | ArrayType x ->
                    let formatter = arrayProperty
                    match x with
                    | GeneratedType x -> GeneratedType x property.Name typeSet settings formatter className
                    | x -> CSharpFactoryPrivate x property.Name typeSet settings formatter
                | x -> CSharpFactoryPrivate x property.Name typeSet settings (getFormatter x))
            |> joinStringsWithSpaceSeparation

        let formattedClassName =
            [ settings.Prefix; className; settings.Suffix ]
            |> joinStringsWithSpaceSeparation
            |> settings.TypeCasing.apply
            
        // Ugly side effect, maybe use Result in order in order to be explicit that things could go wrong.
        validateName formattedClassName

        let ``class`` = ``class`` formattedClassName classContent
        if typeSet.Count = 1 then
            ``class``
        else
            let formattedPropertyName = key |> settings.PropertyCasing.apply
            let property = propertyFormatter formattedClassName formattedPropertyName
            let res = [ ``class``; property ] |> joinStringsWithSpaceSeparation
            res

    and private CSharpFactoryPrivate ``type`` key typeSet settings propertyFormatter =
        match ``type`` with
        | GeneratedType members -> GeneratedType members key typeSet settings propertyFormatter Option.None
        | ArrayType ``type`` -> CSharpFactoryPrivate ``type`` key typeSet settings arrayProperty
        | BaseType x ->
            let formattedPropertyName =
                if typeSet.IsEmpty then [ settings.Prefix; key; settings.Suffix ] |> joinStringsWithSpaceSeparation
                else key
                |> settings.PropertyCasing.apply
            validateName formattedPropertyName
            propertyFormatter x.TypeInfo.Stringified formattedPropertyName

    let internal CSharpFactory ``type`` root settings =
        CSharpFactoryPrivate ``type`` root Set.empty settings (getFormatter ``type``)