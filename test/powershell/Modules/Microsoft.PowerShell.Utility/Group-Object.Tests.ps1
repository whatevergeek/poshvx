Describe "Group-Object DRT Unit Tests" -Tags "CI" {
    It "Test for CaseSensitive switch" {
        $testObject = 'aA', 'aA', 'AA', 'AA'
        $results = $testObject | Group-Object -CaseSensitive
        $results.Count | Should Be 2
        $results.Name.Count | Should Be 2
        $results.Group.Count | Should Be 4
        $results.Name | Should Be aA,AA
        $results.Group | Should Be aA,aA,AA,AA
        $results.Group.GetType().BaseType | Should Be Array
    }
}

Describe "Group-Object" -Tags "CI" {
    $testObject = Get-ChildItem

    It "Should be called using an object as piped without error with no switches" {
	{$testObject | Group-Object } | Should Not Throw
    }

    It "Should be called using the InputObject without error with no other switches" {
	{ Group-Object -InputObject $testObject } | Should Not Throw
    }

    It "Should return three columns- count, name, and group" {
	$actual = Group-Object -InputObject $testObject

	$actual.Count       | Should BeGreaterThan 0
	$actual.Name.Count  | Should BeGreaterThan 0
	$actual.Group.Count | Should BeGreaterThan 0

    }

    It "Should use the group alias" {
	{ group -InputObject $testObject } | Should Not Throw
    }

    It "Should create a collection when the inputObject parameter is used" {
	$actualParam = Group-Object -InputObject $testObject

	$actualParam.Group.GetType().Name | Should Be "Collection``1"
    }

    It "Should output an array when piped input is used" {
	$actual = $testObject | Group-Object

	$actual.Group.GetType().BaseType | Should Be Array
    }

    It "Should have the same output between the group alias and the group-object cmdlet" {
	$actualAlias = group -InputObject $testObject
	$actualCmdlet = Group-Object -InputObject $testObject

	$actualAlias.Name[0] | Should Be $actualCmdlet.Name[0]
	$actualAlias.Group[0] | Should Be $actualCmdlet.Group[0]

    }

    It "Should be able to use the property switch without error" {
	{ $testObject | Group-Object -Property Attributes } | Should Not Throw

	$actual = $testObject | Group-Object -Property Attributes

	$actual.Group.Count | Should BeGreaterThan 0
    }

    It "Should be able to use the property switch on multiple properties without error" {
	{ $testObject | Group-Object -Property Attributes, Length }

	$actual = $testObject | Group-Object -Property Attributes, Length

	$actual.Group.Count | Should BeGreaterThan 0
    }

    It "Should be able to omit members of a group using the NoElement switch without error" {
	{ $testObject | Group-Object -NoElement } | Should Not Throw

	($testObject | Group-Object -NoElement).Group | Should BeNullOrEmpty
    }

    It "Should be able to output a hashtable datatype" {
	$actual = $testObject | Group-Object -AsHashTable

	$actual.GetType().Name | Should be "Hashtable"
    }

    It "Should be able to access when output as hash table" {
	$actual = $testObject | Group-Object -AsHashTable

	$actual.Keys | Should Not BeNullOrEmpty
    }

    It "Should throw when attempting to use AsString without AsHashTable" {
	{ $testObject | Group-Object -AsString } | Should Throw
    }

    It "Should not throw error when using AsString when the AsHashTable was added" {
	{ $testObject | Group-Object -AsHashTable -AsString } | Should Not Throw
    }
}
