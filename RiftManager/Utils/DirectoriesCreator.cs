using System;
using System.IO;

namespace RiftManager.Utils
{
    public class DirectoriesCreator
    {
        public string AppDataFolder { get; }
        public string LogFolder { get; }
        public string AssetsFolder { get; }

        public DirectoriesCreator()
        {
            AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RiftManager");
            LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            AssetsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

            EnsureDirectoryExists(AppDataFolder);
            EnsureDirectoryExists(LogFolder);
            EnsureDirectoryExists(AssetsFolder);
        }

        public string GetManifestsDownloadDirectory()
        {
            string path = Path.Combine(AssetsFolder, "RiotClientAssets");
            EnsureDirectoryExists(path);
            return path;
        }

        public string GetEventAssetsDirectory(string eventId)
        {
            string path = Path.Combine(AssetsFolder, eventId);
            EnsureDirectoryExists(path);
            return path;
        }

        public string GetManifestsGameSubdirectory(string game)
        {
            string path = Path.Combine(GetManifestsDownloadDirectory(), game);
            EnsureDirectoryExists(path);
            return path;
        }

        public string CreateTemporaryDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "RiftManager", Guid.NewGuid().ToString());
            EnsureDirectoryExists(tempDir);
            return tempDir;
        }

        public void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
