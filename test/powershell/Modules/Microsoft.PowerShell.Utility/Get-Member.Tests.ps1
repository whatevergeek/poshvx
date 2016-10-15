Describe "Get-Member" -Tags "CI" {
    It "Should be able to be called on string objects, ints, arrays, etc" {
	$a = 1 #test numbers
	$b = 1.3
	$c = $false #test bools
	$d = @(1,3) # test arrays
	$e = "anoeduntodeu" #test strings
	$f = 'asntoheusth' #test strings

	Get-Member -InputObject $a | Should Not BeNullOrEmpty
	Get-Member -InputObject $b | Should Not BeNullOrEmpty
	Get-Member -InputObject $c | Should Not BeNullOrEmpty
	Get-Member -InputObject $d | Should Not BeNullOrEmpty
	Get-Member -InputObject $e | Should Not BeNullOrEmpty
	Get-Member -InputObject $f | Should Not BeNullOrEmpty
    }

    It "Should be able to extract a field from string objects, ints, arrays, etc" {
	$a = 1 #test numbers
	$b = 1.3
	$c = $false #test bools
	$d = @(1,3) # test arrays
	$e = "anoeduntodeu" #test strings
	$f = 'asntoheusth' #test strings

	$a.GetType().Name | Should Be 'Int32'
	$b.GetType().Name | Should Be 'Double'
	$c.GetType().Name | Should Be 'Boolean'
	$d.GetType().Name | Should Be 'Object[]'
	$e.GetType().Name | Should Be 'String'
	$f.GetType().Name | Should Be 'String'
    }

    It "Should be able to be called on a newly created PSObject" {
	$o = New-Object psobject
	# this creates a dependency on the Add-Member cmdlet.
	Add-Member -InputObject $o -MemberType NoteProperty -Name proppy -Value "superVal"

	Get-Member -InputObject $o | Should Not BeNullOrEmpty
    }
}

