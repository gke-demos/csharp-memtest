// Program.cs
// This file contains the main logic for the C# cgroup memory monitoring application.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions; // Not strictly needed for this version, but often useful for parsing
using System.Threading;

public class CSharpMemTest
{
    // Constants for cgroup paths and filenames
    private const string CgroupBasePath = "/sys/fs/cgroup";
    private const string CgroupProcfsPath = "/proc/self/cgroup";
    private const string CgroupMemoryMaxFilename = "memory.max";

    /// <summary>
    /// Gets the cgroupv2 path for the current process's memory.max file.
    /// This function reads from /proc/self/cgroup to find the relative path
    /// of the current process within the cgroup hierarchy (specifically cgroupv2,
    /// identified by entries starting with "0::"). It then constructs the full
    /// path to the memory.max file under /sys/fs/cgroup.
    /// </summary>
    /// <returns>The full path to memory.max, or null if not found or an error occurs.</returns>
    private static string GetCgroupPath()
    {
        Console.WriteLine($"Reading cgroup info from {CgroupProcfsPath}");
        string cgroupRelativePath = null;
        try
        {
            // Read all lines from the /proc/self/cgroup file
            string[] lines = File.ReadAllLines(CgroupProcfsPath);
            foreach (string line in lines)
            {
                // cgroupv2 entries typically start with "0::"
                // Example: 0::/user.slice/user-1000.slice/session-2.scope
                if (line.StartsWith("0::"))
                {
                    // Extract the relative path after "0::" and trim any whitespace
                    cgroupRelativePath = line.Substring(3).Trim();
                    break; // Found the cgroupv2 entry, no need to read further
                }
            }
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"Error: {CgroupProcfsPath} not found. This usually means cgroupfs is not mounted or not accessible.");
            return null;
        }
        catch (Exception ex)
        {
            // Catch any other exceptions during file reading
            Console.Error.WriteLine($"Error reading {CgroupProcfsPath}: {ex.Message}");
            return null;
        }

        if (cgroupRelativePath == null)
        {
            Console.Error.WriteLine($"Could not find cgroupv2 entry in {CgroupProcfsPath}. Ensure cgroupv2 is enabled and in use.");
            return null;
        }

        // Construct the full path to memory.max file
        // Path.Combine correctly handles path separators for the operating system
        string fullPath = Path.Combine(CgroupBasePath, cgroupRelativePath.TrimStart('/'), CgroupMemoryMaxFilename);

