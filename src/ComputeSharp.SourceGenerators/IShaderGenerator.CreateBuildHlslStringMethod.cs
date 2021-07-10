﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using ComputeSharp.SourceGenerators.Diagnostics;
using ComputeSharp.SourceGenerators.Extensions;
using ComputeSharp.SourceGenerators.Helpers;
using ComputeSharp.SourceGenerators.Mappings;
using ComputeSharp.SourceGenerators.SyntaxRewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ComputeSharp.SourceGenerators.Diagnostics.DiagnosticDescriptors;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle;

namespace ComputeSharp.SourceGenerators
{
    /// <inheritdoc/>
    public sealed partial class IShaderGenerator
    {
        /// <inheritdoc/>
        private static partial MethodDeclarationSyntax CreateBuildHlslStringMethod(
            GeneratorExecutionContext context,
            StructDeclarationSyntax structDeclaration,
            INamedTypeSymbol structDeclarationSymbol,
            out string? implicitTextureType,
            out bool isSamplerUsed)
        {
            // Properties are not supported
            DetectAndReportPropertyDeclarations(context, structDeclarationSymbol);

            // We need to sets to track all discovered custom types and static methods
            HashSet<INamedTypeSymbol> discoveredTypes = new(SymbolEqualityComparer.Default);
            Dictionary<IMethodSymbol, MethodDeclarationSyntax> staticMethods = new(SymbolEqualityComparer.Default);
            Dictionary<IFieldSymbol, string> constantDefinitions = new(SymbolEqualityComparer.Default);

            // A given type can only represent a single shader type
            if (structDeclarationSymbol.AllInterfaces.Count(static interfaceSymbol => interfaceSymbol is { Name: nameof(IComputeShader) } or { IsGenericType: true, Name: nameof(IPixelShader<byte>) }) > 1)
            {
                context.ReportDiagnostic(MultipleShaderTypesImplemented, structDeclarationSymbol, structDeclarationSymbol);
            }

            // Explore the syntax tree and extract the processed info
            var semanticModel = new SemanticModelProvider(context.Compilation);
            var pixelShaderSymbol = structDeclarationSymbol.AllInterfaces.FirstOrDefault(static interfaceSymbol => interfaceSymbol is { IsGenericType: true, Name: nameof(IPixelShader<byte>) });
            var isComputeShader = pixelShaderSymbol is null;
            var pixelShaderTextureType = isComputeShader ? null : HlslKnownTypes.GetMappedNameForPixelShaderType(pixelShaderSymbol!);
            var processedMembers = GetProcessedFields(context, structDeclarationSymbol, discoveredTypes, isComputeShader).ToArray();
            var sharedBuffers = GetGroupSharedMembers(context, structDeclarationSymbol, discoveredTypes).ToArray();
            var (entryPoint, processedMethods, forwardDeclarations, accessesStaticSampler) = GetProcessedMethods(context, structDeclaration, structDeclarationSymbol, semanticModel, discoveredTypes, staticMethods, constantDefinitions, isComputeShader);
            var implicitSamplerField = accessesStaticSampler ? ("SamplerState", "__sampler") : default((string, string)?);
            var processedTypes = GetProcessedTypes(discoveredTypes).ToArray();
            var processedConstants = GetProcessedConstants(constantDefinitions);
            var staticFields = GetStaticFields(context, semanticModel, structDeclaration, structDeclarationSymbol, discoveredTypes, constantDefinitions);
            string namespaceName = structDeclarationSymbol.ContainingNamespace.ToDisplayString(new(typeQualificationStyle: NameAndContainingTypesAndNamespaces));
            string structName = structDeclaration.Identifier.Text;
            SyntaxTokenList structModifiers = structDeclaration.Modifiers;
            IEnumerable<StatementSyntax> bodyStatements = GenerateRenderMethodBody(
                processedConstants,
                staticFields,
                processedTypes,
                isComputeShader,
                processedMembers,
                pixelShaderTextureType,
                accessesStaticSampler,
                sharedBuffers,
                forwardDeclarations,
                processedMethods,
                entryPoint);

            implicitTextureType = pixelShaderTextureType;
            isSamplerUsed = accessesStaticSampler;

            // This code produces a method declaration as follows:
            //
            // public readonly void BuildHlslString(ref ArrayPoolStringBuilder builder, int threadsX, int threadsY, int threadsZ)
            // {
            //     <BODY>
            // }
            return
                MethodDeclaration(
                    PredefinedType(Token(SyntaxKind.VoidKeyword)),
                    Identifier("BuildHlslString"))
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.ReadOnlyKeyword))
                .AddParameterListParameters(
                    Parameter(Identifier("builder")).AddModifiers(Token(SyntaxKind.OutKeyword)).WithType(IdentifierName("ArrayPoolStringBuilder")),
                    Parameter(Identifier("threadsX")).WithType(PredefinedType(Token(SyntaxKind.IntKeyword))),
                    Parameter(Identifier("threadsY")).WithType(PredefinedType(Token(SyntaxKind.IntKeyword))),
                    Parameter(Identifier("threadsZ")).WithType(PredefinedType(Token(SyntaxKind.IntKeyword))))
                .WithBody(Block(bodyStatements));
        }

        /// <summary>
        /// Gets a sequence of captured fields and their mapped names.
        /// </summary>
        /// <param name="context">The current generator context in use.</param>
        /// <param name="structDeclarationSymbol">The input <see cref="INamedTypeSymbol"/> instance to process.</param>
        /// <param name="types">The collection of currently discovered types.</param>
        /// <param name="isComputeShader">Indicates whether or not <paramref name="structDeclarationSymbol"/> represents a compute shader.</param>
        /// <returns>A sequence of captured fields in <paramref name="structDeclarationSymbol"/>.</returns>
        [Pure]
        private static IEnumerable<(IFieldSymbol Symbol, string HlslName, string HlslType)> GetProcessedFields(
            GeneratorExecutionContext context,
            INamedTypeSymbol structDeclarationSymbol,
            ICollection<INamedTypeSymbol> types,
            bool isComputeShader)
        {
            bool hlslResourceFound = false;

            foreach (var fieldSymbol in structDeclarationSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (fieldSymbol.IsStatic) continue;

                AttributeData? attribute = fieldSymbol.GetAttributes().FirstOrDefault(static a => a.AttributeClass is { Name: nameof(GroupSharedAttribute) });

                // Group shared fields must be static
                if (attribute is not null)
                {
                    context.ReportDiagnostic(InvalidGroupSharedFieldDeclaration, fieldSymbol, structDeclarationSymbol, fieldSymbol.Name);
                }

                // Captured fields must be named type symbols
                if (fieldSymbol.Type is not INamedTypeSymbol typeSymbol)
                {
                    context.ReportDiagnostic(InvalidShaderField, fieldSymbol, structDeclarationSymbol, fieldSymbol.Name, fieldSymbol.Type);

                    continue;
                }

                string metadataName = typeSymbol.GetFullMetadataName();

                // Allowed fields must be either resources, unmanaged values or delegates
                if (HlslKnownTypes.IsTypedResourceType(metadataName))
                {
                    hlslResourceFound = true;

                    // Track the type of items in the current buffer
                    if (HlslKnownTypes.IsStructuredBufferType(metadataName))
                    {
                        types.Add((INamedTypeSymbol)typeSymbol.TypeArguments[0]);
                    }
                }
                else if (!typeSymbol.IsUnmanagedType &&
                         typeSymbol.TypeKind != TypeKind.Delegate)
                {
                    // Shaders can only capture valid HLSL resource types or unmanaged types
                    context.ReportDiagnostic(InvalidShaderField, fieldSymbol, structDeclarationSymbol, fieldSymbol.Name, typeSymbol);

                    continue;
                }
                else if (typeSymbol.IsUnmanagedType &&
                         !HlslKnownTypes.IsKnownHlslType(metadataName))
                {
                    // Track the type if it's a custom struct
                    types.Add(typeSymbol);
                }

                string typeName = HlslKnownTypes.GetMappedName(typeSymbol);

                _ = HlslKnownKeywords.TryGetMappedName(fieldSymbol.Name, out string? mapping);

                // Yield back the current mapping for the name (if the name used a reserved keyword)
                yield return (fieldSymbol, mapping ?? fieldSymbol.Name, typeName);
            }

            // If the shader is a compute one (so no implicit output texture), it has to contain at least one resource
            if (!hlslResourceFound && isComputeShader)
            {
                context.ReportDiagnostic(MissingShaderResources, structDeclarationSymbol, structDeclarationSymbol);
            }
        }

        /// <summary>
        /// Gets a sequence of shader static fields and their mapped names.
        /// </summary>
        /// <param name="context">The current generator context in use.</param>
        /// <param name="semanticModel">The <see cref="SemanticModelProvider"/> instance for the type to process.</param>
        /// <param name="structDeclaration">The <see cref="StructDeclarationSyntax"/> instance for the current type.</param>
        /// <param name="structDeclarationSymbol">The type symbol for the shader type.</param>
        /// <param name="discoveredTypes">The collection of currently discovered types.</param>
        /// <param name="constantDefinitions">The collection of discovered constant definitions.</param>
        /// <returns>A sequence of static constant fields in <paramref name="structDeclarationSymbol"/>.</returns>
        [Pure]
        private static IEnumerable<(string Name, string TypeDeclaration, string? Assignment)> GetStaticFields(
            GeneratorExecutionContext context,
            SemanticModelProvider semanticModel,
            StructDeclarationSyntax structDeclaration,
            INamedTypeSymbol structDeclarationSymbol,
            ICollection<INamedTypeSymbol> discoveredTypes,
            IDictionary<IFieldSymbol, string> constantDefinitions)
        {
            foreach (var fieldDeclaration in structDeclaration.Members.OfType<FieldDeclarationSyntax>())
            {
                foreach (var variableDeclarator in fieldDeclaration.Declaration.Variables)
                {
                    IFieldSymbol fieldSymbol = (IFieldSymbol)semanticModel.For(variableDeclarator).GetDeclaredSymbol(variableDeclarator)!;

                    if (!fieldSymbol.IsStatic || fieldSymbol.IsConst)
                    {
                        continue;
                    }

                    AttributeData? attribute = fieldSymbol.GetAttributes().FirstOrDefault(static a => a.AttributeClass is { Name: nameof(GroupSharedAttribute) });

                    if (attribute is not null) continue;

                    // Constant properties must be of a primitive, vector or matrix type
                    if (fieldSymbol.Type is not INamedTypeSymbol typeSymbol ||
                        !HlslKnownTypes.IsKnownHlslType(typeSymbol.GetFullMetadataName()))
                    {
                        context.ReportDiagnostic(InvalidShaderStaticFieldType, variableDeclarator, structDeclarationSymbol, fieldSymbol.Name, fieldSymbol.Type);

                        continue;
                    }

                    _ = HlslKnownKeywords.TryGetMappedName(fieldSymbol.Name, out string? mapping);

                    string typeDeclaration = fieldSymbol.IsReadOnly switch
                    {
                        true => $"static const {HlslKnownTypes.GetMappedName(typeSymbol)}",
                        false => $"static {HlslKnownTypes.GetMappedName(typeSymbol)}"
                    };

                    StaticFieldRewriter staticFieldRewriter = new(
                        semanticModel,
                        discoveredTypes,
                        constantDefinitions,
                        context);

                    string? assignment = staticFieldRewriter.Visit(variableDeclarator)?.NormalizeWhitespace().ToFullString();

                    yield return (mapping ?? fieldSymbol.Name, typeDeclaration, assignment);
                }
            }
        }

        /// <summary>
        /// Gets a sequence of captured members and their mapped names.
        /// </summary>
        /// <param name="context">The current generator context in use.</param>
        /// <param name="structDeclarationSymbol">The input <see cref="INamedTypeSymbol"/> instance to process.</param>
        /// <param name="types">The collection of currently discovered types.</param>
        /// <returns>A sequence of captured members in <paramref name="structDeclarationSymbol"/>.</returns>
        [Pure]
        private static IEnumerable<(string Name, string Type, int? Count)> GetGroupSharedMembers(
            GeneratorExecutionContext context,
            INamedTypeSymbol structDeclarationSymbol,
            ICollection<INamedTypeSymbol> types)
        {
            foreach (var fieldSymbol in structDeclarationSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (!fieldSymbol.IsStatic) continue;

                AttributeData? attribute = fieldSymbol.GetAttributes().FirstOrDefault(static a => a.AttributeClass is { Name: nameof(GroupSharedAttribute) });

                if (attribute is null) continue;

                if (fieldSymbol.Type is not IArrayTypeSymbol typeSymbol)
                {
                    context.ReportDiagnostic(InvalidGroupSharedFieldType, fieldSymbol, structDeclarationSymbol, fieldSymbol.Name, fieldSymbol.Type);

                    continue;
                }

                if (!typeSymbol.ElementType.IsUnmanagedType)
                {
                    context.ReportDiagnostic(InvalidGroupSharedFieldElementType, fieldSymbol, structDeclarationSymbol, fieldSymbol.Name, fieldSymbol.Type);

                    continue;
                }

                int? bufferSize = (int?)attribute.ConstructorArguments.FirstOrDefault().Value;

                string typeName = HlslKnownTypes.GetMappedElementName(typeSymbol);

                _ = HlslKnownKeywords.TryGetMappedName(fieldSymbol.Name, out string? mapping);

                yield return (mapping ?? fieldSymbol.Name, typeName, bufferSize);

                types.Add((INamedTypeSymbol)typeSymbol.ElementType);
            }
        }

        /// <summary>
        /// Gets a sequence of processed methods declared within a given type.
        /// </summary>
        /// <param name="context">The current generator context in use.</param>
        /// <param name="structDeclarationSymbol">The type symbol for the shader type.</param>
        /// <param name="structDeclaration">The <see cref="StructDeclarationSyntax"/> instance for the current type.</param>
        /// <param name="semanticModel">The <see cref="SemanticModelProvider"/> instance for the type to process.</param>
        /// <param name="discoveredTypes">The collection of currently discovered types.</param>
        /// <param name="staticMethods">The set of discovered and processed static methods.</param>
        /// <param name="constantDefinitions">The collection of discovered constant definitions.</param>
        /// <param name="isComputeShader">Indicates whether or not <paramref name="structDeclarationSymbol"/> represents a compute shader.</param>
        /// <returns>A sequence of processed methods in <paramref name="structDeclaration"/>, and the entry point.</returns>
        [Pure]
        private static (string EntryPoint, IEnumerable<string> Methods, IEnumerable<string> Declarations, bool IsSamplerUser) GetProcessedMethods(
            GeneratorExecutionContext context,
            StructDeclarationSyntax structDeclaration,
            INamedTypeSymbol structDeclarationSymbol,
            SemanticModelProvider semanticModel,
            ICollection<INamedTypeSymbol> discoveredTypes,
            IDictionary<IMethodSymbol, MethodDeclarationSyntax> staticMethods,
            IDictionary<IFieldSymbol, string> constantDefinitions,
            bool isComputeShader)
        {
            // Find all declared methods in the type
            ImmutableArray<MethodDeclarationSyntax> methodDeclarations = (
                from syntaxNode in structDeclaration.DescendantNodes()
                where syntaxNode.IsKind(SyntaxKind.MethodDeclaration)
                select (MethodDeclarationSyntax)syntaxNode).ToImmutableArray();

            string? entryPoint = null;
            List<string> methods = new();
            List<string> declarations = new();
            bool isSamplerUsed = false;

            foreach (MethodDeclarationSyntax methodDeclaration in methodDeclarations)
            {
                IMethodSymbol methodDeclarationSymbol = semanticModel.For(methodDeclaration).GetDeclaredSymbol(methodDeclaration)!;
                bool isShaderEntryPoint =
                    (isComputeShader &&
                     methodDeclarationSymbol.Name == nameof(IComputeShader.Execute) &&
                     methodDeclarationSymbol.ReturnsVoid &&
                     methodDeclarationSymbol.TypeParameters.Length == 0 &&
                     methodDeclarationSymbol.Parameters.Length == 0) ||
                    (!isComputeShader &&
                     methodDeclarationSymbol.Name == nameof(IPixelShader<byte>.Execute) &&
                     methodDeclarationSymbol.ReturnType is not null && // TODO: match for pixel type
                     methodDeclarationSymbol.TypeParameters.Length == 0 &&
                     methodDeclarationSymbol.Parameters.Length == 0);

                // Create the source rewriter for the current method
                ShaderSourceRewriter shaderSourceRewriter = new(
                    structDeclarationSymbol,
                    semanticModel,
                    discoveredTypes,
                    staticMethods,
                    constantDefinitions,
                    context,
                    isShaderEntryPoint);

                // Rewrite the method syntax tree
                MethodDeclarationSyntax? processedMethod = shaderSourceRewriter.Visit(methodDeclaration)!.WithoutTrivia();

                // Track the implicit sampler, if used
                isSamplerUsed = isSamplerUsed || shaderSourceRewriter.IsSamplerUsed;

                // Emit the extracted local functions first
                foreach (var localFunction in shaderSourceRewriter.LocalFunctions)
                {
                    methods.Add(localFunction.Value.NormalizeWhitespace().ToFullString());
                    declarations.Add(localFunction.Value.AsDefinition().NormalizeWhitespace().ToFullString());
                }

                // If the method is the shader entry point, do additional processing
                if (isShaderEntryPoint)
                {
                    processedMethod = isComputeShader switch
                    {
                        true => new ExecuteMethodRewriter.Compute(shaderSourceRewriter).Visit(processedMethod)!,
                        false => new ExecuteMethodRewriter.Pixel(shaderSourceRewriter).Visit(processedMethod)!
                    };

                    entryPoint = processedMethod.NormalizeWhitespace().ToFullString();
                }
                else
                {
                    methods.Add(processedMethod.NormalizeWhitespace().ToFullString());
                    declarations.Add(processedMethod.AsDefinition().NormalizeWhitespace().ToFullString());
                }
            }

            // Process static methods as well
            foreach (MethodDeclarationSyntax staticMethod in staticMethods.Values)
            {
                methods.Add(staticMethod.NormalizeWhitespace().ToFullString());
                declarations.Add(staticMethod.AsDefinition().NormalizeWhitespace().ToFullString());
            }

            return (entryPoint!, methods, declarations, isSamplerUsed);
        }

        /// <summary>
        /// Gets a sequence of discovered constants.
        /// </summary>
        /// <param name="constantDefinitions">The collection of discovered constant definitions.</param>
        /// <returns>A sequence of discovered constants to declare in the shader.</returns>
        [Pure]
        internal static IEnumerable<(string Name, string Value)> GetProcessedConstants(IReadOnlyDictionary<IFieldSymbol, string> constantDefinitions)
        {
            foreach (var constant in constantDefinitions)
            {
                var ownerTypeName = ((INamedTypeSymbol)constant.Key.ContainingSymbol).ToDisplayString().ToHlslIdentifierName();
                var constantName = $"__{ownerTypeName}__{constant.Key.Name}";

                yield return (constantName, constant.Value);
            }
        }

        /// <summary>
        /// Gets the sequence of processed discovered custom types.
        /// </summary>
        /// <param name="types">The sequence of discovered custom types.</param>
        /// <returns>A sequence of custom type definitions to add to the shader source.</returns>
        internal static IEnumerable<string> GetProcessedTypes(IEnumerable<INamedTypeSymbol> types)
        {
            foreach (var type in HlslKnownTypes.GetCustomTypes(types))
            {
                var structType = type.GetFullMetadataName().ToHlslIdentifierName();
                var structDeclaration = StructDeclaration(structType);

                // Declare the fields of the current type
                foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
                {
                    INamedTypeSymbol fieldType = (INamedTypeSymbol)field.Type;

                    // Convert the name to the fully qualified HLSL version
                    if (!HlslKnownTypes.TryGetMappedName(fieldType.GetFullMetadataName(), out string? mapped))
                    {
                        mapped = fieldType.GetFullMetadataName().ToHlslIdentifierName();
                    }

                    structDeclaration = structDeclaration.AddMembers(
                        FieldDeclaration(VariableDeclaration(
                            IdentifierName(mapped!)).AddVariables(
                            VariableDeclarator(Identifier(field.Name)))));
                }

                // Insert the trailing ; right after the closing bracket (after normalization)
                yield return
                    structDeclaration
                    .NormalizeWhitespace()
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                    .ToFullString();
            }
        }

        /// <summary>
        /// Finds and reports all declared properties in a shader.
        /// </summary>
        /// <param name="context">The current generator context in use.</param>
        /// <param name="structDeclarationSymbol">The input <see cref="INamedTypeSymbol"/> instance to process.</param>
        private static void DetectAndReportPropertyDeclarations(GeneratorExecutionContext context, INamedTypeSymbol structDeclarationSymbol)
        {
            foreach (var propertySymbol in structDeclarationSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                context.ReportDiagnostic(DiagnosticDescriptors.PropertyDeclaration, propertySymbol);
            }
        }

        /// <summary>
        /// Produces the series of statements to build the current HLSL source.
        /// </summary>
        /// <param name="definedConstants">The sequence of defined constants for the shader.</param>
        /// <param name="staticFields">The sequence of static fields referenced by the shader.</param>
        /// <param name="declaredTypes">The sequence of declared types used by the shader.</param>
        /// <param name="isComputeShader">Whether or not the current shader is a compute shader (or a pixel shader).</param>
        /// <param name="instanceFields">The sequence of instance fields for the current shader.</param>
        /// <param name="implicitTextureType">The type of the implicit target texture, if present.</param>
        /// <param name="isSamplerUsed">Whether the static sampler is used by the shader.</param>
        /// <param name="sharedBuffers">The sequence of shared buffers declared by the shader.</param>
        /// <param name="forwardDeclarations">The sequence of forward method declarations.</param>
        /// <param name="processedMethods">The sequence of processed methods used by the shader.</param>
        /// <param name="executeMethod">The body of the entry point of the shader.</param>
        /// <returns>The series of statements to build the HLSL source to compile to execute the current shader.</returns>
        private static IEnumerable<StatementSyntax> GenerateRenderMethodBody(
            IEnumerable<(string Name, string Value)> definedConstants,
            IEnumerable<(string Name, string TypeDeclaration, string? Assignment)> staticFields,
            IEnumerable<string> declaredTypes,
            bool isComputeShader,
            IEnumerable<(IFieldSymbol Symbol, string HlslName, string HlslType)> instanceFields,
            string? implicitTextureType,
            bool isSamplerUsed,
            IEnumerable<(string Name, string Type, int? Count)> sharedBuffers,
            IEnumerable<string>? forwardDeclarations,
            IEnumerable<string> processedMethods,
            string executeMethod)
        {
            List<StatementSyntax> statements = new();
            int sizeHint = 64;

            void AppendLF()
            {
                statements.Add(ParseStatement("builder.AppendLine();"));
                sizeHint += 1;
            }

            void AppendLine(string text)
            {
                statements.Add(
                    ExpressionStatement(
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("builder"), IdentifierName("Append")))
                            .AddArgumentListArguments(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text))))));
                sizeHint += text.Length;
            }

            void AppendLineAndLF(string text)
            {
                statements.Add(
                    ExpressionStatement(
                        InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("builder"), IdentifierName("AppendLine")))
                            .AddArgumentListArguments(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text))))));
                sizeHint += text.Length + 1;
            }

            void AppendCharacterAndLF(char c)
            {
                statements.Add(ParseStatement($"builder.AppendLine('{c}');"));
                sizeHint += 2;
            }

            // Header
            AppendLineAndLF("// ================================================");
            AppendLineAndLF("//                  AUTO GENERATED");
            AppendLineAndLF("// ================================================");
            AppendLineAndLF("// This shader was created by ComputeSharp.");
            AppendLineAndLF("// See: https://github.com/Sergio0694/ComputeSharp.");

            // Group size constants
            AppendLF();
            AppendLine("#define __GroupSize__get_X ");
            statements.Add(ParseStatement("builder.AppendLine(threadsX.ToString());"));
            AppendLine("#define __GroupSize__get_Y ");
            statements.Add(ParseStatement("builder.AppendLine(threadsY.ToString());"));
            AppendLine("#define __GroupSize__get_Z ");
            statements.Add(ParseStatement("builder.AppendLine(threadsZ.ToString());"));

            // Define declarations
            foreach (var (name, value) in definedConstants)
            {
                AppendLineAndLF($"#define {name} {value}");
            }

            // Static fields
            if (staticFields.Any())
            {
                AppendLF();

                foreach (var field in staticFields)
                {
                    if (field.Assignment is string assignment)
                    {
                        AppendLineAndLF($"{field.TypeDeclaration} {field.Name} = {assignment};");
                    }
                    else
                    {
                        AppendLineAndLF($"{field.TypeDeclaration} {field.Name};");
                    }
                }
            }

            // Declared types
            foreach (var type in declaredTypes)
            {
                AppendLF();
                AppendLineAndLF(type);
            }

            // Captured variables
            AppendLF();
            AppendLineAndLF("cbuffer _ : register(b0)");
            AppendCharacterAndLF('{');
            AppendLineAndLF("    uint __x;");
            AppendLineAndLF("    uint __y;");

            if (isComputeShader)
            {
                AppendLineAndLF("    uint __z;");
            }

            // User-defined values
            foreach (var (fieldSymbol, fieldName, fieldType) in instanceFields)
            {
                if (fieldSymbol.Type.IsUnmanagedType)
                {
                    AppendLineAndLF($"    {fieldType} {fieldName};");
                }
            }

            AppendCharacterAndLF('}');

            int
                constantBuffersCount = 0,
                readOnlyBuffersCount = 0,
                readWriteBuffersCount = 0;

            // Optional implicit texture field
            if (!isComputeShader)
            {
                AppendLF();
                AppendLineAndLF($"{implicitTextureType} __outputTexture : register(u{readWriteBuffersCount++});");
            }

            // Optional sampler field
            if (isSamplerUsed)
            {
                AppendLF();
                AppendLineAndLF("SamplerState __sampler : register(s);");
            }

            // Resources
            foreach (var (fieldSymbol, fieldName, fieldType) in instanceFields)
            {
                string metadataName = fieldSymbol.Type.GetFullMetadataName();

                if (HlslKnownTypes.IsConstantBufferType(metadataName))
                {
                    AppendLF();
                    AppendLineAndLF($"cbuffer _{fieldName} : register(b{constantBuffersCount++})");
                    AppendCharacterAndLF('{');
                    AppendLineAndLF($"    {fieldType} {fieldName}[2];");
                    AppendCharacterAndLF('}');
                }
                else if (HlslKnownTypes.IsReadOnlyTypedResourceType(metadataName))
                {
                    AppendLF();
                    AppendLineAndLF($"{fieldType} {fieldName} : register(t{readOnlyBuffersCount++});");
                }
                else if (HlslKnownTypes.IsTypedResourceType(metadataName))
                {
                    AppendLF();
                    AppendLineAndLF($"{fieldType} {fieldName} : register(u{readWriteBuffersCount++});");
                }
            }

            // Shared buffers
            foreach (var (bufferName, bufferType, bufferCount) in sharedBuffers)
            {
                object count = (object?)bufferCount ?? "threadsX * threadsY * threadsZ";

                AppendLF();
                AppendLineAndLF($"groupshared {bufferType} {bufferName} [{count}];");
            }

            // Forward declarations
            if (forwardDeclarations is not null)
            {
                foreach (var forwardDeclaration in forwardDeclarations)
                {
                    AppendLF();
                    AppendLineAndLF(forwardDeclaration);
                }
            }

            // Captured methods
            if (processedMethods is not null)
            {
                foreach (var method in processedMethods)
                {
                    AppendLF();
                    AppendLineAndLF(method);
                }
            }

            // Entry point
            AppendLF();
            AppendLineAndLF("[NumThreads(__GroupSize__get_X, __GroupSize__get_Y, __GroupSize__get_Z)]");
            AppendLineAndLF(executeMethod);

            // builder = ArrayPoolStringBuilder.Create(<SIZE_HINT>);
            statements.Insert(0,
                ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        IdentifierName("builder"),
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("ArrayPoolStringBuilder"),
                                IdentifierName("Create")))
                        .AddArgumentListArguments(
                            Argument(LiteralExpression(
                                SyntaxKind.NumericLiteralExpression,
                                Literal(sizeHint)))))));

            return statements;
        }
    }
}

