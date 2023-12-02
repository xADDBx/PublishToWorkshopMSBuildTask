using Microsoft.Build.Framework;
using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.Configuration.Assemblies;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PublishToWorkshop {
    public class PublishToWorkshop : Microsoft.Build.Utilities.Task {
        public AppId AppId = 2186680;
        [Required]
        public string PathToManifest { get; set; }
        [Required]
        public string ImageDir { get; set; }
        [Required]
        public string BuildDir { get; set; }
        public int GameAppId { get; set; }

        public override bool Execute() {
            try {
                if (!new FileInfo(PathToManifest).Exists) {
                    throw new FileNotFoundException($"Can't find Manifest file at: {PathToManifest}");
                }
                if (!new DirectoryInfo(ImageDir).Exists) {
                    throw new FileNotFoundException($"Can't find directory containing image at: {ImageDir}");
                }
                if (!new DirectoryInfo(BuildDir).Exists) {
                    throw new DirectoryNotFoundException($"Can't find Directory with build artifacts at: {BuildDir}");
                }
                if (GameAppId > 0) {
                    AppId = GameAppId;
                }
                var modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(PathToManifest));
                if (modInfo != null) {
                    SteamClient.Init(AppId);
                    publishMod(PathToManifest, ImageDir, BuildDir, modInfo).GetAwaiter().GetResult();
                    SteamClient.Shutdown();
                } else {
                    throw new Exception("Deserialization of ManifestFile resulted in null");
                }
            } catch (Exception ex) {
                Log.LogError(ex.ToString());
                return false;
            }
            return true;
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
                          .WithTitle(modInfo.DisplayName ?? "")
                          .WithDescription(modInfo.Description ?? "")
                          .WithContent(di)
                          .WithPreviewFile(Path.Combine(PathToImage, modInfo.ImageName)).SubmitAsync();
            } else {
                result = await Editor.NewCommunityFile
                          .WithTitle(modInfo.DisplayName ?? "")
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