﻿##
## Copyright (c) Microsoft Corporation, 2015
##
## Script debugging tests
##

Describe "Breakpoints set on custom FileSystem provider files should work" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>
    #      Regression test for
    #
    #              221362 Breakpoints set on files in Custom FileSystem providers and networked shares are not hit
    #
    #     </Summary>
    #  </Test>
    #
    try
    {
        #
        # Create a simple script
        #
        $scriptPath = [io.path]::GetTempPath()
        $scriptName = "DebuggerScriptTests-ExposeBug221362.ps1"
        $scriptFullName = [io.path]::Combine($scriptPath, $scriptName)

        write-output '"hello"' > $scriptFullName

        #
        # Create a file system provider
        #
        new-psdrive -name tmpTestA1 -psprovider FileSystem -root $scriptPath > $null

        #
        # Verify that the breakpoint is hit when using the provider
        #
        pushd tmpTestA1:\
        $breakpoint = set-psbreakpoint .\$scriptName 1 -action { continue }
        & .\$scriptName

	    It "Breakpoint hit count" {
		    $breakpoint.HitCount | Should Be 1
	    }
    }
    finally
    {
        popd

        if ($breakpoint -ne $null) { $breakpoint | remove-psbreakpoint }
        if (Test-Path $scriptFullName) { Remove-Item $scriptFullName -Force }
        if ((Get-PSDrive -Name tmpTestA1 2>$null) -ne $null) { Remove-PSDrive -Name tmpTestA1 -Force }
    }
}


Describe "Tests line breakpoints on dot-sourced files" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>Unit tests for line breakpoints on dot-sourced files...</Summary>
    #  </Test>

    try
    {
        #
        # Create the repro script
        #
        $scriptFile = [io.path]::Combine([io.path]::GetTempPath(), "DebuggerScriptTests-ExposeBug245331.ps1")

        write-output '
        function fibonacci
        {
            param($number)

            if ($number -eq 0) {
                return 0
            }

            if ($number -eq 1) {
                return 1
            }

            $f1 = fibonacci($number - 1)
            $f2 = fibonacci($number - 2)

            $f1 + $f2 ######################## line 17
        }

        fibonacci(3)
        ' > $scriptFile

        #
        # Set the breakpoint and verify it is hit
        #
        $breakpoint = sbp $scriptFile 17 -action { continue; }

        & $scriptFile

	    It "Breakpoint on recursive function hit count" {
		    $breakpoint.HitCount | Should BeGreaterThan 0
	    }
    }
    finally
    {
        if ($breakpoint -ne $null) { $breakpoint | remove-psbreakpoint }
        if (Test-Path $scriptFile) { Remove-Item -Path $scriptFile -Force }
    }
}


Describe "Function calls clear debugger cache too early" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>
    #      Regression test for
    #
    #              248703 Function calls clear debugger cache too early
    #
    #     </Summary>
    #  </Test>
    #
    try
    {
        #
        # Create the repro script
        #
        $scriptFile = [io.path]::Combine([io.path]::GetTempPath(), "DebuggerScriptTests-ExposeBug248703.ps1")

        write-output '
        function Hello
        {
            write-output "hello"
        }

        write-output "begin"  ### line 7
        Hello
        write-output "end"    ### line 9
        ' > $scriptFile

        #
        # Set the breakpoints and verify they are hit
        #
        $breakpoint1 = sbp $scriptFile 7 -action { continue; }
        $breakpoint2 = sbp $scriptFile 9 -action { continue; }

        & $scriptFile

	    It "Breakpoint before function call count" {
		    $breakpoint1.HitCount | Should Be 1
	    }

	    It "Breakpoint after function call count" {
		    $breakpoint2.HitCount | Should Be 1
	    }
    }
    finally
    {
        if ($breakpoint1 -ne $null) { $breakpoint1 | remove-psbreakpoint }
        if ($breakpoint2 -ne $null) { $breakpoint2 | remove-psbreakpoint }
        if (Test-Path $scriptFile) { Remove-Item $scriptFile -Force }
    }
}