Describe "Get-Member DRT Unit Tests" -Tags "CI" {
    Context "Verify Get-Member with Class" {
		if(-not ([System.Management.Automation.PSTypeName]'Employee').Type)
		{
			Add-Type -TypeDefinition @"
        public class Employee
        {
            private string firstName;
            private string lastName;
            private int    yearsInMS;

            public string FirstName
            {
                get
                {
                    return firstName;
                }
                set
                {
                    firstName = value;
                }
            }


            public string LastName
            {
                get
                {
                    return lastName;
                }
                set
                {
                    lastName = value;
                }
            }


            public int YearsInMS
            {
                get
                {
                    return yearsInMS;
                }
                set
                {
                    yearsInMS = value;
                }
            }
 
            public Employee(string firstName, string lastName, int yearsInMS)
            {
                this.firstName = firstName;
                this.lastName  = lastName;
                this.yearsInMS = yearsInMS;
            }
        }
"@
		}

        $fileToDeleteName = Join-Path $TestDrive -ChildPath "getMemberTest.ps1xml"
        $XMLFile= @"
<Types>
    <Type>
    <Name>Employee</Name>
    <Members>
        <PropertySet>
            <Name>PropertySetName</Name>
            <ReferencedProperties>
                <Name>FirstName</Name> 
                <Name>LastName</Name> 
            </ReferencedProperties>
        </PropertySet>
        <PropertySet>
            <Name>FullSet</Name>
            <ReferencedProperties>
                <Name>FirstName</Name> 
                <Name>LastName</Name> 
                <Name>YearsInMS</Name> 
            </ReferencedProperties>
        </PropertySet>
    </Members>
    </Type>
    <Type>
    <Name>EmployeePartTime</Name>
    <Members>
        <PropertySet>
            <Name>PropertySetName</Name>
            <ReferencedProperties>
                <Name>FirstName</Name> 
                <Name>HoursPerWeek</Name> 
            </ReferencedProperties>
        </PropertySet>
        <PropertySet>
            <Name>FullSet</Name>
            <ReferencedProperties>
                <Name>FirstName</Name> 
                <Name>LastName</Name> 
                <Name>HoursPerWeek</Name> 
            </ReferencedProperties>
        </PropertySet>
    </Members>
    </Type>
</Types>
"@

        $XMLFile > $fileToDeleteName
        Update-TypeData -AppendPath $fileToDeleteName

        It "Fail to get member without any input" {
            try 
            {
                Get-Member -MemberType All -ErrorAction Stop 
                Throw "Execution OK"
            }
            catch
            {
                $_.FullyQualifiedErrorId | Should Be 'NoObjectInGetMember,Microsoft.PowerShell.Commands.GetMemberCommand'
            }
        }

        It 'Get the expected Properties of "Employee" object' {
            $emps = [Employee]::New("john", "smith", 5), [Employee]::New("joesph", "smith", 15), [Employee]::New("john", "smyth", 2)
            $results = $emps | Get-Member -MemberType Property
            $results.Length | Should Be 3
            $results[0].Name | Should Be "FirstName"
            $results[1].Name | Should Be "LastName"
            $results[2].Name | Should Be "YearsInMS"
        }

        It 'Get the Public Methods of "Employee" object' {
            $emps = [Employee]::New("john", "smith", 5), [Employee]::New("joesph", "smith", 15), [Employee]::New("john", "smyth", 2)
            $methodList = "GetHashCode", "Equals", "ToString", "GetType"
            $results = $emps | Get-Member -MemberType Method
            $results.Length | Should Be $methodList.Length
            $methodFound = @()
        
            for ($i = 0;$i -lt $methodList.Length;$i++)
            {
                for ($j = 0;$j -lt $results.Length;$j++)
                {
                    if ($results[$j].Name.Equals($methodList[$i]))
                    {
                        $methodFound += $true
                    }
                } 
            }

            for ($i = 0;$i -lt $methodList.Length;$i++)
            {
                $methodFound[$i] | Should Be $true
            }
        }

        It 'Get property sets defined in private members' {
            $emps = [Employee]::New("john", "smith", 5), [Employee]::New("joesph", "smith", 15), [Employee]::New("john", "smyth", 2)
            $results = $emps | Get-Member -MemberType PropertySet
            $results.Length | Should Be 2
            $results[0].Name | Should Be "FullSet"
            $results[1].Name | Should Be "PropertySetName"
        }
    }

    Context "Verify Get-Member with Static Parameter" {
        It 'Get the static properties and methods of the object' {
            $obj = New-Object -TypeName System.Int32
            $results = $obj | Get-Member -Static
            $members = "MaxValue", "MinValue", "Parse", "TryParse"
            foreach ($member in $members)
            {
                foreach ($result in $results)
                {
                    if($result.Name.Equals($member))
                    {
                        return $true
                    }
                }
                return $false
            }
        }

        It "Get the static properties and methods of int instance" {
            $results = 1 | Get-Member -Static
            $members = "MaxValue", "MinValue", "Parse", "TryParse"
            foreach ($member in $members)
            {
                foreach ($result in $results)
                {
                    if($result.Name.Equals($member))
                    {
                        return $true
                    }
                }
                return $false
            }
        }

        It "Get the static properties and methods of int instance Wrapped" {
            $results = [pscustomobject]1 | Get-Member -Static
            $members = "MaxValue", "MinValue", "Parse", "TryParse"
            foreach ($member in $members)
            {
                foreach ($result in $results)
                {
                    if($result.Name.Equals($member))
                    {
                        return $true
                    }
                }
                return $false
            }
        }
    }

    Context "Verify Get-Member with other parameters" {
        It 'works with View Parameter' {
            $results = [xml]'<a>some text</a>' | Get-Member -view adapted
            $results.Length | Should Be 38       
        }

        It 'Get hidden members'{
            $results = 'abc' | Get-Member -force
            $hiddenMembers = "psbase", "psextended", "psadapted", "pstypenames", "psobject"
            foreach ($member in $hiddenMembers)
            {
                foreach ($result in $results)
                {
                    if($result.Name.Equals($member))
                    {
                        return $true
                    }
                }
                return $false
            }
        }

        It 'Get Set Property Accessors On PsBase'{
            $results = ('abc').psbase | Get-Member -force get_*
            $expectedMembers = "get_Chars", "get_Length"
            foreach ($member in $expectedMembers)
            {
                foreach ($result in $results)
                {
                    if($result.Name.Equals($member))
                    {
                        return $true
                    }
                }
                return $false
            }
        }
    }
}
