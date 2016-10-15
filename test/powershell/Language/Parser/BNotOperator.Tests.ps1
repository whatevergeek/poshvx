

$baseTypes = @{
    [SByte] = 'sbyte';     [Byte] = 'byte'
    [Int16] = 'short';     [UInt16] = 'ushort'
    [Int32] = 'int';       [UInt32] = 'uint'
    [Int64] = 'long';      [UInt64] = 'ulong'
}

$ns = [Guid]::NewGuid() -replace '-',''

$typeDefinition = "namespace ns_$ns`n{"

$enumTypeNames = foreach ($baseType in $baseTypes.Keys)
{
    $baseTypeName = $baseTypes[$baseType]
    $typeDefinition += @"
    public enum E_$baseTypeName : $baseTypeName
    {
        Min = $($baseType::MinValue),
        MinPlus1 = $($baseType::MinValue + 1),
        MaxMinus1 = $($baseType::MaxValue - 1),
        Max = $($baseType::MaxValue)
    }
"@

    "ns_$ns.E_$baseTypeName"
}

$typeDefinition += "`n}"

Write-Verbose $typeDefinition
Add-Type $typeDefinition

Describe "bnot on enums" -Tags "CI" {
    foreach ($enumType in [type[]]$enumTypeNames)
    {
        Context $enumType.Name {
            It "max - 1" {
                $res = -bnot $enumType::MaxMinus1
                $res | Should Be $enumType::MinPlus1
                $res.GetType() | Should Be $enumType
            }

            It "min + 1" {
                $res = -bnot $enumType::MinPlus1
                $res | Should Be $enumType::MaxMinus1
                $res.GetType() | Should Be $enumType
            }

            It "Max" {
                $res = -bnot $enumType::Max
                $res | Should Be $enumType::Min
                $res.GetType() | Should Be $enumType
            }

            It "Min" {
                $res = -bnot $enumType::Min
                $res | Should Be $enumType::Max
                $res.GetType() | Should Be $enumType
            }
        }
    }
}

Describe "bnot on integral types" -Tags "CI" {
    foreach ($baseType in $baseTypes.Keys)
    {
        Context $baseType.Name  {

            $max = $baseType::MaxValue 
            $maxMinus1 = $max - 1
            $min = $baseType::MinValue
            $minPlus1 = $min + 1

            if ([System.Runtime.InteropServices.Marshal]::SizeOf([type]$baseType) -lt 4)
            {
                $expectedResultType = [int]
            }
            else
            {
                $expectedResultType = $baseType
            }

            if ($baseType -eq [byte] -or $baseType -eq [uint16])
            {
                # Because of type promotion rules, unsigned types smaller than int
                # don't quite follow the pattern of "flip all the bits", so our
                # tests are a little different.
                It "max - 1" {
                    $res = -bnot $maxMinus1
                    $res | Should Be (-bnot [int]$maxMinus1)
                    $res.GetType() | Should Be $expectedResultType
                }

                It "min + 1" {
                    $res = -bnot $minPlus1
                    $res | Should Be (-bnot [int]$minPlus1)
                    $res.GetType() | Should Be $expectedResultType
                }

                It "max" {
                    $res = -bnot $max
                    $res | Should Be (-bnot [int]$max)
                    $res.GetType() | Should Be $expectedResultType
                }

                It "min" {
                    $res = -bnot $min
                    $res | Should Be (-bnot [int]$min)
                    $res.GetType() | Should Be $expectedResultType
                }
                return
            }

            It "max - 1" {
                $res = -bnot $maxMinus1
                $res | Should Be $minPlus1
                $res.GetType() | Should Be $expectedResultType
            }

            It "min + 1" {
                $res = -bnot $minPlus1
                $res | Should Be $maxMinus1
                $res.GetType() | Should Be $expectedResultType
            }

            It "max" {
                $res = -bnot $max
                $res | Should Be $min
                $res.GetType() | Should Be $expectedResultType
            }

            It "min" {
                $res = -bnot $min
                $res | Should Be $max
                $res.GetType() | Should Be $expectedResultType
            }
        }
    }
}

