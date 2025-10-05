// RiftManager.Utils/FileSystemHelper.cs
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq; // Needed for .Any() or similar if used later, though not explicitly in current snippet.
using RiftManager.Services;

namespace RiftManager.Utils
{
    public static class FileSystemHelper
    {
        /// <summary>
        /// Recursively removes empty directories from the given directory.
        /// If the directory itself is empty, it is also removed.
        /// </summary>
        /// <param name="directory">Path to the directory to clean up</param>
        /// <param name="logService">The LogService instance for logging.</param>
        /// <param name="basePathForLogging">Optional: The base path to make log entries relative to.</param>
        public static async Task RemoveEmptyDirectories(string directory, LogService logService, string? basePathForLogging = null)
        {
            // Removed explicit null check for logService, relying on ?. operator.
            // In a well-configured DI environment, logService should not be null.

            var directoryInfo = new DirectoryInfo(directory);
            if (!directoryInfo.Exists || !directoryInfo.Attributes.HasFlag(FileAttributes.Directory))
            {
                return; // Nothing to do if directory doesn't exist or isn't a directory
            }

            try
            {
                var subDirectories = Directory.GetDirectories(directory); // Get only subdirectories for recursion
                var recursiveRemovalTasks = new Task[subDirectories.Length];

                for (int i = 0; i < subDirectories.Length; i++)
                {
                    // Pass basePathForLogging in the recursion!
                    recursiveRemovalTasks[i] = RemoveEmptyDirectories(subDirectories[i], logService, basePathForLogging);
                }

                await Task.WhenAll(recursiveRemovalTasks);

                // After children are processed, check if this directory is empty.
                // We need to re-check GetFileSystemEntries as child directories might have been removed.
                var currentDirEntries = Directory.GetFileSystemEntries(directory);
                
                if (currentDirEntries.Length == 0)
                {
                    string pathToShowInLog = directory;
                    // Make log path relative if basePathForLogging is provided
                    if (!string.IsNullOrEmpty(basePathForLogging) && directory.StartsWith(basePathForLogging, StringComparison.OrdinalIgnoreCase))
                    {
                        pathToShowInLog = directory.Substring(basePathForLogging.Length);
                        if (pathToShowInLog.StartsWith(Path.DirectorySeparatorChar.ToString()) || pathToShowInLog.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                        {
                            pathToShowInLog = pathToShowInLog.Substring(1); // Remove leading separator if exists
                        }
                    }
                    
                    logService?.Log($"FileSystemHelper: Removing empty directory: {pathToShowInLog}");
                    Directory.Delete(directory);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                logService?.LogError($"FileSystemHelper: Access denied when trying to remove directory '{directory}': {ex.Message}");
            }
            catch (IOException ex) when (ex.Message.Contains("The directory is not empty"))
            {
                // This might happen if another process creates a file in between checks, or if GetFileSystemEntries misses something
                logService?.LogDebug($"FileSystemHelper: Directory '{directory}' was not empty when attempting to remove. Skipping.");
            }
            catch (Exception ex)
            {
                logService?.LogError($"FileSystemHelper: An unexpected error occurred while processing directory '{directory}': {ex.Message}");
            }
        }
    }
}