        // Verify that the constructed path actually exists.
        // In C#, File.Exists implicitly checks if the process has access permissions
        // to the file when attempting to read it later.
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Error: Cgroup memory.max file '{fullPath}' does not exist.");
            Console.Error.WriteLine("Ensure your process has read permissions and the path is correct.");
            return null;
        }

        return fullPath;
    }

    /// <summary>
    /// Reads the memory limit from the specified cgroup memory.max file.
    /// This function opens the memory.max file, reads its content, and attempts
    /// to parse it as a long integer. If the content is "max", it returns -1
    /// to signify an unlimited memory.
    /// </summary>
    /// <param name="cgroupPath">The full path to the memory.max file.</param>
    /// <returns>The memory limit in bytes, or -1 if "max" or a parsing/read error occurs.</returns>
    private static long GetMemoryLimit(string cgroupPath)
    {
        try
        {
            // Read all content from the file and trim any leading/trailing whitespace
            string content = File.ReadAllText(cgroupPath).Trim();
            if (content.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                return -1; // Indicate no specific limit (corresponds to 'max' in cgroup)
            }
            // Attempt to parse the content as a long integer
            if (long.TryParse(content, out long limit))
            {
                return limit;
            }
            else
            {
                // Log an error if parsing fails
                Console.Error.WriteLine($"Error parsing memory limit from '{cgroupPath}': '{content}' is not a valid number.");
                return -1; // Indicate a parsing error
            }
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"Error: Cgroup memory.max file '{cgroupPath}' not found.");
            return -1;
        }
        catch (Exception ex)
        {
            // Catch any other exceptions during file reading
            Console.Error.WriteLine($"Error reading from {cgroupPath}: {ex.Message}");
            return -1;
        }
    }

    private static void PrintTotalMemory()
    {
        // GC.GetTotalMemory(true) forces a garbage collection before
        // calculating the total allocated memory, providing a more accurate
        // current memory usage. If set to false, it might return a less
        // up-to-date value.
        long totalMemoryBytes = GC.GetTotalMemory(true);

        Console.WriteLine($"Total memory currently allocated: {totalMemoryBytes} bytes");
    }

     /// <summary>
    /// Retrieves the value of the GCHeapHardLimit configuration variable.
    /// </summary>
    public static void PrintGCHeapHardLimit()
    {
        try
        {
            // Get all GC configuration variables as a dictionary.
            IReadOnlyDictionary<string, object> gcConfig = GC.GetConfigurationVariables();

            // Check if the dictionary contains the "GCHeapHardLimit" key.
            if (gcConfig.TryGetValue("GCHeapHardLimit", out object hardLimitValue))
            {
                // Attempt to cast the value to a long.
                // The value is typically returned as a long representing bytes.
                if (hardLimitValue is long longValue)
                {
                    Console.WriteLine($"GCHeapHardLimit found: {longValue} bytes");
                }
                else
                {
                    Console.WriteLine($"GCHeapHardLimit found, but type was unexpected: {hardLimitValue.GetType().Name}");
                }
            }
            else
            {
                Console.WriteLine("GCHeapHardLimit configuration variable not found.");
                // You can enumerate all available keys for debugging:
                // Console.WriteLine("Available GC configuration variables:");
                // foreach (var entry in gcConfig)
                // {
                //     Console.WriteLine($"- {entry.Key}: {entry.Value} ({entry.Value.GetType().Name})");
                // }
            }
        }
        catch (Exception ex)
        {
            // Catch any exceptions that might occur during the retrieval process.
            Console.WriteLine($"An error occurred while trying to get GCHeapHardLimit: {ex.Message}");
        }
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("Starting cgroupv2 memory limit monitoring application.");
        PrintTotalMemory();
        PrintGCHeapHardLimit();

        // Step 1: Get the cgroupv2 memory.max file path
        string cgroupMemoryMaxPath = GetCgroupPath();
        if (cgroupMemoryMaxPath == null)
        {
            Console.Error.WriteLine("Failed to determine cgroupv2 memory.max path. Exiting.");
            return; // Exit if the path cannot be determined
        }
        Console.WriteLine($"Monitoring cgroupv2 memory limit at: {cgroupMemoryMaxPath}");

        // Step 2: Get the initial memory limit
        long initialMemoryLimitBytes = GetMemoryLimit(cgroupMemoryMaxPath);
        if (initialMemoryLimitBytes == -1)
        {
            Console.WriteLine("Initial memory limit: No specific limit ('max') or error reading.");
            Console.WriteLine("Cannot allocate 90% of 'max'. Exiting.");
            return; // Cannot proceed without a concrete limit to work with
        }
        else if (initialMemoryLimitBytes == 0)
        {
            Console.WriteLine("Initial memory limit is 0 bytes. Cannot allocate memory. Exiting.");
            return;
        }
        else
        {
            Console.WriteLine($"Initial memory limit: {initialMemoryLimitBytes} bytes ({(double)initialMemoryLimitBytes / (1024 * 1024):F2} MB)");
        }

        // use a list to hold byte arrays since they may be dynamically added if memory increases
        List<byte[]> data = new List<byte[]>();
        byte[] allocatedMemory = null;
        // Calculate 70% of the limit as the GCHeapHardLimit is 75% of available memory
        long allocationSize = (long)(initialMemoryLimitBytes * 0.70);  

        if (allocationSize == 0)
        {
            Console.Error.WriteLine("Calculated allocation size is 0 bytes. Exiting.");
            return;
        }

        try
        {
            // Step 3: Allocate 70% of the initial limit
            Console.WriteLine($"Attempting to allocate {allocationSize} bytes ({(double)allocationSize / (1024 * 1024):F2} MB) for 70% of the limit...");
            allocatedMemory = new byte[allocationSize]; // Allocate memory using a byte array
            Console.WriteLine($"Successfully allocated {allocationSize} bytes.");

            // Fill the allocated memory to ensure pages are touched and resident.
            // This is analogous to writing to the memory in the C example to make it "hot".
            Console.WriteLine("Filling allocated memory with data...");
            for (long i = 0; i < allocationSize; i++)
            {
                // Fill with a simple pattern. The cast to byte is safe due to modulo.
                allocatedMemory[i] = (byte)(i % 256);
            }
            Console.WriteLine("Memory filling complete.");
            // add allocated memory to our data list
            data.Add(allocatedMemory);
            PrintTotalMemory();
        }
        catch (OutOfMemoryException ex)
        {
            // Catch specific OutOfMemoryException if the allocation fails
            Console.Error.WriteLine($"Failed to allocate memory: {ex.Message}. This might indicate the system itself is out of memory or the requested allocation is too large for the process's address space.");
            return;
        }
        catch (Exception ex)
        {
            // Catch any other unexpected errors during initial allocation
            Console.Error.WriteLine($"An unexpected error occurred during initial memory allocation: {ex.Message}");
            return;
        }

        // Step 4: Monitor for changes every 1 second in an infinite loop
        while (true)
        {
            Thread.Sleep(1000); // Wait for 1 second
            PrintTotalMemory();
            PrintGCHeapHardLimit();

            long currentMemoryLimitBytes = GetMemoryLimit(cgroupMemoryMaxPath);
            DateTime now = DateTime.Now; // Get current time for logging

            if (currentMemoryLimitBytes == -1)
            {
                Console.WriteLine($"[{now:HH:mm:ss}] Current memory limit: No specific limit ('max') or error reading.");
                if (initialMemoryLimitBytes != -1)
                {
                    // Warn if the limit previously had a value and now is 'max' or unreadable
                    Console.WriteLine("WARNING: Limit changed from a specific value to 'max' or an error occurred while reading.");
                }
            }
            else if (currentMemoryLimitBytes == initialMemoryLimitBytes)
            {
                // If the limit hasn't changed, log that.
                Console.WriteLine($"[{now:HH:mm:ss}] Current memory limit: {currentMemoryLimitBytes} bytes ({(double)currentMemoryLimitBytes / (1024 * 1024):F2} MB) - No change.");
            }
            else
            {
                // If the limit has changed, log the change and reallocate memory.
                Console.WriteLine($"[{now:HH:mm:ss}] Current memory limit: {currentMemoryLimitBytes} bytes ({(double)currentMemoryLimitBytes / (1024 * 1024):F2} MB) - CHANGED from {initialMemoryLimitBytes} bytes ({(double)initialMemoryLimitBytes / (1024 * 1024):F2} MB)!");

                // refresh the GC memory limit to be able to take advantage of our newly found memory!
                try
                {
                    Console.WriteLine("Refreshing Memory Limit");
                    GC.RefreshMemoryLimit();
                    PrintTotalMemory();
                    PrintGCHeapHardLimit();
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Failed to refresh memory limits: {ex.Message}.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"An unexpected error while refreshing memory limit: {ex.Message}");
                }
                
                long newAllocationSize = (long)((currentMemoryLimitBytes - initialMemoryLimitBytes) * 0.70); // Calculate new allocation size

                if (newAllocationSize == 0)
                {
                    Console.WriteLine($"New calculated allocation size is 0 bytes. Releasing previous memory and not allocating new.");
                    // Let the Garbage Collector reclaim the previously allocated memory
                    allocatedMemory = null;
                }
                else
                {
                    try
                    {
                        Console.WriteLine($"Allocating new memory for {newAllocationSize} bytes ({(double)newAllocationSize / (1024 * 1024):F2} MB)...");
                        byte[] newAllocatedMemory = new byte[newAllocationSize];
                        Console.WriteLine($"Successfully allocated {newAllocationSize} bytes.");

                        Console.WriteLine("Filling newly allocated memory with data...");
                        for (long i = 0; i < newAllocationSize; i++)
                        {
                            newAllocatedMemory[i] = (byte)(i % 256);
                        }
                        Console.WriteLine("Memory filling complete.");
                        // add allocated memory to our data list
                        data.Add(newAllocatedMemory);
                    }
                    catch (OutOfMemoryException ex)
                    {
                        Console.Error.WriteLine($"Failed to reallocate memory to {newAllocationSize} bytes: {ex.Message}. Keeping previous allocation if possible.");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"An unexpected error occurred during memory reallocation: {ex.Message}");
                    }
                }

                // Update the 'initial' values for future comparisons in the loop
                initialMemoryLimitBytes = currentMemoryLimitBytes;
                allocationSize = newAllocationSize;
            }
        }
    }
}
