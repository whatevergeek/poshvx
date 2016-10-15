using Xunit;
using System;
using System.Management.Automation;

namespace PSTests
{
    [Collection("AssemblyLoadContext")]
    public static class SecuritySupportTests
    {
        [Fact]
        public static void TestScanContent()
        {
            Assert.Equal(AmsiUtils.ScanContent("", ""), AmsiUtils.AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED);
        }

        [Fact]
        public static void TestCurrentDomain_ProcessExit()
        {
            Assert.Throws<PlatformNotSupportedException>(delegate {
                    AmsiUtils.CurrentDomain_ProcessExit(null, EventArgs.Empty);
                });
        }

        [Fact]
        public static void TestCloseSession()
        {
            AmsiUtils.CloseSession();
        }

        [Fact]
        public static void TestUninitialize()
        {
            AmsiUtils.Uninitialize();
        }
    }
}
