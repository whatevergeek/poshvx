# Ported from MultiMachine Tests 
# Tests\Engine\HelpSystem\Pester.Engine.HelpSystem.BugFix.Tests.ps1
# Tests\Commands\Cmdlets\Microsoft.PowerShell.Security\Pester.Command.Cmdlets.Security.Tests.ps1

# Execution policy currently does not work outside of windows
if(-not $IsWindows)
{
    return
}

Describe "Help work with ExecutionPolicy Restricted " -Tags "Feature" {

    # Validate that 'Get-Help Get-Disk' returns one result when the execution policy is 'Restricted' on Nano
    # From an internal bug - [Regression] Get-Help returns multiple matches when there is an exact match

    # Skip the test if Storage module is not available, return a pesudo result
    # ExecutionPoliy only works on windows
    It "Test for Get-Help Get-Disk" -skip:(!(Test-Path (Join-Path -Path $PSHOME -ChildPath Modules\Storage\Storage.psd1)) -or -not $IsWindows) {

            try
            {
                $currentExecutionPolicy = Get-ExecutionPolicy
                Get-Module -Name Storage | Remove-Module -Force -ErrorAction Stop

                # 'Get-Help Get-Disk' should return one result back
                Set-ExecutionPolicy -ExecutionPolicy Restricted -Force -ErrorAction Stop
                (Get-Help -Name Get-Disk -ErrorAction Stop).Name | Should Be 'Get-Disk'
            }
            catch {
                $_.ToString | should be null
            }
            finally
            {
                Set-ExecutionPolicy $currentExecutionPolicy -Force
            }
    }
}

