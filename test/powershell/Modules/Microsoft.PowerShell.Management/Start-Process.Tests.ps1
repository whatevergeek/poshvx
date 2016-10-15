Describe "Start-Process" -Tags @("CI","SLOW") {
    $pingCommand = (Get-Command -CommandType Application ping)[0].Definition
    $pingDirectory = Split-Path $pingCommand -Parent
    $tempFile = Join-Path -Path $TestDrive -ChildPath PSTest
    $assetsFile = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath assets) -ChildPath SortTest.txt
    if ($IsWindows) {
	$pingParam = "-n 2 localhost"
    }
    elseif ($IsLinux -Or $IsOSX) {
	$pingParam = "-c 2 localhost"
    }

    # Note that ProcessName may still be `powershell` due to dotnet/corefx#5378
    # This has been fixed on Linux, but not on OS X

    It "Should process arguments without error" {
	$process = Start-Process ping -ArgumentList $pingParam -PassThru

	$process.Length      | Should Be 1
	$process.Id          | Should BeGreaterThan 1
	# $process.ProcessName | Should Be "ping"
    }

    It "Should work correctly when used with full path name" {
	$process = Start-Process $pingCommand -ArgumentList $pingParam -PassThru

	$process.Length      | Should Be 1
	$process.Id          | Should BeGreaterThan 1
	# $process.ProcessName | Should Be "ping"
    }

    It "Should invoke correct path when used with FilePath argument" {
	$process = Start-Process -FilePath $pingCommand -ArgumentList $pingParam -PassThru

	$process.Length      | Should Be 1
	$process.Id          | Should BeGreaterThan 1
	# $process.ProcessName | Should Be "ping"
    }

    It "Should wait for command completion if used with Wait argument" {
	$process = Start-Process ping -ArgumentList $pingParam -Wait -PassThru
    }

    It "Should work correctly with WorkingDirectory argument" {
	$process = Start-Process ping -WorkingDirectory $pingDirectory -ArgumentList $pingParam -PassThru

	$process.Length      | Should Be 1
	$process.Id          | Should BeGreaterThan 1
	# $process.ProcessName | Should Be "ping"
    }

    It "Should should handle stderr redirection without error" {
	$process = Start-Process ping -ArgumentList $pingParam -PassThru -RedirectStandardError $tempFile

	$process.Length      | Should Be 1
	$process.Id          | Should BeGreaterThan 1
	# $process.ProcessName | Should Be "ping"
    }

    It "Should should handle stdout redirection without error" {
	$process = Start-Process ping -ArgumentList $pingParam -Wait -RedirectStandardOutput $tempFile
	$dirEntry = dir $tempFile
	$dirEntry.Length | Should BeGreaterThan 0
    }

    It "Should should handle stdin redirection without error" {
	$process = Start-Process sort -Wait -RedirectStandardOutput $tempFile -RedirectStandardInput $assetsFile
	$dirEntry = dir $tempFile
	$dirEntry.Length | Should BeGreaterThan 0
    }

    Remove-Item -Path $tempFile -Force
}
