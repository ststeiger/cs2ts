﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace cs2ts{
    public class Transpiler : CSharpSyntaxWalker{
        private readonly IList<string> _output;

        private int _indent;

        public Transpiler(string code) : base(SyntaxWalkerDepth.StructuredTrivia){
            _output = new List<string>();
            _indent = 0;

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            Visit(root);
        }

        private string GetIndentation(){
            return new string(' ', _indent * 4);
        }

        private void Emit(string text, params object[] args){
            var indentation = GetIndentation();

            string output = null;
            if (!args.Any()){
                output = string.Concat(indentation, text);
            }
            else{
                output = string.Format(string.Concat(indentation, text), args);
            }

            if (text.IndexOf(":int ") != -1)
                nop();

            _output.Add(output);
        }

        private string GetMappedType(TypeSyntax type){
            if (type.ToString() == "void")
                return "void";

            if (type.ToString().EndsWith("Exception"))
                return type.ToString();

            System.String toString = type.ToString();
            String name = null;

            if (toString.StartsWith("bool"))
                name = "Boolean";
            else if (toString.StartsWith("long"))
                name = "number";
            else if (toString.StartsWith("int"))
                name = "number";
            else if (toString.StartsWith("float"))
                name = "number";
            else if (toString.StartsWith("string"))
                name = "string";
            else if (Regex.IsMatch(toString, "List<.*>")){
                System.String replace = Regex.Replace(toString, "List<(.*)>", "Array<$1>");
                name = replace;
            }
            else
                name = toString;

            return name;
        }

        private static string GetVisibilityModifier(SyntaxTokenList tokens){
            return tokens.Any(m => m.Kind() == SyntaxKind.PublicKeyword) ? "public" : "private";
        }

        private Transpiler.BlockScope IndentedBracketScope(){
            return new BlockScope(this);
        }

        private Transpiler.BlockScope IndentedBracketScope(SyntaxNode node){
            return new BlockScope(this, node.Kind() == SyntaxKind.Block);
        }

        public void AddIndent(){
            _indent += 1;
        }

        public void RemoveIndent(){
            _indent -= 1;
        }

        public string ToTypeScript(){
            return string.Join(Environment.NewLine, _output);
        }

        public override void VisitBlock(BlockSyntax node){
            foreach (var statement in node.Statements){
                base.Visit(statement);
            }
        }

        private string str_lastEqualVal = "=0";
        private int counter = 0;

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node){
            string modifier = GetVisibilityModifier(node.Modifiers);

            Emit(string.Join(" ", modifier, "class", node.Identifier.Text));

            str_lastEqualVal = "=0";
            counter = 0;
            using (IndentedBracketScope()){
                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node){
            if (node.EqualsValue != null){
                System.String toString = node.EqualsValue.ToString();
                str_lastEqualVal = toString;
                this.counter = 0;

                var match = System.Text.RegularExpressions.Regex.IsMatch(str_lastEqualVal, @"\s*=\s*0");

                string str = string.Format(" public static {0}:number {1};", node.Identifier.Text, str_lastEqualVal);
                Emit(str);
            }
            else{
                var match = System.Text.RegularExpressions.Regex.IsMatch(str_lastEqualVal, @"\s*=\s*0");
                string str = null;
                if (match)
                    str = string.Format(" public static {0}:number = {1};", node.Identifier.Text, this.counter);
                else
                    str = string.Format(" public static {0}:number {1}+{2};", node.Identifier.Text, str_lastEqualVal,
                        this.counter);

                Emit(str);
            }

            this.counter++;

            this.DefaultVisit(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node){
            string modifier = GetVisibilityModifier(node.Modifiers);

            //string mod = modifier;
            string mod = "";
            string dec = null;
            if (modifier == "public")
                dec = string.Join(" ", "export " + mod, "class", node.Identifier.Text);
            else
                dec = string.Join(" ", mod, "class", node.Identifier.Text);

            string parent = null;
            if (node.BaseList != null){
                parent = node.BaseList.Types.First().ToString();
                dec = dec + " extends " + parent;
            }

            Emit(dec);


            using (IndentedBracketScope()){
                base.VisitClassDeclaration(node);
            }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node){
            string visibility = GetVisibilityModifier(node.Modifiers);

            foreach (var identifier in node.Declaration.Variables){
                var declarationType = node.Declaration.Type;
                string mappedType = GetMappedType(declarationType);
                var text = identifier.GetText().ToString();
                string format = null;
                var indexOf = text.IndexOf("=");
                if (indexOf != -1){
                    var prop = text.Substring(0, indexOf);
                    var val = text.Substring(indexOf + 1);
                    format = string.Format("{0} {1}: {2} = {3};", visibility, prop, mappedType, val);
                }
                else
                    format = string.Format("{0} {1}: {2};", visibility, text, mappedType);

                Emit(format);
            }
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node){
            string visibility = GetVisibilityModifier(node.Modifiers);

            var parameters = string.Format(
                "({0})",
                node.ParameterList
                    .Parameters
                    .Select(p => string.Format("{0}: {1}", p.Identifier.Text, GetMappedType(p.Type)))
                    .ToCsv()
            );

            var methodSignature = string.Format("{0}{1}:", node.Identifier.Text, parameters);
            Emit(String.Join(" ", visibility, methodSignature, this.GetMappedType(node.ReturnType)));

            if (node.Body != null){
                using (IndentedBracketScope()){
                    VisitBlock(node.Body);
                }
            }
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node){
            Emit("module {0}", node.Name.ToString());
            using (IndentedBracketScope()){
                base.VisitNamespaceDeclaration(node);
            }
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node){
            string mappedType = GetMappedType(node.Type);
            string visibility = GetVisibilityModifier(node.Modifiers);

            if (!(node.AccessorList.Accessors.All(ad => ad.Body == null))){
                foreach (var accessor in node.AccessorList.Accessors){
                    var signature =
                        (accessor.Keyword.Kind() != SyntaxKind.GetKeyword
                            ? String.Format("(value: {0})", mappedType)
                            : string.Concat("(): ", mappedType));

                    var format = string.Format("{0} {1} {2}{3}", visibility, accessor.Keyword, node.Identifier.Text,
                        signature);
                    Emit(format);

                    using (IndentedBracketScope()){
                        VisitBlock(accessor.Body);
                    }
                }
            }
            else{
                Emit(string.Join(" ", visibility, string.Concat(node.Identifier.Text, ":"), mappedType + ";"));
            }
        }

        public override void VisitTryStatement(TryStatementSyntax node){
            Emit("try");
            using (IndentedBracketScope()){
                VisitBlock(node.Block);
            }

            foreach (var @catch in node.Catches){
                string arguments = String.Empty;
                if (!(@catch.Declaration == null)){
                    if (@catch.Declaration.Identifier != null)
                        arguments = string.Format(" ({0})", @catch.Declaration.Identifier.Text);
                }

                Emit("catch" + arguments);
                using (IndentedBracketScope()){
                    VisitBlock(@catch.Block);
                }
            }
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node){
            // TODO: Don't just emit the expression as-is. Need to process the nodes of the expression

            var text = node.ToString();
            //var regex = new Regex("\\((bool|Boolean|byte|double|float|int|long|sbyte|short|string|String|uint|ulong|ushort)\\)");
            //regex .Replace(text, )

            Emit(text);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node){
            Emit(node.ToString());
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node){
            var type = node.Type.ToString() != "var" ? GetMappedType(node.Type) : String.Empty;

            if (node.Variables.SeparatorCount == 0){
                foreach (var identifier in node.Variables){
                    var initializer = identifier.Initializer != null ? (" " + identifier.Initializer) : String.Empty;
                    var typeDeclaration = !string.IsNullOrEmpty(type) ? ": " + type : String.Empty;
                    Emit(string.Format("var {0}{1}{2};", identifier.Identifier.Value, typeDeclaration, initializer));
                }
            }
            else{
                var prefix = "var ";
                var identifier = node.Variables.Last();
                var initializer = identifier.Initializer != null ? (" " + identifier.Initializer) : String.Empty;
                var typeDeclaration = !string.IsNullOrEmpty(type) ? ": " + type : String.Empty;

                string padding = new string(' ', prefix.Length);
                var separator = String.Concat(",", Environment.NewLine, GetIndentation(), padding);
                var lines = prefix + String.Join(separator, node.Variables.Select(v => v.Identifier.Value).ToList());
                Emit(string.Format("{0}{1}{2};", lines, typeDeclaration, initializer));
            }
        }

        private string[] ignoreNameSpace = {
            "AnimationOrTween",
            "Coda",
            "Coda.LockStep",
            "Coda.Tools",
            "DG",
            "DG.Tweening",
            "FL",
            "FL.v1",
            "FL.v1.Crc32",
            "FL.v1.File",
            "FL.v1.Security",
            "FlyingWormConsole3",
            "Ref",
            "Spine",
            "ui",
        };

        public override void VisitUsingDirective(UsingDirectiveSyntax node){
            //import {BaseBattleThing} from "./BaseBattleThing";
            System.String name_sp = node.Name.ToString();

            var bbreak = false;
            if (name_sp.IndexOf("System") >= 0)
                bbreak = true;
            if (name_sp.IndexOf("UnityEngine") >= 0)
                bbreak = true;

            if (ignoreNameSpace.ToList().IndexOf(name_sp) >= 0)
                bbreak = true;

            if (!bbreak)
                Emit("import " + name_sp + ";");

            this.DefaultVisit(node);
        }

        public override void DefaultVisit(SyntaxNode node){
            /*
                        System.Type type = node.GetType();
                        System.String substring = type.ToString().Substring(LLLL);
                        System.String toString = node.ToString();
                        System.String split = toString.Split('\n')[0];
                        Console.WriteLine(substring +"   "+ split);
            */

            base.DefaultVisit(node);
        }

        private int LLLL = "Microsoft.CodeAnalysis.CSharp.Syntax.".Length;

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node){
            string visibility = GetVisibilityModifier(node.Modifiers);

            var parameters = string.Format(
                "({0})",
                node.ParameterList
                    .Parameters
                    .Select(p => string.Format("{0}: {1}", p.Identifier.Text, GetMappedType(p.Type)))
                    .ToCsv()
            );

            var methodSignature = string.Format("constructor{0}", parameters);
            Emit(String.Join(" ", methodSignature));

            if (node.Body != null){
                using (IndentedBracketScope()){
                    VisitBlock(node.Body);
                }
            }
        }

        public override void VisitIfStatement(IfStatementSyntax node){
            Emit("if ({0})", node.Condition.ToString());
            using (IndentedBracketScope(node.Statement))
                Visit(node.Statement);

            if (node.Else != null){
                Emit("else");
                using (IndentedBracketScope(node.Else.Statement))
                    Visit(node.Else.Statement);
            }
        }

        public override void VisitWhileStatement(WhileStatementSyntax node){
            Emit("while ({0})", node.Condition.ToString());
            using (IndentedBracketScope(node.Statement))
                Visit(node.Statement);
        }

        public void nop(){
        }

        public override void VisitTrivia(SyntaxTrivia trivia){
            var kind = trivia.Kind();
            if (kind == SyntaxKind.DocumentationCommentExteriorTrivia){
                Emit(trivia.ToString() + trivia.Token.ToString());
            }

            base.VisitTrivia(trivia);

            if (kind == SyntaxKind.SingleLineCommentTrivia)
                Emit(trivia.ToString());

            if (kind == SyntaxKind.MultiLineCommentTrivia
                || kind == SyntaxKind.MultiLineDocumentationCommentTrivia
            ){
                Emit(trivia.ToString());
            }
        }

        public override void VisitDocumentationCommentTrivia(DocumentationCommentTriviaSyntax trivia){
            base.VisitDocumentationCommentTrivia(trivia);

//            if (trivia.Kind() == SyntaxKind.SingleLineDocumentationCommentTrivia)
//                Emit(trivia.ToString());
        }

        internal class BlockScope : IDisposable{
            private readonly Transpiler _visitor;
            private readonly bool _requiresBraces;

            internal BlockScope(Transpiler visitor) : this(visitor, true){
            }

            internal BlockScope(Transpiler visitor, bool requiresBraces){
                _requiresBraces = requiresBraces;
                _visitor = visitor;
                if (_requiresBraces)
                    _visitor.Emit("{");
                _visitor.AddIndent();
            }

            public void Dispose(){
                _visitor.RemoveIndent();
                if (_requiresBraces)
                    _visitor.Emit("}");
            }
        }
    }
}