$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Describe "ConvertFrom-Csv" -Tags "CI" {
    $testObject = "a", "1"
    $testcsv = Join-Path -Path (Join-Path -Path $here -ChildPath assets) -ChildPath TestCsv2.csv
    $testName = "Zaphod BeebleBrox"
    $testColumns = @"
a,b,c
1,2,3
"@
    It "Should be able to be called" {
	{ ConvertFrom-Csv -InputObject $testObject } | Should Not Throw
    }

    It "Should be able to pipe" {
	{ $testObject | ConvertFrom-Csv } | Should Not Throw
    }

    It "Should have expected results when using piped inputs" {
	$csvContent   = Get-Content $testcsv
	$actualresult = $csvContent | ConvertFrom-Csv

	$actualresult.GetType().BaseType.Name | Should Be "Array"
	$actualresult[0].GetType().Name          | Should Be "PSCustomObject"

	#Should have a name property in the result
	$actualresult[0].Name | Should Be $testName
    }

    It "Should be able to set a delimiter" {
	{ $testcsv | ConvertFrom-Csv -Delimiter ";" } | Should Not Throw
    }

    It "Should actually delimit the output" {
	$csvContent   = Get-Content $testcsv
	$actualresult = $csvContent | ConvertFrom-Csv -Delimiter ";"

	$actualresult.GetType().BaseType.Name    | Should Be "Array"
	$actualresult[0].GetType().Name          | Should Be "PSCustomObject"

	# ConvertFrom-Csv takes the first line of the input as a header by default
	$actualresult.Length | Should Be $($csvContent.Length - 1)
    }

    It "Should be able to have multiple columns" {
	$actualData   = $testColumns | ConvertFrom-Csv

	$actualLength = $($( $actualData | gm) | Where-Object {$_.MemberType -eq "NoteProperty" }).Length

	$actualLength | Should Be 3
    }
}

Describe "ConvertFrom-Csv DRT Unit Tests" -Tags "CI" {
    It "Test ConvertFrom-Csv with pipelined InputObject and Header" {
        $inputObject = [pscustomobject]@{ First = 1; Second = 2 } 
        $res = $inputObject | ConvertTo-Csv         
        $result = $res | ConvertFrom-Csv -Header "Header1","Header2"

        $result[0].Header1 | Should Be "First"
        $result[0].Header2 | Should Be "Second"
        $result[1].Header1 | Should Be "1"
        $result[1].Header2 | Should Be "2"
    }
}
