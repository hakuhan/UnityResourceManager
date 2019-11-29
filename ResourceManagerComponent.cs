using UnityEngine;

namespace BaiResourceManager
{
    public class ResourceManagerComponent
    {
        public static string AssetBundle_TargetDirectory_Path = Application.streamingAssetsPath + "/" + "ABFiles";
        public static string AssetBundleDependenciesPath = Application.streamingAssetsPath + "/Dependencies";
        public static string AssetBundleDependenciesName = "AssetBundles";

        public static string AssetBundleVariantName = "ab";
    }
}