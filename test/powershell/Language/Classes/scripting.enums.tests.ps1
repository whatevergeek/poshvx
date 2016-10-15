#
# Copyright (c) Microsoft Corporation, 2015
#

Describe 'enums' -Tags "CI" {

    Context 'basic enums' {
        enum E1
        {
            e0
            e1
            e2
        }

        It "has correct value 0" { [E1]::e0 | Should Be ([E1]0) }
        It "has correct value 1" { [E1]::e1 | Should Be ([E1]1) }
        It "has correct value 2" { [E1]::e2 | Should Be ([E1]2) }
        It "cast from string"    { [E1]::e1 | Should Be 'e1' }
        It "cast to string"      { 'e2' | Should Be ([E1]::e2) }
    }

    Context 'Basic enum with initial value' {
        enum E2
        {
            e0
            e1 = 5
            e2
        }

        It "has correct value 0" { [E2]::e0 | Should Be ([E2]0) }
        It "has correct value 5" { [E2]::e1 | Should Be ([E2]5) }
        It "has correct value 6" { [E2]::e2 | Should Be ([E2]6) }
        It "cast from string"    { [E2]::e1 | Should Be 'e1' }
        It "cast to string"      { 'e2' | Should Be ([E2]::e2) }
    }

    Context 'Basic enum with initial value expression' {
        enum E3
        {
            e0
            e1 = 5
            e2 = [int]::MaxValue
            e3 = 1    # This shouldn't be an error even though previous member was max int
        }

        It "has correct value 0"               { [E3]::e0 | Should Be ([E3]0) }
        It "has correct value 5"               { [E3]::e1 | Should Be ([E3]5) }
        It "has correct value [int]::MaxValue" { [E3]::e2 | Should Be ([E3]([int]::MaxValue)) }
        It "has correct value 1"               { [E3]::e3 | Should Be ([E3]1) }
        It "cast from string"                  { [E3]::e2 | Should Be 'e2' }
        It "cast to string"                    { 'e3' | Should Be ([E3]::e3) }
    }

    Context 'Enum with complicated initial value' {
        enum E4
        {
            e0 = [E5]::e0 + 2
        }

        enum E5
        {
            e0 = [E6]::e0 + 2
        }

        enum E6
        {
            e0 = 38
        }

        It 'E4 has correct value' { [E4]::e0 | Should Be ([E4]42) }
        It 'E5 has correct value' { [E5]::e0 | Should Be ([E5]40) }
        It 'E6 has correct value' { [E6]::e0 | Should Be ([E6]38) }
    }
}

Describe 'Basic enum errors' -Tags "CI" {

    Import-Module $PSScriptRoot\..\LanguageTestSupport.psm1

    AfterAll {
        Remove-Module LanguageTestSupport
    }

    ShouldBeParseError 'enum' MissingNameAfterKeyword 4
    ShouldBeParseError 'enum foo' MissingTypeBody 8
    ShouldBeParseError 'enum foo {' MissingEndCurlyBrace 10
    ShouldBeParseError 'enum foo { x = }' ExpectedValueExpression 14
    ShouldBeParseError 'enum foo { x =' ExpectedValueExpression,MissingEndCurlyBrace  14,14
    ShouldBeParseError 'enum foo {} enum foo {}' MemberAlreadyDefined 12
    ShouldBeParseError 'enum foo { x; x }' MemberAlreadyDefined 14 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo { X; x }' MemberAlreadyDefined 14 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo1 { x = [foo2]::x } enum foo2 { x = [foo1]::x }' CycleInEnumInitializers,CycleInEnumInitializers 0,28 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo { e = [int]::MaxValue;  e2 }' EnumeratorValueTooLarge 33 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo { e = [int]::MaxValue + 1 }' EnumeratorValueTooLarge 15 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo { e = $foo }' EnumeratorValueMustBeConstant 15 -SkipAndCheckRuntimeError
    ShouldBeParseError 'enum foo { e = "hello" }' CannotConvertValue 15 -SkipAndCheckRuntimeError
}
