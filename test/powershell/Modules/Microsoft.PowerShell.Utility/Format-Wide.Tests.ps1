Describe "Format-Wide" -Tags "CI" {

    It "Should have the same output between the alias and the unaliased function" {
        $nonaliased = Get-ChildItem | Format-Wide
        $aliased    = Get-ChildItem | fw

        $($nonaliased | Out-String).CompareTo($($aliased | Out-String)) | Should Be 0
    }

    It "Should be able to specify the columns in output using the column switch" {
        { Get-ChildItem | Format-Wide -Column 3 } | Should Not Throw
    }

    It "Should be able to use the autosize switch" {
        { Get-ChildItem | Format-Wide -Autosize } | Should Not Throw
    }

    It "Should be able to take inputobject instead of pipe" {
        { Format-Wide -InputObject $(Get-ChildItem) } | Should Not Throw
    }

    It "Should be able to use the property switch" {
        { Format-Wide -InputObject $(Get-ChildItem) -Property Mode } | Should Not Throw
    }

    It "Should throw an error when property switch and view switch are used together" {
        try
		{
			Format-Wide -InputObject $(Get-ChildItem) -Property CreationTime -View aoeu
		}
		catch
		{
			$_.FullyQualifiedErrorId | Should be "FormatCannotSpecifyViewAndProperty,Microsoft.PowerShell.Commands.FormatWideCommand"
		}
    }

    It "Should throw and suggest proper input when view is used with invalid input without the property switch" {
        { Format-Wide -InputObject $(Get-Process) -View aoeu } | Should Throw
    }
}

Describe "Format-Wide DRT basic functionality" -Tags "CI" {
  It "Format-Wide with array should work" {
		$al = (0..255)
		$info = @{}
		$info.array = $al
		$result = $info | Format-Wide | Out-String
		$result | Should Match "array"
	}
	
	It "Format-Wide with No Objects for End-To-End should work"{
		$p = @{}
		$result = $p | Format-Wide | Out-String
		$result | Should BeNullOrEmpty
	}
	
	It "Format-Wide with Null Objects for End-To-End should work"{
		$p = $null
		$result = $p | Format-Wide | Out-String
		$result | Should BeNullOrEmpty
	}
	
	It "Format-Wide with single line string for End-To-End should work"{
		$p = "single line string"
		$result = $p | Format-Wide | Out-String
		$result | Should Match $p
	}
	
	It "Format-Wide with multiple line string for End-To-End should work"{
		$p = "Line1\nLine2"
		$result = $p | Format-Wide | Out-String
		$result | Should Match "Line1"
		$result | Should Match "Line2"
	}
	
	It "Format-Wide with string sequence for End-To-End should work"{
		$p = "Line1","Line2"
		$result = $p |Format-Wide | Out-String
		$result | Should Match "Line1"
		$result | Should Match "Line2"
	}
	
   It "Format-Wide with complex object for End-To-End should work" {
		Add-Type -TypeDefinition "public enum MyDayOfWeek{Sun,Mon,Tue,Wed,Thr,Fri,Sat}"
		$eto = New-Object MyDayOfWeek
		$info = @{}
		$info.intArray = 1,2,3,4
		$info.arrayList = "string1","string2"
		$info.enumerable = [MyDayOfWeek]$eto
		$info.enumerableTestObject = $eto
		$result = $info|Format-Wide|Out-String
		$result | Should Match "intArray"
		$result | Should Match "arrayList"
		$result | Should Match "enumerable"
		$result | Should Match "enumerableTestObject"
	}
	
	It "Format-Wide with multiple same class object with grouping should work"{
		Add-Type -TypeDefinition "public class TestGroupingClass{public TestGroupingClass(string name,int length){Name = name;Length = length;}public string Name;public int Length;public string GroupingKey;}"
		$testobject1 = [TestGroupingClass]::New('name1',1)
		$testobject1.GroupingKey = "foo"
		$testobject2 = [TestGroupingClass]::New('name2',2)
		$testobject1.GroupingKey = "bar"
		$testobject3 = [TestGroupingClass]::New('name3',3)
		$testobject1.GroupingKey = "bar"
		$testobjects = @($testobject1,$testobject2,$testobject3)
		$result = $testobjects|Format-Wide -GroupBy GroupingKey|Out-String
		$result | Should Match "GroupingKey: bar"
		$result | Should Match "name1"
		$result | Should Match " GroupingKey:"
		$result | Should Match "name2\s+name3"
	}
}
