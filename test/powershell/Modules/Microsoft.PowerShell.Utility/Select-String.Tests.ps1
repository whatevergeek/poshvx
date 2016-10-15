Describe "Select-String" -Tags "CI" {
    $nl = [Environment]::NewLine
    $currentDirectory = $pwd.Path
    Context "String actions" {
	$testinputone = "hello","Hello","goodbye"
	$testinputtwo = "hello","Hello"

	it "Should be called without errors" {
	    { $testinputone | Select-String -Pattern "hello" } | Should Not Throw
	}

	it "Should be called without error using the sls alias" {
	    { $testinputone | sls -Pattern "hello" } | Should Not Throw
	}

	it "Should return an array data type when multiple matches are found" {
	    ( $testinputtwo | Select-String -Pattern "hello").gettype().basetype | Should Be Array
	}

	it "Should return the same result for the alias sls and Select-String " {
	    $firstMatch = $testinputone | Select-String -Pattern "hello"
	    $secondMatch = $testinputone | sls -Pattern "hello"

	    $equal = @(compare-object $firstMatch $secondMatch).Length -eq 0
	    $equal | Should Be True
	}

	it "Should return an object type when one match is found" {
	    ( $testinputtwo | Select-String -Pattern "hello" -CaseSensitive).gettype().basetype | Should Be System.Object
	}

	it "Should return matchinfo type" {
	    ( $testinputtwo | Select-String -Pattern "hello" -CaseSensitive).gettype().name | Should Be MatchInfo
	}

	it "Should be called without an error using ca for casesensitive " {
	    {$testinputone | Select-String -Pattern "hello" -ca } | Should Not Throw
	}

	it "Should use the ca alias for casesenstive" {
	    $firstMatch = $testinputtwo  | Select-String -Pattern "hello" -CaseSensitive
	    $secondMatch = $testinputtwo | Select-String -Pattern "hello" -ca

	    $equal = @(Compare-Object $firstMatch $secondMatch).Length -eq 0
	    $equal | Should Be True
	}

	it "Should only return the case sensitive match when the casesensitive switch is used" {
	    $testinputtwo | Select-String -Pattern "hello" -CaseSensitive | Should Be "hello"
	}

	it "Should accept a collection of strings from the input object" {
	    { Select-String -InputObject "some stuff", "other stuff" -Pattern "other" } | Should Not Throw
	}

	it "Should return system.object when the input object switch is used on a collection" {
	    ( Select-String -InputObject "some stuff", "other stuff" -pattern "other" ).gettype().basetype | Should Be System.Object
	}

	it "Should return null or empty when the input object switch is used on a collection and the pattern does not exist" {
	    Select-String -InputObject "some stuff", "other stuff" -Pattern "neither" | Should BeNullOrEmpty
	}

	it "Should return a bool type when the quiet switch is used" {
	    ($testinputtwo | Select-String -Quiet "hello" -CaseSensitive).gettype() | Should Be Bool
	}

	it "Should be true when select string returns a positive result when the quiet switch is used" {
	    ($testinputtwo | Select-String -Quiet "hello" -CaseSensitive) | Should Be $True
	}

	it "Should be empty when select string does not return a result when the quiet switch is used" {
	    $testinputtwo | Select-String -Quiet "goodbye"  | Should BeNullOrEmpty
	}

	it "Should return an array of non matching strings when the switch of NotMatch is used and the string do not match" {
	    $testinputone | Select-String -Pattern "goodbye" -NotMatch | Should Be "hello", "hello"
	}

	it "Should return the same as NotMatch" {
	    $firstMatch = $testinputone | Select-String -pattern "goodbye" -NotMatch
	    $secondMatch = $testinputone | Select-String -pattern "goodbye" -n

	    $equal = @(Compare-Object $firstMatch $secondMatch).Length -eq 0
	    $equal | Should Be True
	}
    }

    Context "Filesytem actions" {
	$testDirectory = $TestDrive
	$testInputFile = Join-Path -Path $testDirectory -ChildPath testfile1.txt

	BeforeEach {
	    New-Item $testInputFile -Itemtype "file" -Force -Value "This is a text string, and another string${nl}This is the second line${nl}This is the third line${nl}This is the fourth line${nl}No matches"
	}

	AfterEach {
	    Remove-Item $testInputFile -Force
	}

	It "Should return an object when a match is found is the file on only one line" {
	    (Select-String $testInputFile -Pattern "string").GetType().BaseType | Should be System.Object
	}

	It "Should return an array when a match is found is the file on several lines" {
	    (Select-String $testInputFile -Pattern "in").GetType().BaseType | Should be array
	    (Select-String $testInputFile -Pattern "in")[0].GetType().Name  | Should Be MatchInfo
	}

	It "Should return the name of the file and the string that 'string' is found if there is only one lines that has a match" {
	    $expected = $testInputFile + ":1:This is a text string, and another string"

	    Select-String $(Split-Path $testInputFile -NoQualifier) -Pattern "string" | Should Be $expected
	}

	It "Should return all strings where 'second' is found in testfile1 if there is only one lines that has a match" {
	    $expected = $testInputFile + ":2:This is the second line"

	    Select-String $testInputFile  -Pattern "second"| Should Be $expected
	}

	It "Should return all strings where 'in' is found in testfile1 pattern switch is not required" {
	    $expected1 = "This is a text string, and another string"
	    $expected2 = "This is the second line"
	    $expected3 = "This is the third line"
	    $expected4 = "This is the fourth line"

	    (Select-String in $testInputFile)[0].Line | Should Be $expected1
	    (Select-String in $testInputFile)[1].Line | Should Be $expected2
	    (Select-String in $testInputFile)[2].Line | Should Be $expected3
	    (Select-String in $testInputFile)[3].Line | Should Be $expected4
	    (Select-String in $testInputFile)[4].Line | Should BeNullOrEmpty
	}

	It "Should return empty because 'for' is not  found in testfile1 " {
	    Select-String for $testInputFile | Should BeNullOrEmpty
	}

	It "Should return the third line in testfile1 and the lines above and below it " {
	    $expectedLine       = "testfile1.txt:2:This is the second line"
	    $expectedLineBefore = "testfile1.txt:3:This is the third line"
	    $expectedLineAfter  = "testfile1.txt:4:This is the fourth line"

	    Select-String third $testInputFile -Context 1 | Should Match $expectedLine
	    Select-String third $testInputFile -Context 1 | Should Match $expectedLineBefore
	    Select-String third $testInputFile -Context 1 | Should Match $expectedLineAfter
	}

	It "Should return the number of matches for 'is' in textfile1 " {
	    (Select-String is $testInputFile -CaseSensitive).count| Should Be 4
	}

	It "Should return the third line in testfile1 when a relative path is used" {
	    $expected  = "testfile1.txt:3:This is the third line"

	    $relativePath = Join-Path -Path $testDirectory -ChildPath ".."
	    $relativePath = Join-Path -Path $relativePath -ChildPath ".."
	    $relativePath = Join-Path -Path $relativePath -ChildPath (Split-Path $testDirectory -NoQualifier)
	    $relativePath = Join-Path -Path $relativePath -ChildPath testfile1.txt
	    Select-String third $relativePath  | Should Match $expected
	}

	It "Should return the fourth line in testfile1 when a relative path is used" {
	    $expected = "testfile1.txt:5:No matches"

	    pushd $testDirectory

	    Select-String matches (Join-Path -Path $testDirectory -ChildPath testfile1.txt)  | Should Match $expected
	    popd
	}

	It "Should return the fourth line in testfile1 when a regular expression is used" {
	    $expected  = "testfile1.txt:5:No matches"

	    Select-String 'matc*' $testInputFile -CaseSensitive | Should Match $expected
	}

	It "Should return the fourth line in testfile1 when a regular expression is used, using the alias for casesensitive" {
	    $expected  = "testfile1.txt:5:No matches"

	    Select-String 'matc*' $testInputFile -ca | Should Match $expected
	}
    }
    Push-Location $currentDirectory
}