Describe "Line breakpoints on commands in multi-line pipelines" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>
    #      Regression test for
    #
    #              588887 Line breakpoints on commands in multi-line pipelines are not hit
    #
    #     </Summary>
    #  </Test>

    $script = Join-Path ${TestDrive} ExposeBug588887.DRT.tmp.ps1

    try
    {
        Set-Content $script @'
        1..3 |
        % { $_ } | sort-object |
        get-unique
'@

        $breakpoints = sbp $script 1,2,3 -action { continue }

        $null = & $script

	    It "Breakpoint on line 1 hit count" {
		    $breakpoints[0].HitCount | Should Be 1
	    }

	    It "Breakpoint on line 2 hit count" {
		    $breakpoints[1].HitCount | Should Be 3
	    }

	    It "Breakpoint on line 3 hit count" {
		    $breakpoints[2].HitCount | Should Be 1
	    }
    }
    finally
    {
        if ($breakpoints -ne $null) { $breakpoints | remove-psbreakpoint }
        if (Test-Path $script)
        {
            del $script -Force
        }
    }


    Context "COM TESTS" {
        # DRT for 133807 SetBreakpointWithShortPath
        BeforeAll {
            if ( $IsCoreCLR ) { return } # no COM on core
            $scriptPath1 = Join-Path $TestDrive SBPShortPathBug133807.DRT.tmp.ps1
            $scriptPath1 = setup -f SBPShortPathBug133807.DRT.tmp.ps1 -content '
            1..3 |
            % { $_ } | sort-object |
            get-unique'
            $a = New-Object -ComObject Scripting.FileSystemObject
            $f = $a.GetFile($scriptPath1)
            $scriptPath2 = $f.ShortPath

            $breakpoints = sbp $scriptPath2 1,2,3 -action { continue }
            $null = & $scriptPath2
        }

        AfterAll {
            if ( $IsCoreCLR ) { return }
            if ($breakpoints -ne $null) { $breakpoints | Remove-PSBreakpoint }
        }

        It "Short path Breakpoint on line 1 hit count" -skip:$IsCoreCLR {
            $breakpoints[0].HitCount | Should Be 1
        }

        It "Short path Breakpoint on line 2 hit count" -skip:$IsCoreCLR {
            $breakpoints[1].HitCount | Should Be 3
        }

        It "Short path Breakpoint on line 3 hit count" -skip:$IsCoreCLR {
            $breakpoints[2].HitCount | Should Be 1
        }
    }
}


