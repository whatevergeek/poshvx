Describe "DSC MOF Compilation" -tags "CI" {

    AfterAll {
        $env:PSMODULEPATH = $_modulePath
    }
    BeforeAll {
        $env:DSC_HOME = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath assets) -ChildPath dsc
        $_modulePath = $env:PSMODULEPATH
        $powershellexe = (get-process -pid $PID).MainModule.FileName
        $env:PSMODULEPATH = join-path ([io.path]::GetDirectoryName($powershellexe)) Modules
    }

    It "Should be able to compile a MOF from a basic configuration" -Skip:($IsOSX -or $IsWindows) {
        [Scriptblock]::Create(@"
        configuration DSCTestConfig
        {
            Import-DscResource -ModuleName PSDesiredStateConfiguration
            Node "localhost" {
                nxFile f1
                {
                    DestinationPath = "/tmp/file1";
                }
            }
        }

        DSCTestConfig
"@).Invoke()

        Remove-Item -Force -Recurse -Path DSCTestConfig 
    }

    It "Should be able to compile a MOF from another basic configuration" -Skip:($IsOSX -or $IsWindows) {
        [Scriptblock]::Create(@"
        configuration DSCTestConfig
        {
            Import-DscResource -ModuleName PSDesiredStateConfiguration
            Node "localhost" {
                nxScript f1
                {
                    GetScript = "";
                    SetScript = "";
                    TestScript = "";
                    User = "root";
                }
            }
        }

        DSCTestConfig
"@).Invoke()

        Remove-Item -Force -Recurse -Path DSCTestConfig 
    }

    It "Should be able to compile a MOF from a complex configuration" -Skip:($IsOSX -or $IsWindows) {
        [Scriptblock]::Create(@"
	Configuration WordPressServer{

                Import-DscResource -ModuleName PSDesiredStateConfiguration		

		Node CentOS{
		
			#Ensure Apache packages are installed
				nxPackage httpd {
					Ensure = "Present"
					Name = "httpd"
					PackageManager = "yum"
				}

		#Include vhostdir
		nxFile vHostDir{
		   DestinationPath = "/etc/httpd/conf.d/vhosts.conf"
		   Ensure = "Present"
		   Contents = "IncludeOptional /etc/httpd/sites-enabled/*.conf`n"
		   Type = "File"
		}

		nxFile vHostDirectory{
			DestinationPath = "/etc/httpd/sites-enabled"
			Type = "Directory"
			Ensure = "Present"
		}


		#Ensure directory for Wordpress site
		nxFile wpHttpDir{
			DestinationPath = "/var/www/wordpress"
			Type = "Directory"
			Ensure = "Present"
			Mode = "755"
		}

		#Ensure share directory
		nxFile share{
			DestinationPath = "/mnt/share"
			Type = "Directory"
			Ensure = "Present"
			Mode = "755"
		}

		#Bind httpd to port 8080
		nxFile HttpdPort{
		   DestinationPath = "/etc/httpd/conf.d/listen.conf"
		   Ensure = "Present"
		   Contents = "Listen 8080`n"
		   Type = "File"
		}

		#nfs mounts
		nxScript nfsMount{
			TestScript= "#!/bin/bash"
			GetScript="#!/bin/bash"
			SetScript="#!/bin/bash"

		}

		#Retrieve latest wordpress
		nxFile WordPressTar{
			SourcePath = "/mnt/share/latest.zip"
			DestinationPath = "/tmp/wordpress.zip"
			Checksum = "md5"
			Type = "file"    
			DependsOn = "[nxScript]nfsMount"
		}
		 
		#Extract wordpress if changed
		nxArchive ExtractSite{
			SourcePath = "/tmp/wordpress.zip"
			DestinationPath = "/var/www/wordpress"
			Ensure = "Present"
			DependsOn = "[nxFile]WordpressTar"
		 }

		 #Set wp-config


		 #Fixup SE Linux context
		 #nxScript SELinuxContext{
			#TestScript= "#!/bin/bash"
			#GetScript = "#!/bin/bash"
			#SetScript = "#!/bin/bash"
		 #}

		 #Disable SELinux
		 nxFileLine SELinux {
			Filepath = "/etc/selinux/config"
			DoesNotContainPattern = "SELINUX=enforcing"
			ContainsLine = "SELINUX=disabled"
		 }

		
		nxScript SELinuxHTTPNet{
		  GetScript = "#!/bin/bash`ngetsebool httpd_can_network_connect"
		  setScript = "#!/bin/bash`nsetsebool -P httpd_can_network_connect=1"
		  TestScript = "#!/bin/bash`n exit 1"
		}



		}

	}
        WordPressServer
"@).Invoke()

        Remove-Item -Force -Recurse -Path WordPressServer
    }

}
