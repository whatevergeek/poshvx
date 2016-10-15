$powershellexe = (get-process -id $PID).mainmodule.filename

Describe "Clone array" -Tags "CI" {
    It "Cast in target expr" {
        (([int[]](42)).clone()) | Should Be 42
        (([int[]](1..5)).clone()).Length | Should Be 5
        (([int[]](1..5)).clone()).GetType() | Should Be ([int[]])

    }
    It "Cast not in target expr" {
        $e = [int[]](42)
        $e.Clone() | Should Be 42
        $e = [int[]](1..5)
        $e.Clone().Length | Should Be 5
        $e.Clone().GetType() | Should Be ([int[]])
    }
}

Describe "Set fields through PSMemberInfo" -Tags "CI" {
    Add-Type @"
    public struct AStruct { public string s; }
"@

    It "via cast" {
        ([AStruct]@{s = "abc" }).s | Should Be "abc"
    }
    It "via new-object" {
        (new-object AStruct -prop @{s="abc"}).s | Should Be "abc"
    }
    It "via PSObject" {
        $x = [AStruct]::new()
        $x.psobject.properties['s'].Value = 'abc'
        $x.s | Should Be "abc"
    }
}

Describe "MSFT:3309783" -Tags "CI" {

    It "Run in another process" {
        # For a reliable test, we must run this in a new process because an earlier binding in this process
        # could mask the bug/fix.
        & $powershellexe -noprofile -command "[psobject] | % FullName" | Should Be System.Management.Automation.PSObject
    }

    It "Run in current process" {
        # For good measure, do the same thing in this process
        [psobject] | % FullName | Should Be System.Management.Automation.PSObject
    }

    It "Pipe objects derived from PSObject" {
        # Related - make sure we can still pipe objects derived from PSObject
        class MyPsObj : PSObject
        {
            MyPsObj($obj) : base($obj) { }
            [string] ToString() {
                # Don't change access via .psobject, that was also a bug.
                return "MyObj: " + $this.psobject.BaseObject
            }
        }

        [MyPsObj]::new("abc").psobject.ToString() | Should Be "MyObj: abc"
        [MyPsObj]::new("def") | Out-String | % Trim | Should Be "MyObj: def"
    }
}

Describe "ScriptBlockAst.GetScriptBlock throws on error" -Tags "CI" {

    $e = $null

    It "with parse error" {
        $ast = [System.Management.Automation.Language.Parser]::ParseInput('function ', [ref]$null, [ref]$e)
        { $ast.GetScriptBlock() } | Should Throw
    }


    It "with semantic errors" {
        $ast = [System.Management.Automation.Language.Parser]::ParseInput('function foo{param()begin{}end{[ref][ref]1}dynamicparam{}}', [ref]$null, [ref]$e)

        { $ast.GetScriptBlock() } | Should Throw
        { $ast.EndBlock.Statements[0].Body.GetScriptBlock() } | Should Throw
    }
}

Describe "Hashtable key property syntax" -Tags "CI" {
    $script = @'
    # First create a hashtable wrapped in PSObject
    $hash = New-Object hashtable
    $key = [ConsoleColor]::Red
    $null = $hash.$key
    $hash = @{}
    $hash.$key = 'Hello'
    # works in PS 2,3,4. Fails in PS 5:
    $hash.$key
'@

    It "In current process" {
        # Run in current process, but something that ran earlier could influence
        # the result
        Invoke-Expression $script | Should Be Hello
    }

    It "In different process" {
        # So also run in a fresh process
        $bytes = [System.Text.Encoding]::Unicode.GetBytes($script)
        & $powershellexe -noprofile -encodedCommand ([Convert]::ToBase64String($bytes)) | Should Be Hello
    }
}

Describe "Assign automatic variables" -Tags "CI" {
    
    $autos = '_', 'args', 'this', 'input', 'pscmdlet', 'psboundparameters', 'myinvocation', 'psscriptroot', 'pscommandpath'

    foreach ($auto in $autos)
    {
        It "Assign auto w/ invalid type constraint - $auto" {
            { & ([ScriptBlock]::Create("[datetime]`$$auto = 1")) } | Should Throw $auto
            { . ([ScriptBlock]::Create("[datetime]`$$auto = 1")) } | Should Throw $auto
            { & ([ScriptBlock]::Create("[runspace]`$$auto = 1")) } | Should Throw $auto
            { . ([ScriptBlock]::Create("[runspace]`$$auto = 1")) } | Should Throw $auto
            { & ([ScriptBlock]::Create("[notexist]`$$auto = 1")) } | Should Throw $auto
            { . ([ScriptBlock]::Create("[notexist]`$$auto = 1")) } | Should Throw $auto
        }
    }

    foreach ($auto in $autos)
    {
        It "Assign auto w/o type constraint - $auto" {
            & ([ScriptBlock]::Create("`$$auto = 1; `$$auto")) | Should Be 1
            . ([ScriptBlock]::Create("`$$auto = 1; `$$auto")) | Should Be 1
        }
    }

    It "Assign auto w/ correct type constraint" {
      & { [object]$_ = 1; $_ } | Should Be 1
      & { [object[]]$args = 1; $args } | Should Be 1
      & { [object]$this = 1; $this } | Should Be 1
      & { [object]$input = 1; $input } | Should Be 1
      # Can't test PSCmdlet or PSBoundParameters, they use an internal type
      & { [System.Management.Automation.InvocationInfo]$myInvocation = $myInvocation; $myInvocation.Line } | Should Match Automation.InvocationInfo
      & { [string]$PSScriptRoot = 'abc'; $PSScriptRoot } | Should Be abc
      & { [string]$PSCommandPath = 'abc'; $PSCommandPath } | Should Be abc
    }
}

Describe "Attribute error position" -Tags "CI" {
    It "Ambiguous overloads" {
        try
        {
            & {
                param(
                    [ValidateNotNull(1,2,3,4)]
                    $param
                )
            }
            throw "Should have thrown"
        }
        catch
        {
            $_.InvocationInfo.Line | Should Match ValidateNotNull
            $_.FullyQualifiedErrorId | Should Be MethodCountCouldNotFindBest
        }
    }
}

Describe "Multiple alias attributes" -Tags "CI" {
    It "basic test" {
        function foo {
            param(
                [alias('aa')]
                [alias('bb')]
                $cc
            )
            $cc
        }

        foo -aa 1 | Should Be 1
        foo -bb 2 | Should Be 2
        foo -cc 3 | Should Be 3
    }
}

Describe "Members of System.Type" -Tags "CI" {
    It "Members in public classes derived from System.Type should be found" {
        class MyType : System.Collections.IEnumerable
        {
            [System.Collections.IEnumerator] GetEnumerator() { return $null }
        }

        [type] | Get-Member ImplementedInterfaces | Should Be 'System.Collections.Generic.IEnumerable[type] ImplementedInterfaces {get;}'
        [MyType].ImplementedInterfaces | Should Be System.Collections.IEnumerable
    }
}
