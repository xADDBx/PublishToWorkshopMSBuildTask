using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;

namespace PublishToRT {
    public static class Program {
        public const uint RogueTraderAppId = 2186680;
        public static void Main(string[] args) {
            string PathToManifest = Environment.GetEnvironmentVariable("PathToManifest");
            string DirectoryContainingImage = Environment.GetEnvironmentVariable("ImageDir");
            string PathToBuildFiles = Environment.GetEnvironmentVariable("BuildDir");
            if (!new FileInfo(PathToManifest).Exists) {
                throw new FileNotFoundException($"Can't find Manifest file at: {PathToManifest}");
            }
            if (!new DirectoryInfo(DirectoryContainingImage).Exists) {
                throw new FileNotFoundException($"Can't find directory containing image at: {DirectoryContainingImage}");
            }
            if (!new DirectoryInfo(PathToBuildFiles).Exists) {
                throw new DirectoryNotFoundException($"Can't find Directory with build artifacts at: {PathToBuildFiles}");
            }
            var modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(PathToManifest));
            if (modInfo != null) {
                SteamClient.Init(RogueTraderAppId);
                publishMod(PathToManifest, DirectoryContainingImage, PathToBuildFiles, modInfo).GetAwaiter().GetResult();
                SteamClient.Shutdown();
            } else {
                throw new Exception("Deserialization of ManifestFile resulted in null");
            }
        }
        public static async Task publishMod(string PathToManifest, string PathToImage, string PathToBuildFiles, OwlcatTemplateClass modInfo) {
            PublishResult result;
            var uniqueID = modInfo.UniqueName.Replace(' ', '-');
            var tmpDirPath = Path.Combine(PathToBuildFiles, @"..\temp\");
            var di = new DirectoryInfo(tmpDirPath);
            try {
                di.Create();
            } catch {
                throw new IOException($"Exception while trying to create temp directory: {tmpDirPath}");
            }
            ZipFile.CreateFromDirectory(PathToBuildFiles, Path.Combine(tmpDirPath, $"{uniqueID}.zip"));
            if (ulong.TryParse(modInfo.WorkshopId, out var modID)) {
                var id = new PublishedFileId();
                id.Value = modID;
                result = await new Editor(id)
                          .WithTitle(modInfo.UniqueName ?? "")
                          .WithDescription(modInfo.Description ?? "")
                          .WithContent(di)
                          .WithPreviewFile(Path.Combine(PathToImage, modInfo.ImageName)).SubmitAsync();
            } else {
                result = await Editor.NewCommunityFile
                          .WithTitle(modInfo.UniqueName ?? "")
                          .WithDescription(modInfo.Description ?? "")
                          .WithContent(di)
                          .WithPreviewFile(Path.Combine(PathToImage, modInfo.ImageName)).SubmitAsync();
                try {
                    modInfo.WorkshopId = result.FileId.Value.ToString();
                    File.WriteAllText(PathToManifest, JsonConvert.SerializeObject(modInfo, Formatting.Indented));
                } catch (Exception ex) {
                    Console.WriteLine("Encountered exception while trying to add WorkshopId for newly published mod.");
                    Console.WriteLine(ex.ToString());
                }
            }
            Console.WriteLine(result.Result.ToString());
            di.Delete(true);
        }
        public class OwlcatTemplateClass {
            public string UniqueName;
            public string Version;
            public string DisplayName;
            public string Description;
            public string Author;
            public string ImageName;
            public string WorkshopId;
            public string Repository;
            public string HomePage;
            public IEnumerable<IDictionary<string, string>> Dependencies;
        }
    }
}