﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using SmartCodeGenerator.Contracts;

namespace SmartCodeGenerator
{
    internal class GeneratedDocument
    {
        public static readonly SyntaxTrivia GeneratedByAToolPreamble = SyntaxFactory.Comment(@"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------
".Replace("\r\n", "\n").Replace("\n", Environment.NewLine));

        private readonly Document _originDocument;
        private readonly List<ExternAliasDirectiveSyntax> _emittedExterns;
        private readonly List<UsingDirectiveSyntax> _emittedUsings;
        private readonly List<AttributeListSyntax> _emittedAttributeLists = new List<AttributeListSyntax>();
        private readonly List<MemberDeclarationSyntax> _emittedMembers = new List<MemberDeclarationSyntax>();

        public GeneratedDocument(CompilationUnitSyntax inputCompilationUnit, Document originDocument)
        {
            _originDocument = originDocument;
            _emittedExterns = inputCompilationUnit
                .Externs
                .Select(x => x.WithoutTrivia())
                .ToList();

            _emittedUsings = inputCompilationUnit
                .Usings
                .Select(x => x.WithoutTrivia())
                .ToList();
        }

        public void Append(GenerationResult emitted)
        {
            _emittedExterns.AddRange(emitted.Externs);
            _emittedUsings.AddRange(emitted.Usings);
            _emittedAttributeLists.AddRange(emitted.AttributeLists);
            _emittedMembers.AddRange(emitted.Members);
        }

        public async Task<SyntaxTree?> GenerateSyntaxTree()
        {
            if (_emittedMembers.Count == 0 && _emittedAttributeLists.Count == 0)
            {
                return null;
            }

            var compilationUnit =
                SyntaxFactory.CompilationUnit(
                        SyntaxFactory.List(_emittedExterns),
                        SyntaxFactory.List(_emittedUsings),
                        SyntaxFactory.List(_emittedAttributeLists),
                        SyntaxFactory.List(_emittedMembers))
                    .WithLeadingTrivia(GeneratedByAToolPreamble)
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
                    .WithAdditionalAnnotations(Formatter.Annotation)
                    .WithAdditionalAnnotations(Simplifier.Annotation);

            var root = compilationUnit.SyntaxTree.GetRoot();
            var formattedRoot = Formatter.Format(root, Formatter.Annotation, _originDocument.Project.Solution.Workspace, _originDocument.Project.Solution.Workspace.Options);
            var fakeDocument = _originDocument.WithSyntaxRoot(formattedRoot);

            var simplifiedDocument = await Simplifier.ReduceAsync(fakeDocument);
            return compilationUnit.SyntaxTree.WithRootAndOptions(await simplifiedDocument.GetSyntaxRootAsync(), compilationUnit.SyntaxTree.Options);
        }
    }
}