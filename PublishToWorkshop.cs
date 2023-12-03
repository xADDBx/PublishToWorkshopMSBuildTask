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
            bool result = true;
            try {
                if (!new FileInfo(PathToManifest).Exists) {
                    Log.LogError($"Can't find Manifest file at: {PathToManifest}");
                    return false;
                }
                if (!new DirectoryInfo(ImageDir).Exists) {
                    Log.LogError($"Can't find directory containing image at: {ImageDir}");
                    return false;
                }
                if (!new DirectoryInfo(BuildDir).Exists) {
                    Log.LogError($"Can't find Directory with build artifacts at: {BuildDir}");
                    return false;
                }
                if (GameAppId > 0) {
                    AppId = GameAppId;
                }
                var modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(PathToManifest));
                if (modInfo != null) {
                    SteamClient.Init(AppId);
                    result = publishMod(PathToManifest, ImageDir, BuildDir, modInfo).GetAwaiter().GetResult();
                    SteamClient.Shutdown();
                } else {
                    Log.LogError("Deserialization of ManifestFile resulted in null");
                    return false;
                }
            } catch (Exception ex) {
                Log.LogError(ex.ToString());
                result = false;
            }
            return result;
        }

        public async Task<bool> publishMod(string PathToManifest, string PathToImage, string PathToBuildFiles, OwlcatTemplateClass modInfo) {
            PublishResult result;
            var uniqueID = modInfo.UniqueName.Replace(' ', '-');
            var tmpDirPath = Path.Combine(PathToBuildFiles, @"..\temp\");
            var di = new DirectoryInfo(tmpDirPath);
            try {
                di.Create();
            } catch {
                Log.LogError($"Exception while trying to create temp directory: {tmpDirPath}.");
                return false;
            }
            bool ret = true;
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
                    Log.LogError("Encountered exception while trying to add WorkshopId for newly published mod.");
                    Log.LogError(ex.ToString());
                    ret = false;
                }
            }
            if (result.Result != Result.OK) {
                Log.LogError($"Steam Workshop Update Failed with result: {result.Result}");
                ret = false;
            }
            di.Delete(true);
            return ret;
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