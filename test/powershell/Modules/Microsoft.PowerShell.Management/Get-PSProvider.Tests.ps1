Describe "Get-PSProvider" -Tags "CI" {
    It "Should be able to call with no parameters without error" {
	{ Get-PSProvider } | Should Not Throw
    }

    It "Should be able to call the filesystem provider" {
	{ Get-PSProvider FileSystem } | Should Not Throw

	$actual = Get-PSProvider FileSystem

	$actual.Name | Should Be "FileSystem"

	$actual.Capabilities | Should Be "Filter, ShouldProcess, Credentials"
    }

    It "Should be able to call a provider with a wildcard expression" {
	{ Get-PSProvider File*m } | Should Not Throw
    }

    It "Should be able to pipe the output" {
	$actual = Get-PSProvider

	{ $actual | Format-List } | Should Not Throw
    }
}
