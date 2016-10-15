Describe "Format-Custom" -Tags "CI" {

    Context "Check Format-Custom aliases" {

        It "Should have the same output between the alias and the unaliased function" {
            $nonaliased = Get-FormatData | Format-Custom
            $aliased    = Get-FormatData | fc
            $($nonaliased | Out-String).CompareTo($($aliased | Out-String)) | Should Be 0
        }
    }

    Context "Check specific flags on Format-Custom" {

        It "Should be able to specify the depth in output" {
            $getprocesspester =  Get-FormatData | Format-Custom -depth 1
            ($getprocesspester).Count                   | Should BeGreaterThan 0
        }

        It "Should be able to use the Property flag to select properties" {
            $CommandName = Get-Command | Format-Custom -Property "Name"
            $CommandName               | Should Not Match "Source"
        }

    }
}


Describe "Format-Custom DRT basic functionality" -Tags "CI" {
	Add-Type -TypeDefinition @"
    public abstract class NamedItem
    {
        public string name;
    }

    public abstract class MyContainerBase : NamedItem
    {
        public System.Collections.Generic.List<NamedItem> children = new System.Collections.Generic.List<NamedItem> ();
    }

    public sealed class MyLeaf1 : NamedItem
    {
        public int val1 = 0;
    }

    public sealed class MyLeaf2 : NamedItem
    {
        public int val2 = 0;
    }

    public sealed class MyContainer1 : MyContainerBase
    {
        public string data1;
    }

    public sealed class MyContainer2 : MyContainerBase
    {
        public string data2;
    }
"@

    It "Format-Custom with subobject should work" {
        $expectResult1 = "this is the name"
        $expectResult2 = "this is the name of the sub object"
        $testObject = @{}
        $testObject.name = $expectResult1
        $testObject.subObjectValue = @{}
        $testObject.subObjectValue.name = $expectResult2
        $testObject.subObjectValue.array = (0..63)
        $testObject.subObjectValue.stringarray = @("one","two")
        $result = $testObject | Format-Custom | Out-String
        $result | Should Match $expectResult1
        $result | Should Match $expectResult2
        $result | Should Match "one"
        $result | Should Match "two"
    }
	
	It "Format-Custom with Tree Object should work" {
		$expectedResult=@"
class MyContainer1
{
  data1 = data 1
  children = 
    [
      class MyLeaf1
      {
        val1 = 10
        name = leaf 1
      }
      class MyLeaf2
      {
        val2 = 20
        name = leaf 2
      }
    ]
    
  name = container 1
}

class MyContainer2
{
  data2 = data 2
  children = 
    [
      class MyLeaf1
      {
        val1 = 10
        name = leaf 1
      }
      class MyLeaf2
      {
        val2 = 20
        name = leaf 2
      }
    ]
    
  name = container 2
}

class MyContainer2
{
  data2 = data 2 deep
  children = 
    [
      class MyContainer1
      {
        data1 = cx data
        children = 
          [
            class MyLeaf1
            {
              val1 = 10
              name = leaf 1
            }
            class MyLeaf2
            {
              val2 = 20
              name = leaf 2
            }
          ]
          
        name = cx
      }
    ]
    
  name = container 2 deep
}

class MyContainer1
{
  data1 = cx data
  children = 
    [
      class MyLeaf1
      {
        val1 = 10
        name = leaf 1
      }
      class MyLeaf2
      {
        val2 = 20
        name = leaf 2
      }
    ]
    
  name = cx
}
"@
		$leaf1 = New-Object MyLeaf1
		$leaf1.name = "leaf 1"
		$leaf1.val1 = 10

		$leaf2 = New-Object MyLeaf2
		$leaf2.name = "leaf 2"
		$leaf2.val2 = 20

		$c1 = New-Object MyContainer1
		$c1.name = "container 1"
		$c1.data1 = "data 1"
		$c1.children.Add($leaf1)
		$c1.children.Add($leaf2)

		$c2 = New-Object MyContainer2
		$c2.name = "container 2"
		$c2.data2 = "data 2"
		$c2.children.Add($leaf1)
		$c2.children.Add($leaf2)

		$cDeep = New-Object MyContainer2
		$cDeep.name = "container 2 deep"
		$cDeep.data2 = "data 2 deep"

		$cx= New-Object MyContainer1
		$cx.name = "cx"
		$cx.data1 = "cx data"
		$cx.children.Add($leaf1)
		$cx.children.Add($leaf2)
		$cDeep.children.Add($cx)

		$objectList = @($c1,$c2,$cDeep,$cx)
		$result = $objectList | Format-Custom | Out-String
		$result = $result -replace "[{} `n\r]",""
		$expectedResult = $expectedResult -replace "[{} `n\r]",""
		$result | Should Be $expectedResult
	}
	
	It "Format-Custom with Empty Data Tree Object should work" {
		$expectedResult=@"
class MyContainer1
{
  data1 =
  children =
  name =
}
"@

		$c= New-Object MyContainer1
		$c.name = $null
		$c.data1 = $null
		$c.children = $null
		$objectList = @($c)
		
		$result = $objectList | Format-Custom | Out-String
		$result = $result -replace "[{} `n\r]",""
		$expectedResult = $expectedResult -replace "[{} `n\r]",""
		$result | Should Be $expectedResult
	}
	
	It "Format-Custom with Back Pointers Tree Object should work" {
		$expectedResult=@"
class MyContainer1
{
  data1 =
  children =
    [
      class MyContainer1
      {
        data1 =
        children =
          [
            class MyContainer1
            {
              data1 =
              children =
                [
                  MyContainer1
                ]

              name = ROOT
            }
          ]

        name = ROOT
      }
    ]

  name = ROOT
}
"@

		$root= New-Object MyContainer1
		$root.name = "ROOT"
		$root.children.Add($root)
		$objectList = @($root)
		
		$result = $objectList | Format-Custom | Out-String
		$result = $result -replace "[{} `n\r]",""
		$expectedResult = $expectedResult -replace "[{} `n\r]",""
		$result | Should Be $expectedResult
	}
	
	It "Format-Custom with Leaf Only Data should work" {
		$expectedResult=@"
class MyLeaf1
{
  val1 = 10
  name = leaf 1
}

class MyLeaf2
{
  val2 = 20
  name = leaf 2
}
"@

		$leaf1 = New-Object MyLeaf1
		$leaf1.name = "leaf 1"
		$leaf1.val1 = 10

		$leaf2 = New-Object MyLeaf2
		$leaf2.name = "leaf 2"
		$leaf2.val2 = 20

		$objectList = @($leaf1,$leaf2)
		$result = $objectList | Format-Custom | Out-String
		$result = $result -replace "[{} `n\r]",""
		$expectedResult = $expectedResult -replace "[{} `n\r]",""
		$result | Should Be $expectedResult
	}
}
