using System;
using System.Linq;
using Microsoft.Vbe.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using RubberduckTests.Inspections;
using RubberduckTests.Mocks;

namespace RubberduckTests.Grammar
{
    [TestClass]
    public class ResolverTests
    {
        private RubberduckParserState Resolve(string code, vbext_ComponentType moduleType = vbext_ComponentType.vbext_ct_StdModule)
        {
            var builder = new MockVbeBuilder();
            VBComponent component;
            var vbe = builder.BuildFromSingleModule(code, moduleType, out component);
            var parser = MockParser.Create(vbe.Object, new RubberduckParserState());

            parser.Parse();
            if (parser.State.Status != ParserState.Ready)
            {
                Assert.Inconclusive("Parser state should be 'Ready', but returns '{0}'.", parser.State.Status);
            }

            return parser.State;
        }

        private RubberduckParserState Resolve(params string[] classes)
        {
            var builder = new MockVbeBuilder();
            var projectBuilder = builder.ProjectBuilder("TestProject", vbext_ProjectProtection.vbext_pp_none);
            for (var i = 0; i < classes.Length; i++)
            {
                projectBuilder.AddComponent("Class" + (i + 1), vbext_ComponentType.vbext_ct_ClassModule, classes[i]);
            }

            var project = projectBuilder.Build();
            builder.AddProject(project);
            var vbe = builder.Build();

            var parser = MockParser.Create(vbe.Object, new RubberduckParserState());

            parser.Parse();
            if (parser.State.Status != ParserState.Ready)
            {
                Assert.Inconclusive("Parser state should be 'Ready', but returns '{0}'.", parser.State.Status);
            }

            return parser.State;
        }

        private RubberduckParserState Resolve(params Tuple<string,vbext_ComponentType>[] components)
        {
            var builder = new MockVbeBuilder();
            var projectBuilder = builder.ProjectBuilder("TestProject", vbext_ProjectProtection.vbext_pp_none);
            for (var i = 0; i < components.Length; i++)
            {
                projectBuilder.AddComponent("Component" + (i + 1), components[i].Item2, components[i].Item1);
            }

            var project = projectBuilder.Build();
            builder.AddProject(project);
            var vbe = builder.Build();

            var parser = MockParser.Create(vbe.Object, new RubberduckParserState());

            parser.Parse();
            if (parser.State.Status != ParserState.Ready)
            {
                Assert.Inconclusive("Parser state should be 'Ready', but returns '{0}'.", parser.State.Status);
            }

            return parser.State;
        }

        [TestMethod]
        public void FunctionReturnValueAssignment_IsReferenceToFunctionDeclaration()
        {
            // arrange
            var code = @"
Public Function Foo() As String
    Foo = 42
End Function
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Function && item.IdentifierName == "Foo");

            Assert.AreEqual(1, declaration.References.Count(item => item.IsAssignment));
        }

