Describe "Write-Verbose" -Tags "CI" {
    It "Should be able to call cmdlet without error" {
	{ Write-Verbose -Message "test" -ErrorAction SilentlyContinue } | Should Not Throw
    }

    It "Should not display verbose output by default" {
	$VerbosePreference | Should Be SilentlyContinue

	Write-Verbose -Message "test" | Should BeNullOrEmpty
    }

    It "Should be able to set verbose output to display by changing the `$VerbosePreference automatic variable" {
	$VerbosePreference = "Continue"

	Write-Verbose -Message "test" 4>&1 | Should Not BeNullOrEmpty

	$VerbosePreference = "SilentlyContinue"
    }

    It "Should be able to set verbose output to display by using the verbose switch" {
	Write-Verbose -Message "test" -Verbose 4>&1 | Should Be "test"
    }

    It "Should be able to set verbose switch using a colon and boolean" {
	{ Write-Verbose -Message "test" -Verbose:$false } | Should Not Throw

	$(Write-Verbose -Message "test" -Verbose:$true) 4>&1 | Should Be "test"
    }
}
