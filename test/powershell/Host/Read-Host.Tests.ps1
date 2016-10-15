Describe "Read-Host" -Tags "Slow","Feature" {
    Context "[Console]::ReadKey() implementation on non-Windows" {
        BeforeAll {
            $powershell = Join-Path -Path $PsHome -ChildPath "powershell"
            $assetsDir = Join-Path -Path $PSScriptRoot -ChildPath assets
            if ($IsWindows) {
                $ItArgs = @{ skip = $true }
            } elseif (-not (Get-Command expect -ErrorAction Ignore)) {
                $ItArgs = @{ pending = $true }
            } else {
                $ItArgs = @{ }
            }
        }

        It @ItArgs "Should output correctly" {
            & (Join-Path $assetsDir "Read-Host.Output.expect") $powershell | Out-Null
            $LASTEXITCODE | Should Be 0
        }
    }
}
