Describe "Write-Error DRT Unit Tests" -Tags "CI" {
    It "Should be works with command: write-error myerrortext" {
        $e = Write-Error myerrortext 2>&1
        $e | Should Not BeNullOrEmpty
        $e.GetType().Name | Should Be 'ErrorRecord'
        
        #Exception verification
        $e.Exception.GetType().Name | Should Be 'WriteErrorException'
        $e.Exception.Message | Should Be 'myerrortext'
        $e.Exception.Data.Count | Should Be 0
        $e.Exception.InnerException | Should BeNullOrEmpty 
        
        #ErrorCategoryInfo verification
        $e.CategoryInfo | Should Not BeNullOrEmpty
        $e.CategoryInfo.Category | Should Be 'NotSpecified'
        $e.CategoryInfo.Activity | Should Be 'Write-Error'
        $e.CategoryInfo.Reason | Should Be 'WriteErrorException'
        $e.CategoryInfo.TargetName | Should BeNullOrEmpty
        $e.CategoryInfo.TargetType | Should BeNullOrEmpty
        $e.CategoryInfo.GetMessage() | Should Be 'NotSpecified: (:) [Write-Error], WriteErrorException'

        #ErrorDetails verification
        $e.ErrorDetails | Should BeNullOrEmpty

        #FullyQualifiedErrorId verification 
        $e.FullyQualifiedErrorId | Should Be 'Microsoft.PowerShell.Commands.WriteErrorException'

        #InvocationInfo verification
        $e.InvocationInfo | Should Not BeNullOrEmpty
        $e.InvocationInfo.MyCommand.Name | Should BeNullOrEmpty     
    }

    #Blocked by issue #846
    It "Should be works with all parameters" -Pending { 
        $exception = New-Object -TypeName System.ArgumentNullException -ArgumentList paramname 
        $e = Write-Error -Message myerrortext -Exception $exception -ErrorId myerrorid -Category syntaxerror -TargetObject TargetObject -CategoryActivity myactivity -CategoryReason myreason -CategoryTargetName mytargetname -CategoryTargetType mytargettype -RecommendedAction myrecommendedaction 2>&1
        $e | Should Not BeNullOrEmpty
        $e.GetType().Name | Should Be 'ErrorRecord'

        #Exception verification
        $e.Exception | Should Not BeNullOrEmpty
        $e.Exception.GetType().Name | Should Be 'ArgumentNullException'        
        $e.Exception.ParamName | Should Be 'paramname'
        $e.Exception.Data.Count | Should Be 0
        $e.Exception.InnerException | Should BeNullOrEmpty  
        
        #TargetObject verification 
        $e.TargetObject | Should Be 'TargetObject'

        #FullyQualifiedErrorId verification
        $e.FullyQualifiedErrorId | Should Be 'myerrorid'

        #ErrorCategoryInfo verification
        $e.CategoryInfo | Should Not BeNullOrEmpty
        $e.CategoryInfo.Category | Should Be 'SyntaxError'
        $e.CategoryInfo.Activity | Should Be 'myactivity'
        $e.CategoryInfo.Reason | Should Be 'myreason'
        $e.CategoryInfo.TargetName | Should Be 'mytargetname'
        $e.CategoryInfo.TargetType | Should Be 'mytargettype'
        $e.CategoryInfo.GetMessage() | Should Be 'SyntaxError: (mytargetname:mytargettype) [myactivity], myreason'

        #ErrorDetails verification
        $e.ErrorDetails | Should Not BeNullOrEmpty
        $e.ErrorDetails.Message | Should Be 'myerrortext'
        $e.ErrorDetails.RecommendedAction | Should Be 'myrecommendedaction'

        #InvocationInfo verification
        $e.InvocationInfo | Should Not BeNullOrEmpty
        $e.InvocationInfo.MyCommand.Name | Should BeNullOrEmpty  
    }

    #Blocked by issue #846
    It "Should be works with all parameters" -Pending {
        $e = write-error -Activity fooAct -Reason fooReason -TargetName fooTargetName -TargetType fooTargetType -Message fooMessage 2>&1
        $e.CategoryInfo.Activity | Should Be 'fooAct'
        $e.CategoryInfo.Reason | Should Be 'fooReason'
        $e.CategoryInfo.TargetName | Should Be 'fooTargetName'
        $e.CategoryInfo.TargetType | Should Be 'fooTargetType'
        $e.CategoryInfo.GetMessage() | Should Be 'NotSpecified: (fooTargetName:fooTargetType) [fooAct], fooReason'
    }
}

Describe "Write-Error" -Tags "CI" {
    It "Should be able to throw" {
	Write-Error "test throw" -ErrorAction SilentlyContinue | Should Throw
    }

    It "Should throw a non-terminating error" {
	Write-Error "test throw" -ErrorAction SilentlyContinue

	1 + 1 | Should Be 2
    }

    It "Should trip an exception using the exception switch" {
	$var = 0
	try
	{
	    Write-Error -Exception -Message "test throw"
	}
	catch [System.Exception]
	{

	    $var++
	}
	finally
	{
	    $var | Should Be 1
	}
    }

    It "Should output the error message to the `$error automatic variable" {
	$theError = "Error: Too many input values."
	write-error -message $theError -category InvalidArgument -ErrorAction SilentlyContinue

	$error[0]| Should Be $theError
    }
}