        [TestMethod]
        public void FunctionCall_IsReferenceToFunctionDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething()
    Debug.Print Foo
End Sub

Private Function Foo() As String
    Foo = 42
End Function
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Function && item.IdentifierName == "Foo");

            var reference = declaration.References.SingleOrDefault(item => !item.IsAssignment);
            Assert.IsNotNull(reference);
            Assert.AreEqual("DoSomething", reference.ParentScoping.IdentifierName);
        }

        [TestMethod]
        public void LocalVariableCall_IsReferenceToVariableDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething()
    Dim foo As Integer
    Debug.Print foo
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "foo");

            var reference = declaration.References.SingleOrDefault(item => !item.IsAssignment);
            Assert.IsNotNull(reference);
            Assert.AreEqual("DoSomething", reference.ParentScoping.IdentifierName);
        }

        [TestMethod]
        public void LocalVariableAssignment_IsReferenceToVariableDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething()
    Dim foo As Integer
    foo = 42
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "foo");

            var reference = declaration.References.SingleOrDefault(item => item.IsAssignment);
            Assert.IsNotNull(reference);
            Assert.AreEqual("DoSomething", reference.ParentScoping.IdentifierName);
        }

        [TestMethod]
        public void PublicVariableCall_IsReferenceToVariableDeclaration()
        {
            // arrange
            var code_class1 = @"
Public Sub DoSomething()
    Debug.Print foo
End Sub
";
            var code_class2 = @"
Option Explicit
Public foo As Integer
";
            // act
            var state = Resolve(
                Tuple.Create(code_class1, vbext_ComponentType.vbext_ct_ClassModule), 
                Tuple.Create(code_class2, vbext_ComponentType.vbext_ct_StdModule));

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "foo");

            var reference = declaration.References.SingleOrDefault(item => !item.IsAssignment);
            Assert.IsNotNull(reference);
            Assert.AreEqual("DoSomething", reference.ParentScoping.IdentifierName);
        }

        [TestMethod]
        public void PublicInstanceVariable_DoesNotRequireInstance()
        {
            // todo: reverse this test condition when we can read module attributes

            // arrange
            var code_class1 = @"
Public Sub DoSomething()
    Debug.Print foo
End Sub
";
            var code_class2 = @"
Option Explicit
Public foo As Integer
";
            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "foo");

            var reference = declaration.References.SingleOrDefault(item => !item.IsAssignment
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure);

            Assert.IsNotNull(reference);
        }

        [TestMethod]
        public void PublicVariableAssignment_IsReferenceToVariableDeclaration()
        {
            // arrange
            var code_class1 = @"
Public Sub DoSomething()
    foo = 42
End Sub
";
            var code_class2 = @"
Option Explicit
Public foo As Integer
";
            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "foo");

            var reference = declaration.References.SingleOrDefault(item => item.IsAssignment);
            Assert.IsNotNull(reference);
            Assert.AreEqual("DoSomething", reference.ParentScoping.IdentifierName);
        }

        [TestMethod]
        public void UserDefinedTypeVariableAsTypeClause_IsReferenceToUserDefinedTypeDeclaration()
        {
            // arrange
            var code = @"
Private Type TFoo
    Bar As Integer
End Type
Private this As TFoo
";
            // act
            var state = Resolve(code, vbext_ComponentType.vbext_ct_ClassModule);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.UserDefinedType && item.IdentifierName == "TFoo");

            Assert.IsNotNull(declaration.References.SingleOrDefault());
        }

        [TestMethod]
        public void ObjectVariableAsTypeClause_IsReferenceToClassModuleDeclaration()
        {
            // arrange
            var code_class1 = @"
Public Sub DoSomething()
    Dim foo As Class2
End Sub
";
            var code_class2 = @"
Option Explicit
";

            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Class && item.IdentifierName == "Class2");

            Assert.IsNotNull(declaration.References.SingleOrDefault());
        }

        [TestMethod]
        public void ParameterCall_IsReferenceToParameterDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething(ByVal foo As Integer)
    Debug.Print foo
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter && item.IdentifierName == "foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault());
        }

        [TestMethod]
        public void ParameterAssignment_IsAssignmentReferenceToParameterDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething(ByRef foo As Integer)
    foo = 42
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter && item.IdentifierName == "foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item => item.IsAssignment));
        }

        [TestMethod]
        public void NamedParameterCall_IsReferenceToParameterDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething()
    DoSomethingElse foo:=42
End Sub