Describe "Unit tests for various script breakpoints" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>Unit tests for line breakpoints on dot-sourced files...</Summary>
    #  </Test>
    param($path = $null)

    if ($path -eq $null)
    {
        $path = split-path $MyInvocation.InvocationName
    }

    #
    # Verifies that the given command returns the expected set of breakpoints
    #
    function Verify([ScriptBlock] $command, [System.Management.Automation.Breakpoint[]] $expected)
    {
        $actual = @(& $command)

	    It "Script breakpoint count" {
		    $actual.Count | Should Be $expected.Count
	    }

        foreach ($breakpoint in $actual)
        {
	        It "Expected script breakpoints" {
		        ($expected -contains $breakpoint) | Should Be $true
	        }
        }
    }

    #
    # Verifies that the command fails with the given exception
    #
    function VerifyException([ScriptBlock] $command, [string] $exception)
    {
        try
        {
            & $command

	        It "Script should fail to verify exception" {
		        $false | Should Be $true
	        }
        }
        catch
        {
            $type = $_.Exception.GetType().Name

	        It "Script failed expected exception" {
		        $type | Should Be $exception
	        }
        }
    }

    #
    # Tests
    #
    try
    {
        #
        # Ensure there are no breakpoints at start of test
        #
        gbp | rbp

        #
        # Create a couple of scripts
        #
        $scriptFile1 = [io.path]::Combine([io.path]::GetTempPath(), "DebuggerScriptTests-Get-PsBreakpoint1.ps1")
        $scriptFile2 = [io.path]::Combine([io.path]::GetTempPath(), "DebuggerScriptTests-Get-PsBreakpoint2.ps1")

        write-output '' > $scriptFile1
        write-output '' > $scriptFile2

        #
        # Set several breakpoints of different types
        #
        $line1 = sbp $scriptFile1 1
        $line2 = sbp $scriptFile2 2

        $cmd1 = sbp -c command1 -s $scriptFile1
        $cmd2 = sbp -c command2 -s $scriptFile2
        $cmd3 = sbp -c command3

        $var1 = sbp -v variable1 -s $scriptFile1
        $var2 = sbp -v variable2 -s $scriptFile2
        $var3 = sbp -v variable3

        #
        # The default parameter set must return all breakpoints
        #
        Verify { get-psbreakpoint } $line1,$line2,$cmd1,$cmd2,$cmd3,$var1,$var2,$var3

        #
        # Query by ID
        #
        Verify { get-psbreakpoint -id $line1.ID,$cmd1.ID,$var1.ID } $line1,$cmd1,$var1 # -id
        Verify { get-psbreakpoint $line2.ID,$cmd2.ID,$var2.ID }     $line2,$cmd2,$var2 # positional
        Verify { $cmd3.ID,$var3.ID | get-psbreakpoint }             $cmd3,$var3        # value from pipeline

        VerifyException { get-psbreakpoint -id $null } "ParameterBindingValidationException"
        VerifyException { get-psbreakpoint -id $line1.ID -script $scriptFile1 } "ParameterBindingException"

        #
        # Query by Script
        #
        Verify { get-psbreakpoint -script $scriptFile1 } $line1,$cmd1,$var1 # -script
        Verify { get-psbreakpoint $scriptFile2 }         $line2,$cmd2,$var2 # positional
        Verify { $scriptFile2 | get-psbreakpoint }       $line2,$cmd2,$var2 # value from pipeline

        VerifyException { get-psbreakpoint -script $null } "ParameterBindingValidationException"
        VerifyException { get-psbreakpoint -script $scriptFile1,$null } "ParameterBindingValidationException"

        # Verify that relative paths are handled correctly
        $directoryName = [System.IO.Path]::GetDirectoryName($scriptFile1)
        $fileName = [System.IO.Path]::GetFileName($scriptFile1)

        pushd $directoryName
        Verify { get-psbreakpoint -script $fileName } $line1,$cmd1,$var1
        popd

        #
        # Query by Type
        #
        $commandType = [Microsoft.PowerShell.Commands.BreakpointType]"command"
        $variableType = [Microsoft.PowerShell.Commands.BreakpointType]"variable"

        Verify { get-psbreakpoint -type "line" }                      $line1,$line2     # -type
        Verify { get-psbreakpoint $commandType }                      $cmd1,$cmd2,$cmd3 # positional
        Verify { $variableType | get-psbreakpoint }                   $var1,$var2,$var3 # value from pipeline
        Verify { get-psbreakpoint -type "line" -script $scriptFile1 } @($line1)         # -script parameter

        VerifyException { get-psbreakpoint -type $null } "ParameterBindingValidationException"

        #
        # Query by Command
        #
        Verify { get-psbreakpoint -command "command1","command2" }                       $cmd1,$cmd2 # -command
        Verify { get-psbreakpoint -command "command1","command2" -script $scriptFile1 }  @($cmd1)    # -script parameter

        VerifyException { get-psbreakpoint -command $null } "ParameterBindingValidationException"

        #
        # Query by Variable
        #
        Verify { get-psbreakpoint -variable "variable1","variable2" }                       $var1,$var2 # -command
        Verify { get-psbreakpoint -variable "variable1","variable2" -script $scriptFile1 }  @($var1)    # -script parameter

        VerifyException { get-psbreakpoint -variable $null } "ParameterBindingValidationException"
    }
    finally
    {
        if ($line1 -ne $null) { $line1 | Remove-PSBreakpoint }
        if ($line2 -ne $null) { $line2 | Remove-PSBreakpoint }
        if ($cmd1 -ne $null) { $cmd1 | Remove-PSBreakpoint }
        if ($cmd2 -ne $null) { $cmd2 | Remove-PSBreakpoint }
        if ($cmd3 -ne $null) { $cmd3 | Remove-PSBreakpoint }
        if ($var1 -ne $null) { $var1 | Remove-PSBreakpoint }
        if ($var2 -ne $null) { $var2 | Remove-PSBreakpoint }
        if ($var3 -ne $null) { $var3 | Remove-PSBreakpoint }

        if (Test-Path $scriptFile1) { Remove-Item $scriptFile1 -Force }
        if (Test-Path $scriptFile2) { Remove-Item $scriptFile2 -Force }
    }
}


