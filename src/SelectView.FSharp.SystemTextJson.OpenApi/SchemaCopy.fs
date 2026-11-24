namespace SelectView.FSharp.SystemTextJson.OpenApi

open System.Collections.Generic
open Microsoft.OpenApi

module internal SchemaCopy =

  let copyTo (dst: OpenApiSchema) (src: IOpenApiSchema) =
    dst.Title <- src.Title
    dst.Schema <- src.Schema
    dst.Id <- src.Id
    dst.Comment <- src.Comment

    dst.Vocabulary <-
      match src.Vocabulary with
      | null -> null
      | v -> Dictionary(v)

    dst.DynamicRef <- src.DynamicRef
    dst.DynamicAnchor <- src.DynamicAnchor

    dst.Definitions <-
      match src.Definitions with
      | null -> null
      | d -> Dictionary(d)

    dst.ExclusiveMaximum <- src.ExclusiveMaximum
    dst.ExclusiveMinimum <- src.ExclusiveMinimum
    dst.Maximum <- src.Maximum
    dst.Minimum <- src.Minimum
    dst.MultipleOf <- src.MultipleOf
    dst.MaxLength <- src.MaxLength
    dst.MinLength <- src.MinLength
    dst.Pattern <- src.Pattern

    dst.Type <- src.Type
    dst.Format <- src.Format
    // Microsoft.OpenApi 2.11 treats assigning null as an explicitly configured `const: null`.
    if not (isNull src.Const) then
      dst.Const <- src.Const

    dst.Default <- src.Default

    dst.ReadOnly <- src.ReadOnly
    dst.WriteOnly <- src.WriteOnly
    dst.Deprecated <- src.Deprecated

    dst.AllOf <-
      match src.AllOf with
      | null -> null
      | a -> ResizeArray(a)

    dst.AnyOf <-
      match src.AnyOf with
      | null -> null
      | a -> ResizeArray(a)

    dst.OneOf <-
      match src.OneOf with
      | null -> null
      | a -> ResizeArray(a)

    dst.Not <- src.Not

    dst.Items <- src.Items
    dst.MaxItems <- src.MaxItems
    dst.MinItems <- src.MinItems
    dst.UniqueItems <- src.UniqueItems

    dst.Properties <-
      match src.Properties with
      | null -> null
      | p -> Dictionary(p)

    dst.PatternProperties <-
      match src.PatternProperties with
      | null -> null
      | p -> Dictionary(p)

    dst.MaxProperties <- src.MaxProperties
    dst.MinProperties <- src.MinProperties

    dst.Required <-
      match src.Required with
      | null -> null
      | r -> HashSet(r)

    dst.AdditionalPropertiesAllowed <- src.AdditionalPropertiesAllowed
    dst.AdditionalProperties <- src.AdditionalProperties

    dst.Discriminator <- src.Discriminator
    dst.Description <- src.Description

    dst.Examples <-
      match src.Examples with
      | null -> null
      | e -> ResizeArray(e)

    dst.Enum <-
      match src.Enum with
      | null -> null
      | e -> ResizeArray(e)

    dst.UnevaluatedProperties <- src.UnevaluatedProperties
    dst.ExternalDocs <- src.ExternalDocs
    dst.Xml <- src.Xml

    dst.Extensions <-
      match src.Extensions with
      | null -> null
      | e -> Dictionary(e)

    dst.UnrecognizedKeywords <-
      match src.UnrecognizedKeywords with
      | null -> null
      | e -> Dictionary(e)

    dst.Metadata <-
      if src :? IMetadataContainer then
        (src :?> IMetadataContainer).Metadata
      else
        null

    dst.DependentRequired <-
      match src.DependentRequired with
      | null -> null
      | d -> Dictionary(d)
