﻿Describe "New-Guid" -Tags "CI" {

    It "returns a new guid" {
        $guid = New-Guid
        $guid.GetType().FullName | Should Be "System.Guid"
    }

    It "should not be all zeros" {
        $guid = New-Guid
        $guid.ToString() | Should Not Be "00000000-0000-0000-0000-000000000000"
    }

    It "should return different guids with each call" {
        $guid1 = New-Guid
        $guid2 = New-Guid
        $guid1.ToString() | Should Not Be $guid2.ToString()
    }
}

