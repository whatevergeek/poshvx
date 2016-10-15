
Describe "Set-Variable DRT Unit Tests" -Tags "CI" {
	It "Set-Variable normal variable Name should works"{
		Set-Variable foo bar
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
	
	It "Set-Variable normal variable Name with position should works"{
		Set-Variable -Name foo bar
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
	
	It "Set-Variable normal variable Name with scope should works"{
		Set-Variable -Name foo -Value bar0
		
		Set-Variable -Name foo -Value bar -Scope "1"
		$var1=Get-Variable -Name foo -scope "1"
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
		
		Set-Variable -Name foo -Value newValue -Scope "local"
		$var1=Get-Variable -Name foo -scope "local"
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "newValue"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
		
		Set-Variable -Name foo -Value newValue2 -Scope "script"
		$var1=Get-Variable -Name foo -scope "script"
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "newValue2"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
	
	It "Set-Variable normal variable Name with position should works"{
		Set-Variable abcaVar bar
		Set-Variable bcdaVar anotherVal
		Set-Variable aVarfoo bogusval
		
		Set-Variable -Name "*aV*" -Value "overwrite" -Include "*Var*" -Exclude "bcd*"
		
		$var1=Get-Variable -Name "*aVar*" -Scope "local"
		$var1[0].Name|Should Be "abcaVar"
		$var1[0].Value|Should Be "overwrite"
		$var1[0].Options|Should Be "None"
		$var1[0].Description|Should Be ""
		
		$var1[1].Name|Should Be "aVarfoo"
		$var1[1].Value|Should Be "overwrite"
		$var1[1].Options|Should Be "None"
		$var1[1].Description|Should Be ""
		
		$var1[2].Name|Should Be "bcdaVar"
		$var1[2].Value|Should Be "anotherVal"
		$var1[2].Options|Should Be "None"
		$var1[2].Description|Should Be ""
	}
	
	It "Set-Variable normal variable Name with Description and Value should works"{
		Set-Variable foo bar
		Set-Variable -Name foo $null -Description "new description" -PassThru:$true -Scope "local"
		$var1=Get-Variable -Name foo -Scope "local"
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be $null
		$var1.Options|Should Be "None"
		$var1.Description|Should Be "new description"
	}
	
	It "Set-Variable normal variable Name with just Description should works"{
		Set-Variable foo bar
		Set-Variable -Name foo -Description "new description" -PassThru:$true -Scope "local"
		$var1=Get-Variable -Name foo -Scope "local"
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be "new description"
	}
	
	It "Set-Variable overwrite Constant Option should throw SessionStateUnauthorizedAccessException"{	
		Set-Variable -Name abcaVar bar -Option Constant -Scope "local"
		try { 
			Set-Variable -Name abcaVar new -Scope "local" -EA Stop
			Throw "Execution OK"
		} 
		catch {
			$_.FullyQualifiedErrorId | Should be "VariableNotWritable,Microsoft.PowerShell.Commands.SetVariableCommand"
		}
	}
	
	It "Set-Variable of existing Private variable without force should throw Exception"{
		Set-Variable abcaVar bar -Description "new description" -Option Private
		$var1=Get-Variable -Name abcaVar
		$var1.Name|Should Be "abcaVar"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "Private"
		$var1.Description|Should Be "new description"
		
		Set-Variable abcaVar other -Description "new description"
		$var1=Get-Variable -Name abcaVar
		$var1.Name|Should Be "abcaVar"
		$var1.Value|Should Be "other"
		$var1.Options|Should Be "Private"
		$var1.Description|Should Be "new description"
	}
	
	It "Set-Variable with Exclude, then Get-Variable it should throw ItemNotFoundException"{
		Set-Variable -Name foo1,foo2 hello -Exclude foo2 -EA Stop
		try { 
			Get-Variable -Name foo2 -EA Stop
			Throw "Execution OK"
		} 
		catch {
			$_.FullyQualifiedErrorId | Should be "VariableNotFound,Microsoft.PowerShell.Commands.GetVariableCommand"
		}
	}
	
	It "Set-Variable of existing ReadOnly variable without force should throw Exception"{
		Set-Variable abcaVar bar -Description "new description" -Option ReadOnly
		$var1=Get-Variable -Name abcaVar
		$var1.Name|Should Be "abcaVar"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "ReadOnly"
		$var1.Description|Should Be "new description"
		try { 
			Set-Variable abcaVar -Option None -EA Stop
			Throw "Execution OK"
		} 
		catch {
			$_.FullyQualifiedErrorId | Should be "VariableNotWritable,Microsoft.PowerShell.Commands.SetVariableCommand"
		}
	}
	
	It "Set-Variable of ReadOnly variable with private scope should work"{
		Set-Variable foo bar -Description "new description" -Option ReadOnly -scope "private"
		$var1=Get-Variable -Name foo
		$var1.Name|Should Be "foo"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "ReadOnly, Private"
		$var1.Description|Should Be "new description"
	}
	
	It "Set-Variable pipeline with Get-Variable should work"{
		$footest1="bar"
		${Get-Variable footest1 -valueonly|Set-Variable bootest1 -passthru}
		$var1=Get-Variable -Name footest1
		$var1.Name|Should Be "footest1"
		$var1.Value|Should Be "bar"
		$var1.Options|Should Be "None"
		$var1.Description|Should Be ""
	}
}

Describe "Set-Variable" -Tags "CI" {
    It "Should create a new variable with no parameters" {
	{ Set-Variable testVar } | Should Not Throw
    }

    It "Should assign a value to a variable it has to create" {
	Set-Variable -Name testVar -Value 4

	Get-Variable testVar -ValueOnly | Should Be 4
    }

    It "Should change the value of an already existing variable" {
	$testVar=1

	$testVar | Should Not Be 2

	Set-Variable testVar -Value 2

	$testVar | Should Be 2
    }

    It "Should be able to be called with the set alias" {
	set testVar -Value 1

	$testVar | Should Be 1
    }

    It "Should be able to be called with the sv alias" {
	sv testVar -Value 2

	$testVar | Should Be 2
    }

    It "Should be able to set variable name using the Name parameter" {
	Set-Variable -Name testVar -Value 1

	$testVar | Should Be 1
    }

    It "Should be able to set the value of a variable by piped input" {
	$testValue = "piped input"

	$testValue | Set-Variable -Name testVar

	$testVar | Should Be $testValue
    }

    It "Should be able to pipe object properties to output using the PassThru switch" {
	$in = Set-Variable -Name testVar -Value "test" -Description "test description" -PassThru

	$output = $in | Format-List -Property Description | Out-String

	# This will cause errors running these tests in Windows
	$output.Trim() | Should Be "Description : test description"
    }

    It "Should be able to set the value using the value switch" {
	Set-Variable -Name testVar -Value 4

	$testVar | Should Be 4

	Set-Variable -Name testVar -Value "test"

	$testVar | Should Be "test"
    }

    Context "Scope Tests" {
	It "Should be able to set a global scope variable using the global switch" {
	    { Set-Variable globalVar -Value 1 -Scope global -Force } | Should Not Throw
	}

	It "Should be able to set a global variable using the script scope switch" {
	    { Set-Variable globalVar -Value 1 -Scope script -Force } | Should Not Throw
	}

	It "Should be able to set an item locally using the local switch" {
	    { Set-Variable globalVar -Value 1 -Scope local -Force } | Should Not Throw
	}
    }
}
