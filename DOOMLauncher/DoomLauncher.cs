using System;
using System.Diagnostics;
using System.Threading;
using Memory;

namespace DoomLauncher
{
    class DoomLauncher
    {
        // Memory object for DOOM process memory handling
        private static Mem Memory = new Mem();

        // Main entrypoint
        private static int Main(string[] args)
        {
            // Launch doom with the passed arguments
            string launchArgs = String.Join(" ", args);
            var doom = Process.Start("DOOMx64vk.exe", launchArgs);

            if (doom == null)
            {
                Console.WriteLine("Failed to open the DOOM process!");
                return 1;
            }

            // Wait for the original Steam process to exit
            doom.WaitForExit();

            // Wait for the DOOM process to be re-opened
            int pId = 0;
            SpinWait.SpinUntil(() => (pId = Memory.GetProcIdFromName("DOOMx64vk")) != 0);

            if (Memory.OpenProcess(pId))
            {
                // Wait until the memory is loaded
                SpinWait.SpinUntil(() =>
                {
                    var bytesRead = Memory.ReadBytes("DOOMx64vk.exe+18a31d0", 7);

                    if (bytesRead == null)
                    {
                        return false;
                    }

                    return BitConverter.ToString(bytesRead) == "0F-B6-81-89-4C-03-00";
                });

                // Apply the patch and close the process
                Memory.WriteMemory("DOOMx64vk.exe+18a31d0", "bytes", "31 C0 90 90 90 90 90");
                Memory.CloseProcess();

                Console.WriteLine("DOOM process has been patched successfully.");
                return 0;
            }
            else
            {
                Console.WriteLine("Failed to patch the DOOM process!");
                return 1;
            }
        }
    }
}