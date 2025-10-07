using System;
using System.IO;
using System.Reflection;

namespace RiftManager.Utils
{
    public static class EmbeddedResourceManager
    {
        public static void ExtractDirectory(string resourceFolderPrefix, string targetDirectory, DirectoriesCreator directoriesCreator)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Ensure the prefix ends with the directory separator for correct matching
            if (!resourceFolderPrefix.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                resourceFolderPrefix += Path.DirectorySeparatorChar;
            }

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.StartsWith(resourceFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string targetPath = Path.Combine(targetDirectory, resourceName);
                    
                    string targetDir = Path.GetDirectoryName(targetPath);
                    directoriesCreator.EnsureDirectoryExists(targetDir);

                    using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (resourceStream == null) continue;

                        using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                        {
                            resourceStream.CopyTo(fileStream);
                        }
                    }
                }
            }
        }
    }
}
