#if UNITY_EDITOR_WIN
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Windows DPAPI for the current user, through a P/Invoke to <c>crypt32.dll</c>.
    /// <c>ProtectedData</c> is not used: it needs a reference to <c>System.Security.dll</c> that the
    /// SDK's editor assemblies do not have, and that a creator project at the .NET Standard API level
    /// does not have at all. The scope is the same as <c>DataProtectionScope.CurrentUser</c>: another OS
    /// user, or a copy of the file on another machine, cannot read what this wrote. Another process of
    /// the same user can.
    /// </summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        private const int CryptprotectUiForbidden = 0x1;

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy,
                                                    IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy,
                                                      IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        public static byte[] Protect(byte[] plain, byte[] entropy) => Run(true, plain, entropy);

        public static byte[] Unprotect(byte[] cipher, byte[] entropy) => Run(false, cipher, entropy);

        private static byte[] Run(bool protect, byte[] input, byte[] entropy)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            IntPtr inputPtr = Marshal.AllocHGlobal(Math.Max(1, input.Length));
            IntPtr entropyPtr = IntPtr.Zero;
            IntPtr entropyBlobPtr = IntPtr.Zero;
            DataBlob output = default;

            try
            {
                Marshal.Copy(input, 0, inputPtr, input.Length);
                DataBlob inputBlob = new() { cbData = input.Length, pbData = inputPtr };

                if (entropy != null && entropy.Length > 0)
                {
                    entropyPtr = Marshal.AllocHGlobal(entropy.Length);
                    Marshal.Copy(entropy, 0, entropyPtr, entropy.Length);
                    entropyBlobPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DataBlob)));
                    Marshal.StructureToPtr(new DataBlob { cbData = entropy.Length, pbData = entropyPtr }, entropyBlobPtr, false);
                }

                bool ok = protect
                    ? CryptProtectData(ref inputBlob, null, entropyBlobPtr, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, entropyBlobPtr, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);

                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), protect ? "CryptProtectData failed" : "CryptUnprotectData failed");

                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                if (output.pbData != IntPtr.Zero)
                    LocalFree(output.pbData);

                Marshal.FreeHGlobal(inputPtr);

                if (entropyPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(entropyPtr);

                if (entropyBlobPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(entropyBlobPtr);
            }
        }
    }
}
#endif