Describe "Unit tests for line breakpoints on dot-sourced files" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>PriorityOne</TestType>
    #    <Summary>Unit tests for line breakpoints on dot-sourced files...</Summary>
    #  </Test>
    #
    param($path = $null)

    if ($path -eq $null)
    {
        $path = split-path $MyInvocation.InvocationName
    }

    try
    {
        #
        # Create a test script
        #
        $scriptFile = [io.path]::Combine([io.path]::GetTempPath(), "DebuggerScriptTests-InMemoryBreakpoints.ps1")

        write-output '
        function Function1
        {
            write-host "In Function1" # line 4
        }

        function Function2
        {
            write-host "In Function2" # line 9
        }

        function Get-TestCmdlet
        {
            [CmdletBinding()]
            param()

            begin
            {
                write-host "In Get-TestCmdlet (begin)"
            }

            process
            {
                write-host "In Get-TestCmdlet (process)" # line 24
            }

            end
            {
                write-host "In Get-TestCmdlet (end)"
            }
        }
        ' > $scriptFile

        #
        # Set a couple of line breakpoints on the file, dot-source it and verify that the breakpoints are hit
        #
        $breakpoint1 = sbp $scriptFile 4 -action { continue; }
        $breakpoint2 = sbp $scriptFile 9 -action { continue; }
        $breakpoint3 = sbp $scriptFile 24 -action { continue; }

        . $scriptFile

        Function1
        Get-TestCmdlet

	    It "Breakpoint on function hit count" {
		    $breakpoint1.HitCount | Should Be 1
	    }

	    It "Breakpoint on uncalled function hit count" {
		    $breakpoint2.HitCount | Should Be 0
	    }

	    It "Breakpoint on cmdlet hit count" {
		    $breakpoint3.HitCount | Should Be 1
	    }
    }
    finally
    {
        if ($breakpoint1 -ne $null) { $breakpoint1 | Remove-PSBreakpoint }
        if ($breakpoint2 -ne $null) { $breakpoint2 | Remove-PSBreakpoint }
        if ($breakpoint3 -ne $null) { $breakpoint3 | Remove-PSBreakpoint }
        if (Test-Path $scriptFile) { Remove-Item $scriptFile -Force }
    }
}


