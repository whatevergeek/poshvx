
Describe "Type accelerators" -Tags "CI" {
    $TypeAcceleratorsType = [psobject].Assembly.GetType("System.Management.Automation.TypeAccelerators")

    $TypeAccelerators = $TypeAcceleratorsType::Get
    $TypeAcceleratorsType::Add('msft_2174855', [int])
    $TypeAcceleratorsType::Add('msft_2174855_rm', [int])

    It "Basic type accelerator usage" {
        [msft_2174855] | Should Be ([int])
    }

    It "Can query type accelerators" {
        if ( $IsCoreCLR ) { $count = 80 } else { $count = 82 }
        $TypeAccelerators.Count -gt $count | Should Be $true
        $TypeAccelerators['xml'] | Should Be ([System.Xml.XmlDocument])
        $TypeAccelerators['AllowNull'] | Should Be ([System.Management.Automation.AllowNullAttribute])
    }

    It "Can remove type accelerator" {
        $TypeAcceleratorsType::Get['msft_2174855_rm'] | Should Be ([int])
        $TypeAcceleratorsType::Remove('msft_2174855_rm')
        $TypeAcceleratorsType::Get['msft_2174855_rm'] | Should Be $null
    }
}