Describe "Validate ExecutionPolicy cmdlets in PowerShell" -Tags "CI" {

    BeforeAll {


        #Generate test data
        $drive = 'TestDrive:\'
        $testDirectory =  Join-Path $drive ("MultiMachineTestData\Commands\Cmdlets\Security_TestData\ExecutionPolicyTestData")
        if(Test-Path $testDirectory)
        {
            Remove-Item -Force -Recurse $testDirectory -ea SilentlyContinue
        }
        $null = New-Item $testDirectory -ItemType Directory -Force
        $remoteTestDirectory = $testDirectory

        $InternetSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath InternetSignatureCorruptedScript.ps1        
        $InternetSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath InternetSignedScript.ps1
        $InternetUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath InternetUnsignedScript.ps1
        $IntranetSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath IntranetSignatureCorruptedScript.ps1
        $IntranetSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath IntranetSignedScript.ps1       
        $IntranetUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath IntranetUnsignedScript.ps1
        $LocalSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath LocalSignatureCorruptedScript.ps1
        $LocalSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath LocalSignedScript.ps1
        $LocalUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath LocalUnsignedScript.ps1
        $TrustedSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath TrustedSignatureCorruptedScript.ps1
        $TrustedSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath TrustedSignedScript.ps1
        $TrustedUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath TrustedUnsignedScript.ps1
        $UntrustedSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath UntrustedSignatureCorruptedScript.ps1       
        $UntrustedSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath UntrustedSignedScript.ps1
        $UntrustedUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath UntrustedUnsignedScript.ps1
        $MyComputerSignatureCorruptedScript = Join-Path -Path $remoteTestDirectory -ChildPath MyComputerSignatureCorruptedScript.ps1
        $MyComputerSignedScript = Join-Path -Path $remoteTestDirectory -ChildPath MyComputerSignedScript.ps1
        $MyComputerUnsignedScript = Join-Path -Path $remoteTestDirectory -ChildPath MyComputerUnsignedScript.ps1

        $fileType = @{
            "Local" = -1
            "MyComputer" = 0
            "Intranet" = 1
            "Trusted" = 2
            "Internet" = 3
            "Untrusted" = 4
        }

        $testFilesInfo = @(
            @{
                FilePath = $InternetSignatureCorruptedScript
                FileType = $fileType.Internet
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $InternetSignedScript
                FileType = $fileType.Internet
                AddSignature = $true
                Corrupted = $false
            }
            @{
                FilePath = $InternetUnsignedScript
                FileType = $fileType.Internet
                AddSignature = $false
                Corrupted = $false
            }
            @{
                FilePath = $IntranetSignatureCorruptedScript
                FileType = $fileType.Intranet
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $IntranetSignedScript
                FileType = $fileType.Intranet
                AddSignature = $true
                Corrupted = $false
            }
            @{
                FilePath = $IntranetUnsignedScript
                FileType = $fileType.Intranet
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $LocalSignatureCorruptedScript
                FileType = $fileType.Local
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $LocalSignedScript
                FileType = $fileType.Local
                AddSignature = $true
                Corrupted = $false
            }
            @{
                FilePath = $LocalUnsignedScript
                FileType = $fileType.Local
                AddSignature = $false
                Corrupted = $false
            }
            @{
                FilePath = $TrustedSignatureCorruptedScript
                FileType = $fileType.Trusted
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $TrustedSignedScript
                FileType = $fileType.Trusted
                AddSignature = $true
                Corrupted = $false
            }
            @{
                FilePath = $TrustedUnsignedScript
                FileType = $fileType.Trusted
                AddSignature = $false
                Corrupted = $false
            }
             @{
                FilePath = $UntrustedSignatureCorruptedScript
                FileType = $fileType.Untrusted
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $UntrustedSignedScript
                FileType = $fileType.Untrusted
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $UntrustedUnsignedScript
                FileType = $fileType.Untrusted
                AddSignature = $true
                Corrupted = $false
            }
             @{
                FilePath = $MyComputerSignatureCorruptedScript
                FileType = $fileType.MyComputer
                AddSignature = $true
                Corrupted = $true
            }
            @{
                FilePath = $MyComputerSignedScript
                FileType = $fileType.MyComputer
                AddSignature = $true
                Corrupted = $false
            }
            @{
                FilePath = $MyComputerUnsignedScript
                FileType = $fileType.MyComputer
                AddSignature = $false
                Corrupted = $false
            }
        )

        #Generate Test Data on remote machine and get the execution policy
            
            function createTestFile
            {
                param (
                [string]
                $FilePath,

                [int]
                $FileType,

                [switch]
                $AddSignature,

                [switch]
                $Corrupted
                )
                     
                $null = New-Item -Path $filePath -ItemType File

                $content = "`"Hello`"" + "`r`n" 
                if($AddSignature)
                {
                    if($Corrupted)
                    {
                        # Add corrupted signature
                        $content += @"
# SIG # Begin signature block
# MIIPTAYJKoZIhvcNAQcCoIIPPTCCDzkCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUYkdwUPVVR4frPbdbTE8ZPwfD
# +XegggyDMIIGFTCCA/2gAwIBAgITMwAAABrJQBS8Ii1KJQAAAAAAGjANBgkqhkiG
# 9w0BAQsFADCBkDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAO
# BgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjE6
# MDgGA1UEAxMxTWljcm9zb2Z0IFRlc3RpbmcgUm9vdCBDZXJ0aWZpY2F0ZSBBdXRo
# b3JpdHkgMjAxMDAeFw0xNDAyMDQxODAyMjVaFw0xODAyMDQxODAyMjVaMIGBMRMw
# EQYKCZImiZPyLGQBGRYDY29tMRkwFwYKCZImiZPyLGQBGRYJbWljcm9zb2Z0MRQw
# EgYKCZImiZPyLGQBGRYEY29ycDEXMBUGCgmSJomT8ixkARkWB3JlZG1vbmQxIDAe
# BgNVBAMTF01TSVQgVGVzdCBDb2RlU2lnbiBDQSAzMIIBIjANBgkqhkiG9w0BAQEF
# AAOCAQ8AMIIBCgKCAQEAuV1NahtVcKSQ6osSVsCcXSsk5finBZfPTbq39nQiX9L0
# PY+5Zi73qGhDv3m+exmvWoYTgI2AQZ48lQtohf4QV0THWjsvvP/r12WZSlOfUGi5
# 5639OAmXiAPpFwPffubajzyIcYBDthJonBlhRsGCWoSaZRBZnp/39tDDvHvQqb+i
# w94CDTFfjcQ/K6xtSCNH1IaKQd6TP2mVdtbYBHIfuLWWO/quLuVgKKxz9sHjONVx
# 9nEcWwatIPiz5J9TsR/bbDxzF5AH9U8jm++ZNECu2zYPhqNj9t3HKYOrUNIEi/b9
# xYlQfMw85hPkMBTJWieyufXHkhzouvTzI3E+VhJ8EwIDAQABo4IBczCCAW8wEgYJ
# KwYBBAGCNxUBBAUCAwEAATAjBgkrBgEEAYI3FQIEFgQUxeHTk4FfDvbJdORSZob2
# 57rUxG4wHQYDVR0OBBYEFLU0zfVssWSEb3tmjxXucfADs2jrMBkGCSsGAQQBgjcU
# AgQMHgoAUwB1AGIAQwBBMAsGA1UdDwQEAwIBhjASBgNVHRMBAf8ECDAGAQH/AgEA
# MB8GA1UdIwQYMBaAFKMBBH4wiDPruTGcyuuFdmf8ZbTRMFkGA1UdHwRSMFAwTqBM
# oEqGSGh0dHA6Ly9jcmwubWljcm9zb2Z0LmNvbS9wa2kvY3JsL3Byb2R1Y3RzL01p
# Y1Rlc1Jvb0NlckF1dF8yMDEwLTA2LTE3LmNybDBdBggrBgEFBQcBAQRRME8wTQYI
# KwYBBQUHMAKGQWh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2kvY2VydHMvTWlj
# VGVzUm9vQ2VyQXV0XzIwMTAtMDYtMTcuY3J0MA0GCSqGSIb3DQEBCwUAA4ICAQBt
# 9EVv44wAgXhIItfRrX2LjyEyig6DkExisf3j/RNwa3BLNK5PlfNjU/0H58V1k/Dy
# S3CIzLhvn+PBCrpjWr5R1blkJbKQUdP/ZNz28QOXd0l+Ha3P6Mne1NNfXDAjkRHK
# SqzndTxJT7s/03jYcCfh3JyiXzT8Dt5GXlWIr1wJfQljhzon3w9sptb5sIJTjB9Z
# 0VWITkvAc2hVjFkpPPWkODXIYXYIRBxKjakXr7fEx3//ECQYcQrKBvUrLirEsI0g
# mxQ2QO30iQMxug5l4VYSuHhjaN6t86OjyUySGeImiLLKpVZt1uXIggpepSS9b6Pt
# cxqD0+L532oYNJMlT/Y04PGtyfKIVFMGYTmlHoHUU78BNrpGj6C/s+qyzwXpKDHI
# eQ2RozXUzt4SS8W1E3YVxWU2AWnP0BdS7PSB9BvVCkIf1bfuM6s88iSGFh0qaZyG
# sGDlU8s7YkS2i32+nTr5NJAH/v7yd6E7DQYZULBKdKfQDXuY+6s8kjg2OduGchge
# aZZh2NLh2V5OgVrXx7CzM0K6TMZNJRhgaHE7dzT3EC2uZ6ZT/SIwxwfKXYDjsPxx
# R4C9qkdnSDVCPncGAHhyR75i3fGJ28FHhd7mtePU+zbPJ/JGyADOdPDWgJFulg97
# 809qAfXmu6I7+ObsqlCMl8hbpctmWSqqpd8wZ36ntTCCBmYwggVOoAMCAQICE0MD
# Bi6W0bK7qmSfpQAAAQMGLpYwDQYJKoZIhvcNAQELBQAwgYExEzARBgoJkiaJk/Is
# ZAEZFgNjb20xGTAXBgoJkiaJk/IsZAEZFgltaWNyb3NvZnQxFDASBgoJkiaJk/Is
# ZAEZFgRjb3JwMRcwFQYKCZImiZPyLGQBGRYHcmVkbW9uZDEgMB4GA1UEAxMXTVNJ
# VCBUZXN0IENvZGVTaWduIENBIDMwHhcNMTQxMjIyMTk0MzQ3WhcNMTYxMjIxMTk0
# MzQ3WjCBhDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNV
# BAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEuMCwG
# A1UEAxMlTWljcm9zb2Z0IENvcnBvcmF0aW9uIDNyZCBwYXJ0eSBXUCBXUzCCASIw
# DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAL4ofcc4uy3h6Ai2Bh8guql21/+u
# LMLhEeHbz5STKqMoxXqy8i3uRcK/oo57INq3H+cQ4yqvuUrPwi3wQE9OG7wO4ymc
# 4M/3WTNVfjdOx0FK2y6UuKZpWQlwycuELbONrvXTzdtGuM0aiGbELRJFOq+742I+
# G3x3otZrTSXC1m6aOoKb50rSqUJ0ENb1PMJV9GBTXnRDde7ub7W3jp9Dj0HxFnof
# QRZSWfCDrO1l1hle7zPBuTnLfCXbma0oRHlTz3m3yEGlUQscxYu6BI+aJkKDKa5R
# L2PCPnau3WuUMFsmQZk6pFrACxIvq+OZTLsorTsZUooCL/5V1ofaHahnJ68CAwEA
# AaOCAtAwggLMMD0GCSsGAQQBgjcVBwQwMC4GJisGAQQBgjcVCIPPiU2t8gKFoZ8M
# gvrKfYHh+3SBT4PGhWmH7vANAgFkAgErMAsGA1UdDwQEAwIHgDA4BgkrBgEEAYI3
# FQoEKzApMA0GCysGAQQBgjdMBYIsMAwGCisGAQQBgjdMAwEwCgYIKwYBBQUHAwMw
# LAYDVR0lBCUwIwYLKwYBBAGCN0wFgiwGCisGAQQBgjdMAwEGCCsGAQUFBwMDMB0G
# A1UdDgQWBBT+6HzYZdp8xPv1xylrDwOMuYQkvDAwBgNVHREEKTAnoCUGCisGAQQB
# gjcUAgOgFwwVZG9uZ2Jvd0BtaWNyb3NvZnQuY29tMB8GA1UdIwQYMBaAFLU0zfVs
# sWSEb3tmjxXucfADs2jrMIHxBgNVHR8EgekwgeYwgeOggeCggd2GOWh0dHA6Ly9j
# b3JwcGtpL2NybC9NU0lUJTIwVGVzdCUyMENvZGVTaWduJTIwQ0ElMjAzKDEpLmNy
# bIZQaHR0cDovL21zY3JsLm1pY3Jvc29mdC5jb20vcGtpL21zY29ycC9jcmwvTVNJ
# VCUyMFRlc3QlMjBDb2RlU2lnbiUyMENBJTIwMygxKS5jcmyGTmh0dHA6Ly9jcmwu
# bWljcm9zb2Z0LmNvbS9wa2kvbXNjb3JwL2NybC9NU0lUJTIwVGVzdCUyMENvZGVT
# aWduJTIwQ0ElMjAzKDEpLmNybDCBrwYIKwYBBQUHAQEEgaIwgZ8wRQYIKwYBBQUH
# MAKGOWh0dHA6Ly9jb3JwcGtpL2FpYS9NU0lUJTIwVGVzdCUyMENvZGVTaWduJTIw
# Q0ElMjAzKDEpLmNydDBWBggrBgEFBQcwAoZKaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraS9tc2NvcnAvTVNJVCUyMFRlc3QlMjBDb2RlU2lnbiUyMENBJTIwMygx
# KS5jcnQwDQYJKoZIhvcNAQELBQADggEBAFRprvk5BxGyn5On1ICDyKRw9rLqyMET
# IDuBmX/enKuLRmETJSF7Dvzo/XbSXm+FTbGwnp5TOIPtCAeT0NuUAAjdo2iRT2Xr
# wc/B4x2dWMJmFG86WmPPWByfw1gFSep1xN6vA9qPb2VAXTmz8Ta75vSmCEfRAqOC
# 7U4uv3RBWImDx+7tI71XLKBmn1s1TTs1rL+43MsNMA7YNeM8/G0k2KbcNeLONNMG
# wJwtlu9CutONhULkhi2C3T7huDtNZgg+LnTbNvZeXMhHtfx8obh1fmgfOrdLUgE9
# 1YtW0F6mZ7OsdWPGV1wPOdRuNxgzGWvOIYCUTeeTU7b+Cifz/mTf/9QxggIzMIIC
# LwIBATCBmTCBgTETMBEGCgmSJomT8ixkARkWA2NvbTEZMBcGCgmSJomT8ixkARkW
# CW1pY3Jvc29mdDEUMBIGCgmSJomT8ixkARkWBGNvcnAxFzAVBgoJkiaJk/IsZAEZ
# FgdyZWRtb25kMSAwHgYDVQQDExdNU0lUIFRlc3QgQ29kZVNpZ24gQ0EgMwITQwMG
# LpbRsruqZJ+lAAABAwYuljAJBgUrDgMCGgUAoHAwEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEOMAwGCisG
# AQQBgjcCARUwIwYJKoZIhvcNAQkEMRYEFDFRa0VJKJQ1h2LG6dYzXKpBneOfMA0G
# CSqGSIb3DQEBAQUABIIBAHbWmEOWfj37SNw8NDnAAg7bl0L3oyGVKPWysRnriHC9
# aYImucAy2QXKo6YUWxHMqFvRPFrF07qkTDV249iC+L8gb1X0wwq/YuWWFbdN2J8s
# 4CnN6I4Ff2AF4Co34MZGhtIHd3D7H1oPMelTlHQOc5CXyB/wkduoNgS0GCoeZXSK
# DdMuN7dbru3PvCxe0ShzRwxBOa4EWZ6dHDAQRdrxkK2vVLWHg+6th8lRNnCJQeb+
# 03tMRItnm/sAmKR9PCWm4YZob3ug9T9Qa1K00TuNskjXO+G2S2mjhFC5+HGKjLZd
# bJydl0MIIMBtlLEGa4CcFtszxaww5Cx+YtCbxPp3iII=
# SIG # End signature block
"@
                    }
                    else
                    {
                        # Add correct signature
                        $content += @"
# SIG # Begin signature block
# MIIPTAYJKoZIhvcNAQcCoIIPPTCCDzkCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUYkdwUPVVR4frPbdbTE8ZPwfD
# +XegggyDMIIGFTCCA/2gAwIBAgITMwAAABrJQBS8Ii1KJQAAAAAAGjANBgkqhkiG
# 9w0BAQsFADCBkDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAO
# BgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjE6
# MDgGA1UEAxMxTWljcm9zb2Z0IFRlc3RpbmcgUm9vdCBDZXJ0aWZpY2F0ZSBBdXRo
# b3JpdHkgMjAxMDAeFw0xNDAyMDQxODAyMjVaFw0xODAyMDQxODAyMjVaMIGBMRMw
# EQYKCZImiZPyLGQBGRYDY29tMRkwFwYKCZImiZPyLGQBGRYJbWljcm9zb2Z0MRQw
# EgYKCZImiZPyLGQBGRYEY29ycDEXMBUGCgmSJomT8ixkARkWB3JlZG1vbmQxIDAe
# BgNVBAMTF01TSVQgVGVzdCBDb2RlU2lnbiBDQSAzMIIBIjANBgkqhkiG9w0BAQEF
# AAOCAQ8AMIIBCgKCAQEAuV1NahtVcKSQ6osSVsCcXSsk5finBZfPTbq39nQiX9L0
# PY+5Zi73qGhDv3m+exmvWoYTgI2AQZ48lQtohf4QV0THWjsvvP/r12WZSlOfUGi5
# 5639OAmXiAPpFwPffubajzyIcYBDthJonBlhRsGCWoSaZRBZnp/39tDDvHvQqb+i
# w94CDTFfjcQ/K6xtSCNH1IaKQd6TP2mVdtbYBHIfuLWWO/quLuVgKKxz9sHjONVx
# 9nEcWwatIPiz5J9TsR/bbDxzF5AH9U8jm++ZNECu2zYPhqNj9t3HKYOrUNIEi/b9
# xYlQfMw85hPkMBTJWieyufXHkhzouvTzI3E+VhJ8EwIDAQABo4IBczCCAW8wEgYJ
# KwYBBAGCNxUBBAUCAwEAATAjBgkrBgEEAYI3FQIEFgQUxeHTk4FfDvbJdORSZob2
# 57rUxG4wHQYDVR0OBBYEFLU0zfVssWSEb3tmjxXucfADs2jrMBkGCSsGAQQBgjcU
# AgQMHgoAUwB1AGIAQwBBMAsGA1UdDwQEAwIBhjASBgNVHRMBAf8ECDAGAQH/AgEA
# MB8GA1UdIwQYMBaAFKMBBH4wiDPruTGcyuuFdmf8ZbTRMFkGA1UdHwRSMFAwTqBM
# oEqGSGh0dHA6Ly9jcmwubWljcm9zb2Z0LmNvbS9wa2kvY3JsL3Byb2R1Y3RzL01p
# Y1Rlc1Jvb0NlckF1dF8yMDEwLTA2LTE3LmNybDBdBggrBgEFBQcBAQRRME8wTQYI
# KwYBBQUHMAKGQWh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2kvY2VydHMvTWlj
# VGVzUm9vQ2VyQXV0XzIwMTAtMDYtMTcuY3J0MA0GCSqGSIb3DQEBCwUAA4ICAQBt
# 9EVv44wAgXhIItfRrX2LjyEyig6DkExisf3j/RNwa3BLNK5PlfNjU/0H58V1k/Dy
# S3CIzLhvn+PBCrpjWr5R1blkJbKQUdP/ZNz28QOXd0l+Ha3P6Mne1NNfXDAjkRHK
# SqzndTxJT7s/03jYcCfh3JyiXzT8Dt5GXlWIr1wJfQljhzon3w9sptb5sIJTjB9Z
# 0VWITkvAc2hVjFkpPPWkODXIYXYIRBxKjakXr7fEx3//ECQYcQrKBvUrLirEsI0g
# mxQ2QO30iQMxug5l4VYSuHhjaN6t86OjyUySGeImiLLKpVZt1uXIggpepSS9b6Pt
# cxqD0+L532oYNJMlT/Y04PGtyfKIVFMGYTmlHoHUU78BNrpGj6C/s+qyzwXpKDHI
# eQ2RozXUzt4SS8W1E3YVxWU2AWnP0BdS7PSB9BvVCkIf1bfuM6s88iSGFh0qaZyG
# sGDlU8s7YkS2i32+nTr5NJAH/v7yd6E7DQYZULBKdKfQDXuY+6s8kjg2OduGchge
# aZZh2NLh2V5OgVrXx7CzM0K6TMZNJRhgaHE7dzT3EC2uZ6ZT/SIwxwfKXYDjsPxx
# R4C9qkdnSDVCPncGAHhyR75i3fGJ28FHhd7mtePU+zbPJ/JGyADOdPDWgJFulg97
# 809qAfXmu6I7+ObsqlCMl8hbpctmWSqqpd8wZ36ntTCCBmYwggVOoAMCAQICE0MD
# Bi6W0bK7qmSfpQAAAQMGLpYwDQYJKoZIhvcNAQELBQAwgYExEzARBgoJkiaJk/Is
# ZAEZFgNjb20xGTAXBgoJkiaJk/IsZAEZFgltaWNyb3NvZnQxFDASBgoJkiaJk/Is
# ZAEZFgRjb3JwMRcwFQYKCZImiZPyLGQBGRYHcmVkbW9uZDEgMB4GA1UEAxMXTVNJ
# VCBUZXN0IENvZGVTaWduIENBIDMwHhcNMTQxMjIyMTk0MzQ3WhcNMTYxMjIxMTk0
# MzQ3WjCBhDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNV
# BAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEuMCwG
# A1UEAxMlTWljcm9zb2Z0IENvcnBvcmF0aW9uIDNyZCBwYXJ0eSBXUCBXUzCCASIw
# DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAL4ofcc4uy3h6Ai2Bh8guql21/+u
# LMLhEeHbz5STKqMoxXqy8i3uRcK/oo57INq3H+cQ4yqvuUrPwi3wQE9OG7wO4ymc
# 4M/3WTNVfjdOx0FK2y6UuKZpWQlwycuELbONrvXTzdtGuM0aiGbELRJFOq+742I+
# G3x3otZrTSXC1m6aOoKb50rSqUJ0ENb1PMJV9GBTXnRDde7ub7W3jp9Dj0HxFnof
# QRZSWfCDrO1l1hle7zPBuTnLfCXbma0oRHlTz3m3yEGlUQscxYu6BI+aJkKDKa5R
# L2PCPnau3WuUMFsmQZk6pFrACxIvq+OZTLsorTsZUooCL/5V1ofaHahnJ68CAwEA
# AaOCAtAwggLMMD0GCSsGAQQBgjcVBwQwMC4GJisGAQQBgjcVCIPPiU2t8gKFoZ8M
# gvrKfYHh+3SBT4PGhWmH7vANAgFkAgErMAsGA1UdDwQEAwIHgDA4BgkrBgEEAYI3
# FQoEKzApMA0GCysGAQQBgjdMBYIsMAwGCisGAQQBgjdMAwEwCgYIKwYBBQUHAwMw
# LAYDVR0lBCUwIwYLKwYBBAGCN0wFgiwGCisGAQQBgjdMAwEGCCsGAQUFBwMDMB0G
# A1UdDgQWBBT+6HzYZdp8xPv1xylrDwOMuYQkvDAwBgNVHREEKTAnoCUGCisGAQQB
# gjcUAgOgFwwVZG9uZ2Jvd0BtaWNyb3NvZnQuY29tMB8GA1UdIwQYMBaAFLU0zfVs
# sWSEb3tmjxXucfADs2jrMIHxBgNVHR8EgekwgeYwgeOggeCggd2GOWh0dHA6Ly9j
# b3JwcGtpL2NybC9NU0lUJTIwVGVzdCUyMENvZGVTaWduJTIwQ0ElMjAzKDEpLmNy
# bIZQaHR0cDovL21zY3JsLm1pY3Jvc29mdC5jb20vcGtpL21zY29ycC9jcmwvTVNJ
# VCUyMFRlc3QlMjBDb2RlU2lnbiUyMENBJTIwMygxKS5jcmyGTmh0dHA6Ly9jcmwu
# bWljcm9zb2Z0LmNvbS9wa2kvbXNjb3JwL2NybC9NU0lUJTIwVGVzdCUyMENvZGVT
# aWduJTIwQ0ElMjAzKDEpLmNybDCBrwYIKwYBBQUHAQEEgaIwgZ8wRQYIKwYBBQUH
# MAKGOWh0dHA6Ly9jb3JwcGtpL2FpYS9NU0lUJTIwVGVzdCUyMENvZGVTaWduJTIw
# Q0ElMjAzKDEpLmNydDBWBggrBgEFBQcwAoZKaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraS9tc2NvcnAvTVNJVCUyMFRlc3QlMjBDb2RlU2lnbiUyMENBJTIwMygx
# KS5jcnQwDQYJKoZIhvcNAQELBQADggEBAFRprvk5BxGyn5On1ICDyKRw9rLqyMET
# IDuBmX/enKuLRmETJSF7Dvzo/XbSXm+FTbGwnp5TOIPtCAeT0NuUAAjdo2iRT2Xr
# wc/B4x2dWMJmFG86WmPPWByfw1gFSep1xN6vA9qPb2VAXTmz8Ta75vSmCEfRAqOC
# 7U4uv3RBWImDx+7tI71XLKBmn1s1TTs1rL+43MsNMA7YNeM8/G0k2KbcNeLONNMG
# wJwtlu9CutONhULkhi2C3T7huDtNZgg+LnTbNvZeXMhHtfx8obh1fmgfOrdLUgE9
# 1YtW0F6mZ7OsdWPGV1wPOdRuNxgzGWvOIYCUTeeTU7b+Cifz/mTf/9QxggIzMIIC
# LwIBATCBmTCBgTETMBEGCgmSJomT8ixkARkWA2NvbTEZMBcGCgmSJomT8ixkARkW
# CW1pY3Jvc29mdDEUMBIGCgmSJomT8ixkARkWBGNvcnAxFzAVBgoJkiaJk/IsZAEZ
# FgdyZWRtb25kMSAwHgYDVQQDExdNU0lUIFRlc3QgQ29kZVNpZ24gQ0EgMwITQwMG
# LpbRsruqZJ+lAAABAwYuljAJBgUrDgMCGgUAoHAwEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEOMAwGCisG
# AQQBgjcCARUwIwYJKoZIhvcNAQkEMRYEFDFRa0VJKJQ1h2LG6dYzXKpBneOfMA0G
# CSqGSIb3DQEBAQUABIIBAHbWmEOWfj37SNw8NDnAAg7bl0L3oyGVKPWysRnriHC9
# aYImucAy2QXKo6YUWxHMqFvRPFrF07qkTDV249iC+L8gb1X0wwq/YuWWFbdN2J8s
# 4CnN6I4Ff2AF4Co34MZGhtIHd3D7H1oPMelTlHQOc5CXyB/wkduoNgS0GCoeZXSK
# DdMuN7dbru3PvCxe0ShzRwxBOa4EWZ6dHDAQRdrxkK2vVLWHg+6th8lRNnCJQeb+
# 03tMRItnm/sAmKR9PCWm4YZob3ug9T9Qa1K00TuNskjXO+G2S2mjhFC5+HGKjLZd
# bJydl0MIIMBtlLEGa4CcFtszxaww5Cx+YtCbxPp3iII=
# SIG # End signature block
"@
                    }
                }

                set-content $filePath -Value $content

                ## Valida File types and their corresponding int values are :
                ##  
                ##    Local = -1
                ##    MyComputer = 0
                ##    Intranet = 1
                ##    Trusted = 2
                ##    Internet = 3
                ##    Untrusted = 4
                ## We need to add alternate streams in all files except for the local file

                if(-1 -ne $FileType)
                {
                    $alternateStreamContent = @"
[ZoneTransfer]
ZoneId=$FileType
"@
                    Add-Content -Path $filePath -Value $alternateStreamContent -stream Zone.Identifier
                }
            }
            
            foreach($fileInfo in $testFilesInfo)
            {
                createTestFile -FilePath $fileInfo.filePath -FileType $fileInfo.fileType -AddSignature:$fileInfo.AddSignature -Corrupted:$fileInfo.corrupted
            }

            #Get Execution Policy
            $originalExecPolicy = Get-ExecutionPolicy
            $originalExecutionPolicy =  $originalExecPolicy
    }
    AfterAll {
        #Clean up
        
            $testDirectory = $remoteTestDirectory

            Remove-Item $testDirectory -Recurse -Force -ea SilentlyContinue
            Remove-Item function:createTestFile -ea SilentlyContinue
    }
    Context "Validate that 'Restricted' execution policy works on OneCore powershell" {

        BeforeAll {
            Set-ExecutionPolicy Restricted -Force -Scope Process | Out-Null
        }

        AfterAll {
            Set-ExecutionPolicy $originalExecutionPolicy -Force -Scope Process | Out-Null
        }

        function Test-RestrictedExecutionPolicy
        {
            param ($testScript)

            $TestTypePrefix = "Test 'Restricted' execution policy."

            It "$TestTypePrefix Running $testScript script should raise PSSecurityException" -skip:(-not $IsWindows) {

                $scriptName = $testScript

                $exception = $null
                try {
                    & $scriptName
                }
                catch
                {
                    $exception = $_
                }

                $exceptionType = $exception.Exception.getType()
                $result = $exceptionType

                $result |  Should be "System.Management.Automation.PSSecurityException"
            }
        }

        $testScripts = @(
            $InternetSignatureCorruptedScript
            $InternetSignedScript
            $InternetUnsignedScript
            $IntranetSignatureCorruptedScript
            $IntranetSignedScript
            $IntranetUnsignedScript
            $LocalSignatureCorruptedScript
            $localSignedScript
            $LocalUnsignedScript
            $TrustedSignatureCorruptedScript
            $TrustedSignedScript
            $UntrustedSignatureCorruptedScript
            $UntrustedSignedScript
            $UntrustedUnsignedScript
            $TrustedUnsignedScript
            $MyComputerSignatureCorruptedScript
            $MyComputerSignedScript
            $MyComputerUnsignedScript
        )

        foreach($testScript in $testScripts)
        {
            Test-RestrictedExecutionPolicy $testScript
        }
    }

    AfterAll {
        # Clean up 
        $testDirectory = $remoteTestDirectory

        Remove-Item $testDirectory -Recurse -Force -ea SilentlyContinue
        Remove-Item function:createTestFile -ea SilentlyContinue
    }
    Context "Validate that 'Unrestricted' execution policy works on OneCore powershell" {

        BeforeAll {
            Set-ExecutionPolicy Unrestricted -Force -Scope Process | Out-Null
        }

        AfterAll {
            Set-ExecutionPolicy $originalExecutionPolicy -Force -Scope Process | Out-Null
        }

        function Test-UnrestrictedExecutionPolicy {

            param($testScript, $expected)

            $TestTypePrefix = "Test 'Unrestricted' execution policy."

            It "$TestTypePrefix Running $testScript script should return $expected" -skip:(-not $IsWindows) {
                $scriptName = $testScript

                $result = & $scriptName

                $result |  Should be $expected
            }
        }

        $expected = "Hello"
        $testScripts = @(
            $IntranetSignatureCorruptedScript
            $IntranetSignedScript
            $IntranetUnsignedScript
            $LocalSignatureCorruptedScript
            $localSignedScript
            $LocalUnsignedScript
            $TrustedSignatureCorruptedScript
            $TrustedSignedScript
            $TrustedUnsignedScript
            $MyComputerSignatureCorruptedScript
            $MyComputerSignedScript
            $MyComputerUnsignedScript
        )

        foreach($testScript in $testScripts) {
            Test-UnrestrictedExecutionPolicy $testScript $expected
        }
    }

    Context "Validate that 'ByPass' execution policy works on OneCore powershell" {

        BeforeAll {
            Set-ExecutionPolicy Bypass -Force -Scope Process | Out-Null
        }

        AfterAll {
            Set-ExecutionPolicy $originalExecutionPolicy -Force -Scope Process | Out-Null
        }

        function Test-ByPassExecutionPolicy {

            param($testScript, $expected)

            $TestTypePrefix = "Test 'ByPass' execution policy."

            It "$TestTypePrefix Running $testScript script should return $expected"  -skip:(-not $IsWindows)  {
                $scriptName = $testScript

                $result = & $scriptName
                return $result

                $result |  Should be $expected
            }
        }

        $expected = "Hello"
        $testScripts = @(
            $InternetSignatureCorruptedScript
            $InternetSignedScript
            $InternetUnsignedScript
            $IntranetSignatureCorruptedScript
            $IntranetSignedScript
            $IntranetUnsignedScript
            $LocalSignatureCorruptedScript
            $LocalSignedScript
            $LocalUnsignedScript
            $TrustedSignatureCorruptedScript
            $TrustedSignedScript
            $TrustedUnsignedScript
            $UntrustedSignatureCorruptedScript
            $UntrustedSignedScript
            $UntrustedUnSignedScript
            $MyComputerSignatureCorruptedScript
            $MyComputerSignedScript
            $MyComputerUnsignedScript
        )
        foreach($testScript in $testScripts) {
            Test-ByPassExecutionPolicy $testScript $expected
        }
    }

    Context "'RemoteSigned' execution policy works on OneCore powershell" {

        BeforeAll {
                Set-ExecutionPolicy RemoteSigned -Force -Scope Process | Out-Null
        }

        AfterAll {
            Set-ExecutionPolicy $originalExecutionPolicy -Force -Scope Process
        }

        function Test-RemoteSignedExecutionPolicy {

            param($testScript, $expected, $error)

            $TestTypePrefix = "Test 'RemoteSigned' execution policy."

            It "$TestTypePrefix Running $testScript script should return $expected" -skip:(-not $IsWindows) {
                $scriptName=$testScript

                $scriptResult = $null
                $exception = $null

                try
                {
                    $scriptResult = & $scriptName
                }
                catch
                {
                    $exception = $_
                }

                $errorType = $null
                if($null -ne $exception)
                {
                    $errorType = $exception.exception.getType()
                    $scriptResult = $null
                }
                $result = @{
                    "result" = $scriptResult
                    "exception" = $errorType
                }

                $actualResult = $result."result"
                $actualError = $result."exception"

                $actualResult |  Should be $expected
                $actualError | Should be $error
            }
        }
        $message = "Hello"
        $error = "System.Management.Automation.PSSecurityException"
        $testData = @(
        @{
            testScript = $LocalUnsignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $LocalSignatureCorruptedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $LocalSignedScript
            expected = "Hello"
            error = $null
        }
        @{
            testScript = $MyComputerUnsignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $MyComputerSignatureCorruptedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $myComputerSignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $TrustedUnsignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $TrustedSignatureCorruptedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $TrustedSignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $IntranetUnsignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $IntranetSignatureCorruptedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $IntranetSignedScript
            expected = $message
            error = $null
        }
        @{
            testScript = $InternetUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $InternetSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $UntrustedUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $UntrustedSignatureCorruptedScript
            expected = $null
            error = $error
        }
        )

        foreach($testCase in $testData) {
            Test-RemoteSignedExecutionPolicy @testCase
        }
    }

    Context "Validate that 'AllSigned' execution policy works on OneCore powershell" {

        BeforeAll {
            Set-ExecutionPolicy AllSigned -Force -Scope Process
        }

        AfterAll {
            Set-ExecutionPolicy $originalExecutionPolicy -Force -Scope Process
        }

        function Test-AllSignedExecutionPolicy {

            param($testScript, $error)

            $TestTypePrefix = "Test 'AllSigned' execution policy."

            It "$TestTypePrefix Running $testScript script should return $error" -skip:(-not $IsWindows) {

                $scriptName = $testScript

                $exception = $null
                try
                {
                    & $scriptName
                }
                catch
                {
                    $exception = $_
                }
                $errorType = $null

                if($null -ne $exception)
                {
                    $errorType = $exception.exception.getType()
                }

                $result = $errorType

                $result | Should be $error
            }
        }
        $error = "System.Management.Automation.PSSecurityException"
        $testData = @(
        @{
            testScript = $LocalUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $LocalSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $MyComputerUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $MyComputerSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $TrustedUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $TrustedSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $IntranetUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $IntranetSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $InternetUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $InternetSignatureCorruptedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $UntrustedUnsignedScript
            expected = $null
            error = $error
        }
        @{
            testScript = $UntrustedSignatureCorruptedScript
            expected = $null
            error = $error
        }
        )
        foreach($testScript in $testScripts) {
            Test-AllSignedExecutionPolicy $testScript $error
        }
    }
}

Describe "Validate Set-ExecutionPolicy -Scope" -Tags "CI" {

    BeforeAll {
        $originalPolicies = Get-ExecutionPolicy -list

        # Calls Set-ExecutionPolicy with a known-bad Scope and expects failure.
        # It is defined here so that it will be available at It scope.
        function VerfiyBlockedSetExecutionPolicy
        {
            param(
                [string]
                $policyScope
            )
            $fqeid = ""
            try {
                Set-ExecutionPolicy -Scope $policyScope -ExecutionPolicy Restricted
            }
            catch {
                $fqeid = $_.FullyQualifiedErrorId
            }

            $fqeid | Should Be "CantSetGroupPolicy,Microsoft.PowerShell.Commands.SetExecutionPolicyCommand"
        }
    }

    AfterAll {
        foreach ($scopedPolicy in $originalPolicies)
        {
            if (($scopedPolicy.Scope -eq "Process") -or
                ($scopedPolicy.Scope -eq "CurrentUser"))
            {
                try {
                    Set-ExecutionPolicy -Scope $scopedPolicy.Scope -ExecutionPolicy $scopedPolicy.ExecutionPolicy -Force
                }
                catch {
                    if ($_.FullyQualifiedErrorId -ne "ExecutionPolicyOverride,Microsoft.PowerShell.Commands.SetExecutionPolicyCommand")
                    {
                        # Re-throw unrecognized exceptions. Otherwise, swallow 
                        # the exception that warns about overridden policies
                        throw $_
                    }
                }
            }
            elseif($scopedPolicy.Scope -eq "LocalMachine")
            {
                try {
                    Set-ExecutionPolicy -Scope $scopedPolicy.Scope -ExecutionPolicy $scopedPolicy.ExecutionPolicy -Force
                }
                catch {
                    if ($_.FullyQualifiedErrorId -eq "System.UnauthorizedAccessException,Microsoft.PowerShell.Commands.SetExecutionPolicyCommand")
                    {
                        # Do nothing. Depending on the ownership of the file, 
                        # regular users may or may not be able to set its 
                        # value. 
                        #
                        # When targetting the Registry, regular users cannot
                        # modify this value.
                    }
                    elseif ($_.FullyQualifiedErrorId -ne "ExecutionPolicyOverride,Microsoft.PowerShell.Commands.SetExecutionPolicyCommand")
                    {
                        # Re-throw unrecognized exceptions. Otherwise, swallow 
                        # the exception that warns about overridden policies
                        throw $_
                    }
                }
            }
        }
    }

    It "-Scope MachinePolicy is not Modifiable" {
        VerfiyBlockedSetExecutionPolicy "MachinePolicy"
    }

    It "-Scope UserPolicy is not Modifiable" {
        VerfiyBlockedSetExecutionPolicy "UserPolicy"
    }

    It "-Scope Process is Settable" {
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy ByPass
        Get-ExecutionPolicy -Scope Process | Should Be "ByPass"
    }

    It "-Scope CurrentUser is Settable" {
        Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy ByPass
        Get-ExecutionPolicy -Scope CurrentUser | Should Be "ByPass"
    }

    # This test requires Administrator privileges on Windows.
    It "-Scope LocalMachine is Settable" {
        Set-ExecutionPolicy -Scope LocalMachine -ExecutionPolicy ByPass
        Get-ExecutionPolicy -Scope LocalMachine | Should Be "ByPass"
    }
}

Describe "Validate that 'ConvertTo-SecureString -Key' and 'ConvertFrom-SecureString -Key' work on NanoServer and IoT" -Tags "CI" {

    It "ConvertTo-SecureString should return back the SecureString that was constructed from 'ValidateConvertSecureString'." -skip:(-not $IsWindows) {

        $testString = "ValidateConvertSecureString"
        $secureString = ConvertTo-SecureString -String "ValidateConvertSecureString" -AsPlainText -Force
        $key = (3,4,2,3,56,34,254,222,1,1,2,23,42,54,33,233,1,34,2,7,6,5,35,43)

        $encryptedString = ConvertFrom-SecureString -SecureString $secureString -Key $key
        $decryptedString = ConvertTo-SecureString -String $encryptedString -Key $key

        $cred = [pscredential]::new("domain\user", $decryptedString)
        $netCred = $cred.GetNetworkCredential()
        $password = $netCred.Password

        $password | Should be $testString
    }
}