Private Sub DoSomethingElse(ByVal foo As Integer)
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter && item.IdentifierName == "foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item => 
                item.ParentScoping.IdentifierName == "DoSomething"));
        }

        [TestMethod]
        public void UserDefinedTypeMemberCall_IsReferenceToUserDefinedTypeMemberDeclaration()
        {
            // arrange
            var code = @"
Private Type TFoo
    Bar As Integer
End Type
Private this As TFoo

Public Property Get Bar() As Integer
    Bar = this.Bar
End Property
";
            // act
            var state = Resolve(code, vbext_ComponentType.vbext_ct_ClassModule);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.UserDefinedTypeMember && item.IdentifierName == "Bar");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.PropertyGet
                && item.ParentScoping.IdentifierName == "Bar"));
        }

        [TestMethod]
        public void UserDefinedTypeVariableCall_IsReferenceToVariableDeclaration()
        {
            // arrange
            var code = @"
Private Type TFoo
    Bar As Integer
End Type
Private this As TFoo

Public Property Get Bar() As Integer
    Bar = this.Bar
End Property
";
            // act
            var state = Resolve(code, vbext_ComponentType.vbext_ct_ClassModule);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "this");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.PropertyGet
                && item.ParentScoping.IdentifierName == "Bar"));
        }

        [TestMethod]
        public void WithVariableMemberCall_IsReferenceToMemberDeclaration()
        {
            // arrange
            var code_class1 = @"
Public Property Get Foo() As Integer
    Foo = 42
End Property
";
            var code_class2 = @"
Public Sub DoSomething()
    With New Class1
        Debug.Print .Foo
    End With
End Sub
";
            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertyGet && item.IdentifierName == "Foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"));
        }

        [TestMethod]
        public void NestedWithVariableMemberCall_IsReferenceToMemberDeclaration()
        {
            // arrange
            var code_class1 = @"
Public Property Get Foo() As Class2
    Foo = New Class2
End Property
";
            var code_class2 = @"
Public Property Get Bar() As Integer
    Bar = 42
End Property
";
            var code_class3 = @"
Public Sub DoSomething()
    With New Class1
        With .Foo
            Debug.Print .Bar
        End With
    End With
End Sub
";

            // act
            var state = Resolve(code_class1, code_class2, code_class3);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertyGet && item.IdentifierName == "Bar");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"));
        }

        [TestMethod]
        public void ResolvesLocalVariableToSmallestScopeIdentifier()
        {
            var code = @"
Private foo As Integer

Private Sub DoSomething()
    Dim foo As Integer
    foo = 42
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable
                && item.ParentScopeDeclaration.IdentifierName == "DoSomething"
                && item.IdentifierName == "foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault());

            var fieldDeclaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable
                && item.ParentScopeDeclaration.DeclarationType == DeclarationType.Module
                && item.IdentifierName == "foo");

            Assert.IsNull(fieldDeclaration.References.SingleOrDefault());
        }

        [TestMethod]
        public void Implements_IsReferenceToClassDeclaration()
        {
            var code_class1 = @"
Public Sub DoSomething()
End Sub
";
            var code_class2 = @"
Implements Class1

Private Sub Class1_DoSomething()
End Sub
";
            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Class && item.IdentifierName == "Class1");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.IdentifierName == "Class2"));
        }

        [TestMethod]
        public void NestedMemberCall_IsReferenceToMember()
        {
            // arrange
            var code_class1 = @"
Public Property Get Foo() As Class2
    Foo = New Class2
End Property
";
            var code_class2 = @"
Public Property Get Bar() As Integer
    Bar = 42
End Property
";
            var code_class3 = @"
Public Sub DoSomething(ByVal a As Class1)
    Debug.Print a.Foo.Bar
End Sub
";
            // act
            var state = Resolve(code_class1, code_class2, code_class3);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertyGet && item.IdentifierName == "Bar");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"));
        }

        [TestMethod]
        public void MemberCallParent_IsReferenceToParent()
        {
            // arrange
            var code_class1 = @"
Public Property Get Foo() As Integer
    Foo = 42
End Property
";
            var code_class2 = @"
Public Sub DoSomething(ByVal a As Class1)
    Debug.Print a.Foo
End Sub
";
            // act
            var state = Resolve(code_class1, code_class2);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter && item.IdentifierName == "a");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"));
        }

        [TestMethod]
        public void ForLoop_IsAssignmentReferenceToIteratorDeclaration()
        {
            // arrange
            var code = @"
Public Sub DoSomething()
    Dim i As Integer
    For i = 0 To 9
    Next
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "i");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.IsAssignment));
        }

        [TestMethod]
        public void ForEachLoop_IsReferenceToIteratorDeclaration()
        {
            var code = @"
Public Sub DoSomething(ByVal c As Collection)
    Dim i As Variant
    For Each i In c
    Next
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable && item.IdentifierName == "i");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.IsAssignment));
        }

        [TestMethod]
        public void ForEachLoop_InClauseIsReferenceToIteratedDeclaration()
        {
            var code = @"
Public Sub DoSomething(ByVal c As Collection)
    Dim i As Variant
    For Each i In c
    Next
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter && item.IdentifierName == "c");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"
                && !item.IsAssignment));
        }

        [TestMethod]
        public void ArraySubscriptAccess_IsReferenceToArrayDeclaration()
        {
            var code = @"
Public Sub DoSomething(ParamArray values())
    Dim i As Integer
    For i = 0 To 9
        Debug.Print values(i)
    Next
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter 
                && item.IdentifierName == "values"
                && item.IsArray());

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"
                && !item.IsAssignment));
        }

        [TestMethod]
        public void ArraySubscriptWrite_IsAssignmentReferenceToArrayDeclaration()
        {
            var code = @"
Public Sub DoSomething(ParamArray values())
    Dim i As Integer
    For i = LBound(values) To UBound(values)
        values(i) = 42
    Next
End Sub
";
            // act
            var state = Resolve(code);

            // assert
            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Parameter
                && item.IdentifierName == "values"
                && item.IsArray());

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.DeclarationType == DeclarationType.Procedure
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.IsAssignment));
        }

        [TestMethod]
        public void PropertyGetCall_IsReferenceToPropertyGetDeclaration()
        {
            var code_class1 = @"
Private tValue As Integer

Public Property Get Foo() As Integer
    Foo = tValue
End Property

Public Property Let Foo(ByVal value As Integer)
    tValue = value
End Property
";
            var code_class2 = @"
Public Sub DoSomething()
    Dim bar As New Class1
    Debug.Print bar.Foo
End Sub
";

            var state = Resolve(code_class1, code_class2);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertyGet
                && item.IdentifierName == "Foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                !item.IsAssignment
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void PropertySetCall_IsReferenceToPropertySetDeclaration()
        {
            var code_class1 = @"
Private tValue As Object

Public Property Get Foo() As Object
    Set Foo = tValue
End Property

Public Property Set Foo(ByVal value As Object)
    Set tValue = value
End Property
";
            var code_class2 = @"
Public Sub DoSomething()
    Dim bar As New Class1
    Set bar.Foo = Nothing
End Sub
";

            var state = Resolve(code_class1, code_class2);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertySet
                && item.IdentifierName == "Foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.IsAssignment
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void PropertyLetCall_IsReferenceToPropertyLetDeclaration()
        {
            var code_class1 = @"
Private tValue As Integer

Public Property Get Foo() As Integer
    Foo = tValue
End Property

Public Property Let Foo(ByVal value As Integer)
    tValue = value
End Property
";
            var code_class2 = @"
Public Sub DoSomething()
    Dim bar As New Class1
    bar.Foo = 42
End Sub
";

            var state = Resolve(code_class1, code_class2);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.PropertyLet
                && item.IdentifierName == "Foo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.IsAssignment
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void EnumMemberCall_IsReferenceToEnumMemberDeclaration()
        {
            var code = @"
Option Explicit
Public Enum FooBarBaz
    Foo
    Bar
    Baz
End Enum

Public Sub DoSomething()
    Dim a As FooBarBaz
    a = Foo
End Sub
";
            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.EnumerationMember
                && item.IdentifierName == "Foo"
                && item.ParentDeclaration.IdentifierName == "FooBarBaz");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                !item.IsAssignment 
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));

        }

        [TestMethod]
        public void QualifiedEnumMemberCall_IsReferenceToEnumMemberDeclaration()
        {
            var code = @"
Option Explicit
Public Enum FooBarBaz
    Foo
    Bar
    Baz
End Enum

Public Sub DoSomething()
    Dim a As FooBarBaz
    a = FooBarBaz.Foo
End Sub
";
            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.EnumerationMember
                && item.IdentifierName == "Foo"
                && item.ParentDeclaration.IdentifierName == "FooBarBaz");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                !item.IsAssignment
                && item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void EnumParameterAsTypeName_ResolvesToEnumTypeDeclaration()
        {
            var code = @"

Option Explicit
Public Enum FooBarBaz
    Foo
    Bar
    Baz
End Enum

Public Sub DoSomething(ByVal a As FooBarBaz)
End Sub
";

            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Enumeration
                && item.IdentifierName == "FooBarBaz");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item => 
                item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void UserDefinedTypeParameterAsTypeName_ResolvesToUserDefinedTypeDeclaration()
        {
            var code = @"
Option Explicit
Public Type TFoo
    Foo As Integer
End Type

Public Sub DoSomething(ByVal a As TFoo)
End Sub
";

            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.UserDefinedType
                && item.IdentifierName == "TFoo");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item =>
                item.ParentScoping.IdentifierName == "DoSomething"
                && item.ParentScoping.DeclarationType == DeclarationType.Procedure));
        }

        [TestMethod]
        public void LocalArrayOrFunctionCall_ResolvesToSmallestScopedDeclaration()
        {
            var code = @"
'note: Dim Foo() As Integer on this line would not compile in VBA
Public Sub DoSomething()
    Dim Foo() As Integer
    Debug.Print Foo(0) 'VBA raises index out of bounds error, i.e. VBA resolves to local Foo()
End Sub

Private Function Foo(ByVal bar As Integer)
    Foo = bar + 42
End Function";

            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item => 
                item.DeclarationType == DeclarationType.Variable
                && item.IsArray()
                && item.ParentScopeDeclaration.IdentifierName == "DoSomething");

            Assert.IsNotNull(declaration.References.SingleOrDefault(item => !item.IsAssignment));
        }

        [TestMethod]
        public void AnnotatedReference_LineAbove_HasAnnotations()
        {
            var code = @"
Public Sub DoSomething()
    Dim foo As Integer
    '@Ignore UnassignedVariableUsage
    Debug.Print foo    
End Sub
";
            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable);

            var usage = declaration.References.Single();

            Assert.AreEqual("@Ignore UnassignedVariableUsage", usage.Annotations);
        }

        [TestMethod]
        public void AnnotatedReference_SameLine_HasNoAnnotations()
        {
            var code = @"
Public Sub DoSomething()
    Dim foo As Integer
    Debug.Print foo '@Ignore UnassignedVariableUsage 
End Sub
";
            var state = Resolve(code);

            var declaration = state.AllUserDeclarations.Single(item =>
                item.DeclarationType == DeclarationType.Variable);

            var usage = declaration.References.Single();

            Assert.AreEqual(string.Empty, usage.Annotations);
        }


    }
}