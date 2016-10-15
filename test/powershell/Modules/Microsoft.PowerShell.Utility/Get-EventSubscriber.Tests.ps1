Describe "Get-EventSubscriber" -Tags "CI" {

    AfterEach {
	Unregister-Event -SourceIdentifier PesterTestRegister -ErrorAction SilentlyContinue
    }

    Context "Check return type of Get-EventSubscriber" {

	It "Should return System.Management.Automation.PSEventSubscriber as return type of New-Event" {
	    $pesterobject = (New-Object System.Collections.ObjectModel.ObservableCollection[object])
	    Register-ObjectEvent -InputObject $pesterobject -EventName CollectionChanged -SourceIdentifier PesterTestRegister
	    (Get-EventSubscriber).GetType() | Should Be System.Management.Automation.PSEventSubscriber
	}
    }

    Context "Check Get-EventSubscriber can validly register events"{
	It "Should return source identifier of PesterTimer " {
	    $pesterobject = (New-Object System.Collections.ObjectModel.ObservableCollection[object])
	    Register-ObjectEvent -InputObject $pesterobject -EventName CollectionChanged -SourceIdentifier PesterTestRegister
	    (Get-EventSubscriber -SourceIdentifier PesterTestRegister).SourceIdentifier | Should Be "PesterTestRegister"
	}

	It "Should return an integer greater than 0 for the SubscriptionId" {
	    $pesterobject = (New-Object System.Collections.ObjectModel.ObservableCollection[object])
	    Register-ObjectEvent -InputObject $pesterobject -EventName CollectionChanged -SourceIdentifier PesterTestRegister
	    (Get-EventSubscriber -SourceIdentifier PesterTestRegister).SubscriptionId | Should BeGreaterThan 0

	}
    }
}
