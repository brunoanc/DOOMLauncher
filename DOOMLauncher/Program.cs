using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

[DllImport("kernel32.dll")]
static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, bool bInheritHandle, Int32 dwProcessId);

[DllImport("kernel32.dll")]
static extern Int32 CloseHandle(IntPtr hObject);

[DllImport("kernel32.dll")]
static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

[DllImport("kernel32.dll")]
static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesRead);

[DllImport("kernel32.dll")]
static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesWritten);

// Check if the app was ran as an administrator
try
{
    var user = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(user);
    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
    {
        throw new Exception();
    }
}
catch
{
    Console.WriteLine("The app was not ran as administrator, aborting.");
    return 1;
}

// Launch doom with the passed arguments
var startInfo = new ProcessStartInfo();
startInfo.UseShellExecute = true;
startInfo.FileName = ("steam://run/379720//" + String.Join(" ", args));
var doom = Process.Start(startInfo);
if (doom == null)
{
    Console.WriteLine("Failed to open the DOOM process!");
    return 1;
}

// Wait for the DOOM process to be re-opened
Process? doomProcess = null;

SpinWait.SpinUntil(() =>
{
    var processList = Process.GetProcesses();
    foreach (var process in processList)
    {
        if (process.ProcessName.Equals("DOOMx64vk", StringComparison.CurrentCultureIgnoreCase))
        {
            doomProcess = process;
            return true;
        }
    }

    return false;
});

try
{
    // Open the process
    var pHandle = OpenProcess(0x1F0FFF, true, doomProcess!.Id);
    if (pHandle == IntPtr.Zero)
    {
        throw new Exception();
    }

    // Get the address for the memory we will replace (old game version)
    var oldAddress = IntPtr.Add(doomProcess.MainModule!.BaseAddress, 0x18A31D0);

    // Get the address for the memory we will replace (new game update)
    var newAddress = IntPtr.Add(doomProcess.MainModule!.BaseAddress, 0x1596A40);

    // Determine which game version we have
    var address = IntPtr.Zero;

    // Wait until the memory is loaded
    SpinWait.SpinUntil(() =>
    {
        var bytesRead = new byte[3];

        if (ReadProcessMemory(pHandle, oldAddress, bytesRead, (UIntPtr)bytesRead.Length, IntPtr.Zero))
        {
            // Check if the movzx instruction has been loaded into memory
            if (bytesRead.SequenceEqual(new byte[] { 0x0F, 0xB6, 0x81 }))
            {
                address = oldAddress;
                return true;
            }
        }

        if (ReadProcessMemory(pHandle, newAddress, bytesRead, (UIntPtr)bytesRead.Length, IntPtr.Zero))
        {
            // Check if the movzx instruction has been loaded into memory
            if (bytesRead.SequenceEqual(new byte[] { 0x0F, 0xB6, 0x81 }))
            {
                address = newAddress;
                return true;
            }
        }

        return false;
    });

    // New instruction to write (xor eax,eax)
    var bytesToWrite = new byte[] { 0x31, 0xC0, 0x90, 0x90, 0x90, 0x90, 0x90 };

    // Remove write protection
    uint oldMemProt = 0x00;
    VirtualProtectEx(pHandle, address, (IntPtr)bytesToWrite.Length, (uint)0x40, out oldMemProt);

    // Apply the patch
    WriteProcessMemory(pHandle, address, bytesToWrite, (UIntPtr)bytesToWrite.Length, IntPtr.Zero);

    // Restore write protection
    VirtualProtectEx(pHandle, address, (IntPtr)bytesToWrite.Length, oldMemProt, out _);

    // Close the process
    CloseHandle(pHandle);

    // Exit
    Console.WriteLine("DOOM process has been patched successfully.");
    return 0;
}
catch (Exception ex)
{
    // Exit
    Console.WriteLine(ex);
    Console.WriteLine("Failed to patch the DOOM process!");
    return 1;
}
