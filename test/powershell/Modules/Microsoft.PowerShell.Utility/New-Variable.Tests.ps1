
Describe "New-Variable DRT Unit Tests" -Tags "CI" {
	It "New-Variable variable with description should works"{
		New-Variable foo bar -description "my description"
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be "my description"
	}
	
	It "New-Variable variable with option should works"{
		New-Variable foo bar -option Constant
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "Constant"
		$var1.Description|Should Be ""
	}
	
	It "New-Variable variable twice should throw Exception"{
		New-Variable foo bogus
		
		try {
			New-Variable foo bar -EA Stop
			Throw "Execution OK"
		} 
		catch {
			$_.CategoryInfo| Should Match "SessionStateException"
			$_.FullyQualifiedErrorId | Should Be "VariableAlreadyExists,Microsoft.PowerShell.Commands.NewVariableCommand"
		}
		New-Variable foo bar -Force -PassThru
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
	
	It "New-Variable ReadOnly variable twice should throw Exception"{
		New-Variable foo bogus -option ReadOnly
		
		try {
			New-Variable foo bar -EA Stop
			Throw "Execution OK"
		} 
		catch {
			$_.CategoryInfo| Should Match "SessionStateException"
			$_.FullyQualifiedErrorId | Should Be "VariableAlreadyExists,Microsoft.PowerShell.Commands.NewVariableCommand"
		}
		New-Variable foo bar -Force -PassThru
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
}

Describe "New-Variable" -Tags "CI" {
    It "Should create a new variable with no parameters" {
	{ New-Variable var1 } | Should Not Throw
    }

    It "Should be able to set variable name using the Name parameter" {
	{ New-Variable -Name var1 } | Should Not Throw
    }

    It "Should be able to assign a value to a variable using the value switch" {
	New-Variable var1 -Value 4

	$var1 | Should Be 4
    }

    It "Should be able to assign a value to a new variable without using the value switch" {
	New-Variable var1 "test"

	$var1 | Should Be "test"
    }

    It "Should assign a description to a new variable using the description switch" {
	New-Variable var1 100 -Description "Test Description"

	(Get-Variable var1).Description | Should Be "Test Description"
    }

    It "Should be able to be called with the nv alias" {
	nv var1
	$var1 | Should BeNullOrEmpty
	nv var2 2
	$var2 | Should Be 2
    }

    It "Should not be able to set the name of a new variable to that of an old variable within same scope when the Force switch is missing" {
	New-Variable var1
	(New-Variable var1 -ErrorAction SilentlyContinue) | Should Throw
    }

    It "Should change the value of an already existing variable using the Force switch" {
	New-Variable var1 -Value 1

	$var1 | Should Be 1

	New-Variable var1 -Value 2 -Force

	$var1 | Should Be 2
	$var1 | Should Not Be 1

    }

    It "Should be able to set the value of a variable by piped input" {
	$in = "value"

	$in | New-Variable -Name var1

	$var1 | Should Be $in

    }

    It "Should be able to pipe object properties to output using the PassThru switch" {
	$in = Set-Variable -Name testVar -Value "test" -Description "test description" -PassThru

	$in.Description | Should Be "test description"
    }

    It "Should be able to set the value using the value switch" {
	New-Variable -Name var1 -Value 2

	$var1 | Should Be 2
    }

    Context "Option tests" {
	It "Should be able to use the options switch without error" {
		{ New-Variable -Name var1 -Value 2 -Option Unspecified } | Should Not Throw
	}

	It "Should default to none as the value for options" {
		 (new-variable -name var2 -value 4 -passthru).Options | should be "None" 
	}

	It "Should be able to set ReadOnly option" {
		{ New-Variable -Name var1 -Value 2 -Option ReadOnly } | Should Not Throw
	}

	It "Should not be able to change variable created using the ReadOnly option when the Force switch is not used" {
		New-Variable -Name var1 -Value 1 -Option ReadOnly

		Set-Variable -Name var1 -Value 2 -ErrorAction SilentlyContinue

		$var1 | Should Not Be 2
	}

	It "Should be able to set a new variable to constant" {
		{ New-Variable -Name var1 -Option Constant } | Should Not Throw
	}

	It "Should not be able to change an existing variable to constant" {
		New-Variable -Name var1 -Value 1 -PassThru

		Set-Variable -Name var1 -Option Constant  -ErrorAction SilentlyContinue

		(Get-Variable var1).Options | should be "None" 
	}

	It "Should not be able to delete a constant variable" {
		New-Variable -Name var1 -Value 2 -Option Constant

		Remove-Variable -Name var1 -ErrorAction SilentlyContinue

		$var1 | Should Be 2
	}

	It "Should not be able to change a constant variable" {
		New-Variable -Name var1 -Value 1 -Option Constant

		Set-Variable -Name var1 -Value 2  -ErrorAction SilentlyContinue

		$var1 | Should Not Be 2
	}

	It "Should be able to create a variable as private without error" {
		{ New-Variable -Name var1 -Option Private } | Should Not Throw
	}

	It "Should be able to see the value of a private variable when within scope" {

		New-Variable -Name var1 -Value 100 -Option Private

		$var1 | Should Be 100

	}

	It "Should not be able to see the value of a private variable when out of scope" {
		{New-Variable -Name var1 -Value 1 -Option Private}| Should Not Throw

		$var1 | Should BeNullOrEmpty
	}

	It "Should be able to use the AllScope switch without error" {
	    { New-Variable -Name var1 -Option AllScope } | Should Not Throw
	}

	It "Should be able to see variable created using the AllScope switch in a child scope" {
	    New-Variable -Name var1 -Value 1 -Option AllScope
	    &{ $var1 = 2 }
		$var1 | Should Be 2
	}

    }

    Context "Scope Tests" {
    BeforeAll {
        if ( get-variable -scope global -name globalVar1 -ea SilentlyContinue )
        {
            Remove-Variable -scope global -name globalVar1
        }
        if ( get-variable -scope script -name scriptvar -ea SilentlyContinue )
        {
            remove-variable -scope script -name scriptvar
        }
        # no check for local scope variable as that scope is created with test invocation
    }
    AfterAll {
        if ( get-variable -scope global -name globalVar1 )
        {
            Remove-Variable -scope global -name globalVar1
        }
        if ( get-variable -scope script -name scriptvar )
        {
            remove-variable -scope script -name scriptvar
        }
    }
    It "Should be able to create a global scope variable using the global switch" {
        new-variable -Scope global -name globalvar1 -value 1
        get-variable -Scope global -name globalVar1 -ValueOnly | Should be 1
    }
    It "Should be able to create a local scope variable using the local switch" {
        Get-Variable -scope local -name localvar -ValueOnly -ea silentlycontinue | should BeNullOrEmpty
        New-Variable -Scope local -Name localVar -value 10 
        get-variable -scope local -name localvar -ValueOnly | Should be 10
    }
    It "Should be able to create a script scope variable using the script switch" {
        new-variable -scope script -name scriptvar -value 100
        get-variable -scope script -name scriptvar -ValueOnly | should be 100
    }
	}
}
