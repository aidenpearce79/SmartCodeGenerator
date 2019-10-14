﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MS-PL license. See LICENSE.txt file in the project root for full license information.


using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace CodeGeneration.Roslyn.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// The class responsible for generating compilation units to add to the project being built.
    /// </summary>
    public static class DocumentTransform
    {
        /// <summary>
        /// A "generated by tool" comment string with environment/os-normalized newlines.
        /// </summary>
        public static readonly string GeneratedByAToolPreamble = @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------
".Replace("\r\n", "\n").Replace("\n", Environment.NewLine); // normalize regardless of git checkout policy

        /// <summary>
        /// Produces a new document in response to any code generation attributes found in the specified document.
        /// </summary>
        /// <param name="compilation">The compilation to which the document belongs.</param>
        /// <param name="inputDocument">The document to scan for generator attributes.</param>
        /// <param name="projectDirectory">The path of the <c>.csproj</c> project file.</param>
        /// <param name="assemblyLoader">A function that can load an assembly with the given name.</param>
        /// <param name="progress">Reports warnings and errors in code generation.</param>
        /// <returns>A task whose result is the generated document.</returns>
        public static async Task<SyntaxTree> TransformAsync(
            CSharpCompilation compilation,
            Document document,
            string projectDirectory,
            Func<AssemblyName, Assembly> assemblyLoader,
            IProgress<Diagnostic> progress, CancellationToken cancellationToken)
        {
            
            var inputSyntaxTree = await document.GetSyntaxTreeAsync();
            var inputSemanticModel = await document.GetSemanticModelAsync();
            var inputCompilationUnit = inputSyntaxTree.GetCompilationUnitRoot();
            var emittedExterns = inputCompilationUnit
                .Externs
                .Select(x => x.WithoutTrivia())
                .ToList();

            var emittedUsings = inputCompilationUnit
                .Usings
                .Select(x => x.WithoutTrivia())
                .ToList();

            var emittedAttributeLists = new List<AttributeListSyntax>();
            var emittedMembers = new List<MemberDeclarationSyntax>();
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);
            foreach (var memberNode in GetMemberDeclarations(inputSyntaxTree))
            {
                var attributeData = GetAttributeData(compilation, inputSemanticModel, memberNode);
                if (attributeData.Length == 0)
                {
                    continue;
                }
                
                //TODO: Add caching for found generators
                var generators = GeneratorFinder.FindCodeGenerators(attributeData, assemblyLoader).ToList();
                var context = new TransformationContext(memberNode, inputSemanticModel, compilation, projectDirectory, emittedUsings, emittedExterns, syntaxGenerator);

                foreach (var generator in generators)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var emitted = await generator.GenerateRichAsync(context, progress, cancellationToken);
                    emittedExterns.AddRange(emitted.Externs);
                    emittedUsings.AddRange(emitted.Usings);
                    emittedAttributeLists.AddRange(emitted.AttributeLists);
                    emittedMembers.AddRange(emitted.Members);
                }
            }

            return GenerateSyntaxTree(emittedExterns, emittedUsings, emittedAttributeLists, emittedMembers);
        }

        private static SyntaxTree GenerateSyntaxTree(List<ExternAliasDirectiveSyntax> emittedExterns, List<UsingDirectiveSyntax> emittedUsings, List<AttributeListSyntax> emittedAttributeLists,
            List<MemberDeclarationSyntax> emittedMembers)
        {
            var compilationUnit =
                SyntaxFactory.CompilationUnit(
                        SyntaxFactory.List(emittedExterns),
                        SyntaxFactory.List(emittedUsings),
                        SyntaxFactory.List(emittedAttributeLists),
                        SyntaxFactory.List(emittedMembers))
                    .WithLeadingTrivia(SyntaxFactory.Comment(GeneratedByAToolPreamble))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
                    .NormalizeWhitespace()
                    .WithAdditionalAnnotations(Formatter.Annotation)
                    .WithAdditionalAnnotations(Simplifier.Annotation)
                ;

            return compilationUnit.SyntaxTree;
        }

        private static IEnumerable<CSharpSyntaxNode> GetMemberDeclarations(SyntaxTree inputDocument)
        {
            return inputDocument
                .GetRoot()
                .DescendantNodesAndSelf(n => n is CompilationUnitSyntax || n is NamespaceDeclarationSyntax || n is TypeDeclarationSyntax)
                .OfType<CSharpSyntaxNode>();
        }

        private static ImmutableArray<AttributeData> GetAttributeData(Compilation compilation, SemanticModel semanticModel, SyntaxNode syntaxNode)
        {
            switch (syntaxNode)
            {
                case CompilationUnitSyntax syntax:
                    return compilation.Assembly.GetAttributes().Where(x => x.ApplicationSyntaxReference.SyntaxTree == syntax.SyntaxTree).ToImmutableArray();
                default:
                    return semanticModel.GetDeclaredSymbol(syntaxNode)?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty;
            }
        }
    }
}
