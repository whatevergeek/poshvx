Preparing
=========

PowerShell releases use [Semantic Versioning][semver].
Until we hit 6.0, each sprint results in a bump to the build number,
so `v6.0.0-alpha.7` goes to `v6.0.0-alpha.8`.

When a particular commit is chosen as a release,
we create an [annotated tag][tag] that names the release,
and list the major changes since the previous release.
An annotated tag has a message (like a commit),
and is *not* the same as a lightweight tag.
Create one with `git tag -a v6.0.0-alpha.7`.
Our convention is to prepend the `v` to the semantic version.
The summary (first line) of the annotated tag message should be the full release title,
e.g. 'v6.0.0-alpha.7 release of PowerShell'.

While creating a release, it is advised to make a new branch such that
necessary documentation updates and hot fixes can be made,
without having to include all changes made to master.
This release branch can be reviewed by the normal PR process.

When the annotated tag is finalized, push it with `git push --tags`.
GitHub will see the tag and present it as an option when creating a new [release][].
Start the release, use the annotated tag's summary as the title,
and save the release as a draft while you upload the binary packages.

Just as important as creating the release is updating the links on our readme,
and the package names in the installation instructions.
The AppVeyor build number should also be incremented.

When creating the packages, please ensure that the file path does not contain user names.
That is, clone to `/PowerShell` on Unix, and `C:\PowerShell` for Windows.
The debug symbols include the absolute path to the sources when built,
so it should appear `/PowerShell/src/powershell/System.Management.Automation`,
not `/home/username/src/PowerShell/...`.

[semver]: http://semver.org/
[tag]: https://git-scm.com/book/en/v2/Git-Basics-Tagging
[release]: https://help.github.com/articles/creating-releases/

Building Packages
=================

The `build.psm1` module contains a `Start-PSPackage` function to build packages.
It **requires** that `Start-PSBuild -CrossGen` has been run.

Linux / macOS
------------

The `Start-PSBuild` function delegates to `New-UnixPackage`.
This function will automatically deduce the correct version from the most recent annotated tag (using `git describe`).

At this time, each package must be made on the corresponding platform.
The packages each have the .NET Core runtime-identifier appended to their filename.
This is necessary to differentiate the Ubuntu 14.04 and 16.04 packages,
which must be separate due to having different dependencies.

The `Start-PSBuild` function relies on the [Effing Package Management][fpm] project,
which makes building packages for any (non-Windows) platform a breeze.
Similarly, the PowerShell man-page is generated from the Markdown-like file
[`assets/powershell.1.ronn`][man] using [Ronn][].
The function `Start-PSBootstrap -Publish` will install both these tools.

To modify any property of the packages, edit the `New-UnixPackage` function.
Please also refer to the function for details on the package properties
(such as the description, maintainer, vendor, URL,
license, category, dependencies, and file layout).

> Note that the only configuration on Linux and macOS is `Linux`,
> which is release (i.e. not debug) configuration.

### Side-By-Side Design

To support side-by-side Unix packages, we use the following design:

We will maintain a `powershell` package
which owns the `/usr/bin/powershell` symlink,
is the latest version, and is upgradeable.
This is the only package named `powershell`
and similarly is the only package owning any symlinks,
executables, or man-pages named `powershell`.
Until we have a package repository,
this package will contain actual PowerShell bits
(i.e. it is not a meta-package).
These bits are installed to `/opt/microsoft/powershell/6.0.0-alpha.8/`,
where the version will change with each update
(and is the pre-release version).
On macOS, the prefix is `/usr/local`,
instead of `/opt/microsoft` because it is derived from BSD.

> When we have access to package repositories where dependencies can be properly resolved,
> this `powershell` package can become a meta-package which auto-installs the latest package,
> and so only owns the symlink.

For explicitly versioned packages, say for PowerShell 6.0,
we will maintain separate packages named in the form `powershell6.0`,
which owns the binary `powershell6.0`, the symlink `powershell6.0`,
the man-page `powershell6.0`,
and is installed to `/opt/microsoft/powershell/6.0/`.
Specifically this package owns nothing named `powershell`,
as only the `powershell` package owns those files.
This package is upgradeable, but should only be updated with hot-fixes.
This is a necessary consequence of Unix package managers,
as files among packages *cannot* conflict.
From a user-experience perspective,
if the user requires a specific version of PowerShell,
they should not be required to use an absolute path,
and instead should be given a binary with the version in the name.
This pattern is followed by many other languages
(Python being the most obvious example).
This same pattern can be followed for versions 6.1, 7.0, etc.,
and can be used for patch version (e.g. 6.0.1).
Use `Start-PSPackage -Name powershell6.0` to generate
the versioned `powershell6.0` package.
Without `-Name` specified, the primary `powershell`
package will instead be created.

[fpm]: https://github.com/jordansissel/fpm
[man]: ../../assets/powershell.1.ronn
[ronn]: https://github.com/rtomayko/ronn

Windows
-------

The `Start-PSBuild` function delegates to `New-MSIPackage` which creates a Windows Installer Package of PowerShell.
The packages *must* be published in release mode, so use `Start-PSBuild -CrossGen -Configuration Release`.
It uses the Windows Installer XML Toolset (WiX) to generate a `PowerShell_<version>.msi`,
which installs a self-contained copy of the current version (commit) of PowerShell.
It copies the output of the published PowerShell application to a version-specific folder in Program Files,
and installs a shortcut in the Start Menu.
It can be uninstalled through Programs and Features.

Note that PowerShell is always self-contained, thus using it does not require installing it.
The output of `Start-PSBuild` includes a `powershell.exe` executable which can simply be launched.
