using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace DOOMLauncher;

// kernel32.dll imported functions and values
internal class Kernel32
{
    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, bool bInheritHandle, Int32 dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern Int32 CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern int FreeConsole();

    // Process flags
    public static readonly uint PROCESS_VM_READ = 0x0010;

    public static readonly uint PROCESS_VM_WRITE = 0x0020;

    public static readonly uint PROCESS_VM_OPERATION = 0x0008;

    // Console flags
    public static readonly uint ATTACH_PARENT_PROCESS = 0x0FFFFFFFF;
}

// Main program class
internal class Program {
    // Patch memory for given process
    static bool PatchProcessMemory(string name, int[] possibleOffsets, byte[] originalMemory, byte[] memoryToWrite)
    {
        // Wait for the process to be opened
        Process? process = null;
        var foundProcess = SpinWait.SpinUntil(() =>
        {
            var processList = Process.GetProcesses();
            foreach (var possibleProcess in processList)
            {
                if (possibleProcess.ProcessName.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                {
                    process = possibleProcess;
                    return true;
                }
            }

            return false;
        }, 120000);

        if (!foundProcess || process == null)
        {
            return false;
        }

        // Open the process handle
        var accessRights = Kernel32.PROCESS_VM_OPERATION | Kernel32.PROCESS_VM_READ | Kernel32.PROCESS_VM_WRITE;
        var processId = process?.Id ?? 0;
        var pHandle = Kernel32.OpenProcess(accessRights, true, processId);
        if (pHandle == IntPtr.Zero)
        {
            return false;
        }

        // Get address to patch
        var address = IntPtr.Zero;
        var foundAddress = SpinWait.SpinUntil(() =>
        {
            // Read memory at the given offsets
            var bytesRead = new byte[originalMemory.Length];
            foreach (var offset in possibleOffsets)
            {
                // Get address from offset
                var possibleAddress = IntPtr.Add(process?.MainModule?.BaseAddress ?? 0, offset);
                if (possibleAddress == offset)
                {
                    // Process may have closed, exit to trigger null check
                    return true;
                }

                if (Kernel32.ReadProcessMemory(pHandle, possibleAddress, bytesRead, (UIntPtr)bytesRead.Length, IntPtr.Zero))
                {
                    // Check if the instructions have been loaded into memory
                    if (bytesRead.SequenceEqual(originalMemory))
                    {
                        address = possibleAddress;
                        return true;
                    }
                }
            }

            return false;
        }, 120000);

        if (!foundAddress || address == IntPtr.Zero)
        {
            Kernel32.CloseHandle(pHandle);
            return false;
        }

        // Write the patch to memory and return
        var wroteMemory = Kernel32.WriteProcessMemory(pHandle, address, memoryToWrite, (UIntPtr)memoryToWrite.Length, IntPtr.Zero);
        Kernel32.CloseHandle(pHandle);
        return wroteMemory;
    }

    // Main function
    static async Task<int> Main(string[] args)
    {
        // Enable console output for WinExe
        Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);

        Console.WriteLine("\nDOOMLauncher v3.0 by powerball253");

        // Launch DOOM with the passed arguments
        var startInfo = new ProcessStartInfo();
        startInfo.UseShellExecute = true;
        startInfo.FileName = ("steam://run/379720//" + String.Join(" ", args));
        var doom = Process.Start(startInfo);
        if (doom == null)
        {
            Console.WriteLine("Failed to open DOOM.");
            return 1;
        }
        else {
            Console.WriteLine("Opened DOOM through Steam.");
        }

        // Patch values
        var originalMemory = new byte[] { 0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x56 };
        var memoryToWrite = new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3, 0x90 }; // mov eax, 1; ret; nop

        // Possible offsets for the patch (currently one for the 2018 update and one for the 2024 update)
        var openGLOffsets = new int[] { 0x17FE710, 0x169CAE0 };
        var vulkanOffsets = new int[] { 0x180BDE0, 0x169BC60 };

        // Patch OpenGL process
        var openGlTask = Task.Run(() => {
            if (PatchProcessMemory("DOOMx64", openGLOffsets, originalMemory, memoryToWrite))
            {
                Console.WriteLine("Successfully patched the OpenGL process.");
                return true;
            }
            else
            {
                Console.WriteLine("Failed to patch the OpenGL process.");
                return false;
            }
        });

        // Patch Vulkan process
        var vulkanTask = Task.Run(() => {
            if (PatchProcessMemory("DOOMx64vk", vulkanOffsets, originalMemory, memoryToWrite))
            {
                Console.WriteLine("Successfully patched the Vulkan process.");
                return true;
            }
            else
            {
                Console.WriteLine("Failed to patch the Vulkan process.");
                return false;
            }
        });

        // Await patching results
        var openGlResult = await openGlTask;
        var vulkanResult = await vulkanTask;

        // Free console
        Kernel32.FreeConsole();

        // Exit
        if (openGlResult || vulkanResult)
        {
            return 0;
        }
        else
        {
            return 1;
        }
    }
}
