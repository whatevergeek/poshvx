
Describe 'Automatic variable $input' -Tags "CI" {
    # Skip on hold for discussion on https://github.com/PowerShell/PowerShell/issues/1563
    # $input type in advanced functions
    It '$input Type should be enumerator' -Skip {
        function from_begin { [cmdletbinding()]param() begin { Write-Output -NoEnumerate $input } }
        function from_process { [cmdletbinding()]param() process { Write-Output -NoEnumerate $input } }
        function from_end { [cmdletbinding()]param() end { Write-Output -NoEnumerate $input } }

        (from_begin) -is [System.Collections.IEnumerator] | Should Be $true
        (from_process) -is [System.Collections.IEnumerator] | Should Be $true
        (from_end) -is [System.Collections.IEnumerator] | Should Be $true
    }

    It 'Empty $input really is empty' {
        & { @($input).Count } | Should Be 0
        & { [cmdletbinding()]param() begin { @($input).Count } } | Should Be 0
        & { [cmdletbinding()]param() process { @($input).Count } } | Should Be 0
        & { [cmdletbinding()]param() end { @($input).Count } } | Should Be 0
    }
}