Describe "Unit tests for line breakpoints on modules" -Tags "CI" {
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>Unit tests for line breakpoints on modules...</Summary>
    #  </Test>
    #
    $oldModulePath = $env:PSMODULEPATH
    try
    {
        #
        # Create a test module
        #
        $moduleName = "ModuleBreakpoints"
        $moduleRoot = [io.path]::GetTempPath();
        $moduleDirectory = [io.path]::Combine($moduleRoot, $moduleName)
        $moduleFile = [io.path]::Combine($moduleDirectory, $moduleName + ".psm1")

        mkdir $moduleDirectory 2> $null

        write-output '
        function ModuleFunction1
        {
            write-output "In ModuleFunction1" # line 4
        }

        function ModuleFunction2
        {
            write-output "In ModuleFunction2" # line 9
        }

        function Get-ModuleCmdlet
        {
            [CmdletBinding()]
            param()

            begin
            {
                write-output "In Get-ModuleCmdlet (begin)"
            }

            process
            {
                write-output "In Get-ModuleCmdlet (process)" # line 24
            }

            end
            {
                write-output "In Get-ModuleCmdlet (end)"
            }
        }

        export-modulemember ModuleFunction1
        export-modulemember ModuleFunction2
        export-modulemember Get-ModuleCmdlet
        ' > $moduleFile

        #
        # Load the module
        #
        $ENV:PSMODULEPATH = $moduleRoot

        import-module $moduleName

        #
        # Set a couple of line breakpoints on the module and verify that they are hit
        #
        $breakpoint1 = sbp $moduleFile 4 -action { continue }
        $breakpoint2 = sbp $moduleFile 9 -action { continue }
        $breakpoint3 = sbp $moduleFile 24 -Action { continue }
        $breakpoint4 = sbp $moduleFile 25 -Action { continue }

        ModuleFunction1

        Get-ModuleCmdlet

	    It "Breakpoint1 on module function hit count" {
		    $breakpoint1.HitCount | Should Be 1
	    }

	    It "Breakpoint2 on uncalled module function hit count" {
		    $breakpoint2.HitCount | Should Be 0
	    }

	    It "Breakpoint3 on module cmdlet hit count" {
		    $breakpoint3.HitCount | Should Be 1
	    }

	    It "Breakpoint4 on module cmdlet hit count" {
		    $breakpoint4.HitCount | Should Be 1
	    }
    }
    finally
    {
        $env:PSMODULEPATH = $oldModulePath
        if ($breakpoint1 -ne $null) { Remove-PSBreakpoint $breakpoint1 }
        if ($breakpoint2 -ne $null) { Remove-PSBreakpoint $breakpoint2 }
        if ($breakpoint3 -ne $null) { Remove-PSBreakpoint $breakpoint3 }
        if ($breakpoint4 -ne $null) { Remove-PSBreakpoint $breakpoint4 }
        get-module $moduleName | remove-module
        if (Test-Path $moduleDirectory) { Remove-Item $moduleDirectory -r -force -ea silentlycontinue }
    }
}


Describe "Sometimes line breakpoints are ignored" -Tags "CI" {
    #####################################################################################
    #
    #  <Test>
    #    <TestType>DRT</TestType>
    #    <Summary>
    #	   WinBlue: 177846 - Win8: Sometimes line breakpoints are ignored after the first
    #      time you use the 'c' debugger command.
    #    </Summary>
    #  </Test>
    #
    #####################################################################################

    $path = [io.path]::GetTempPath();
    $tempFileName1 = Join-Path -Path $path -ChildPath "TDBG47488F.ps1"
    $tempFileName2 = Join-Path -Path $path -ChildPath "TDBG88473F.ps1"

    try
    {
        @'
        while ($count -lt 5)
        {
            $count += 1
            "Hello $count"
        }
'@ > $tempFileName1

        @'
        do
        {
            $count2 += 1
            "Hello do $count2"
        }
        while ($count2 -lt 5)
'@ > $tempFileName2

        $bp1 = Set-PSBreakpoint -Script $tempFileName1 -Line 3 -Action {continue}
        & $tempFileName1

	    It "Breakpoint 1 hit count" {
		    $bp1.HitCount | Should Be 6
	    }

        $bp2 = Set-PSBreakpoint -Script $tempFileName2 -Line 3 -Action {continue}
        & $tempFileName2

	    It "Breakpoint 2 hit count" {
		    $bp2.HitCount | Should Be 6
	    }
    }
    finally
    {
        if ($bp1 -ne $null) { Remove-PSBreakpoint $bp1 }
        if ($bp2 -ne $null) { Remove-PSBreakpoint $bp2 }

        if (Test-Path -Path $tempFileName1) { Remove-Item $tempFileName1 -force }
        if (Test-Path -Path $tempFileName2) { Remove-Item $tempFileName2 -force }
    }
}
