using Microsoft.Build.Framework;
using Newtonsoft.Json;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Configuration.Assemblies;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PublishToRTWorkshop {
    public class RTPublishTask : Microsoft.Build.Utilities.Task {
        public static readonly AppId_t RogueTraderAppId = new(2186680);
        [Required]
        public string PathToManifest { get; set; }
        [Required]
        public string ImageDir { get; set; }
        [Required]
        public string BuildDir { get; set; }
        [Required]
        public string PathToGameFiles { get; set; }

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
                var managedPath = Path.Combine(PathToGameFiles, "WH40KRT_Data", "Managed");
                var managedDir = new DirectoryInfo(managedPath);
                if (!managedDir.Exists) {
                    throw new DirectoryNotFoundException($"Can't find managed game directory at: {managedPath}");
                }
                var modInfo = JsonConvert.DeserializeObject<OwlcatTemplateClass>(File.ReadAllText(PathToManifest));
                if (modInfo != null) {
                    if (!SteamAPI.Init()) {
                        throw new Exception("SteamAPI.Init returned false");
                    }
                    publishMod(PathToManifest, ImageDir, BuildDir, modInfo).GetAwaiter().GetResult();
                    itemCreated.Dispose();
                    itemUpdate.Dispose();
                    SteamAPI.Shutdown();
                } else {
                    throw new Exception("Deserialization of ManifestFile resulted in null");
                }
            } catch (Exception ex) {
                Log.LogError(ex.ToString());
                return false;
            }
            return true;
        }

        public Callback<CreateItemResult_t> itemCreated;
        public CallResult<SubmitItemUpdateResult_t> itemUpdate;
        public PublishedFileId_t modId = default;
        public bool hasQueryResult = false;
        public bool finishedUpdate = false;
        public void OnItemCreated(CreateItemResult_t pCallback) {
            if (pCallback.m_eResult != EResult.k_EResultOK) {
                Log.LogError($"Error while trying to create new Workshop item: {pCallback.m_eResult}");
            } else {
                modId = pCallback.m_nPublishedFileId;
            }
            finishedUpdate = true;
        }
        public void OnItemUpdated(SubmitItemUpdateResult_t pCallback, bool bIOFailure) {
            if (pCallback.m_eResult != EResult.k_EResultOK) {
                Log.LogError($"Error while trying to create update Workshop item: {pCallback.m_eResult}");
            }
            hasQueryResult = true;
        }
        public async Task publishMod(string PathToManifest, string PathToImage, string PathToBuildFiles, OwlcatTemplateClass modInfo) {
            itemCreated = Callback<CreateItemResult_t>.Create(OnItemCreated);
            itemUpdate = CallResult<SubmitItemUpdateResult_t>.Create(OnItemUpdated);
            var uniqueID = modInfo.UniqueName.Replace(' ', '-');
            var tmpDirPath = Path.Combine(PathToBuildFiles, @"..\temp\");
            var di = new DirectoryInfo(tmpDirPath);
            try {
                di.Create();
            } catch {
                throw new IOException($"Exception while trying to create temp directory: {tmpDirPath}");
            }
            ZipFile.CreateFromDirectory(PathToBuildFiles, Path.Combine(tmpDirPath, $"{uniqueID}.zip"));
            if (!ulong.TryParse(modInfo.WorkshopId, out var id)) {
                var api_call = SteamUGC.CreateItem(RogueTraderAppId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 5) {
                    Thread.Sleep(50);
                    SteamAPI.RunCallbacks();
                    if (hasQueryResult) break;
                }
                stopwatch.Stop();
                if (!hasQueryResult) {
                    Log.LogError("Did not receive Callback within 5 seconds for creating new Steam item.");
                    throw new Exception("Steam not responding");
                } else {
                    if (modId == default) {
                        throw new Exception("Failed to create new Workshop item");
                    }
                }
                try {
                    modInfo.WorkshopId = modId.m_PublishedFileId.ToString();
                    File.WriteAllText(PathToManifest, JsonConvert.SerializeObject(modInfo, Formatting.Indented));
                } catch (Exception ex) {
                    Log.LogError("Encountered exception while trying to add WorkshopId for newly published mod.");
                    Log.LogError(ex.ToString());
                }
            } else {
                modId.m_PublishedFileId = id;
            }
            var update = SteamUGC.StartItemUpdate(RogueTraderAppId, modId);
            if (SteamUGC.SetItemTitle(update, modInfo.UniqueName) &&
            SteamUGC.SetItemDescription(update, modInfo.Description) &&
            SteamUGC.SetItemContent(update, di.FullName) &&
            SteamUGC.SetItemPreview(update, Path.Combine(PathToImage, modInfo.ImageName)) == true) {
                var callResult = SteamUGC.SubmitItemUpdate(update, "Updated Item");
                itemUpdate.Set(callResult);
                Log.LogMessage("Successfully started item update.");
            } else {
                Log.LogError("Invalid UGCUpdateHandle_t used for updating the workshop mod.");
            }
            while (!finishedUpdate) {
                Thread.Sleep(200);
                SteamAPI.RunCallbacks();
                var cur = SteamUGC.GetItemUpdateProgress(update, out var processed, out var total);
                Log.LogMessage($"Doing: {cur}. Processed {processed} of {total} bytes.");
            }
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