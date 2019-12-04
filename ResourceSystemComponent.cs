using UnityEngine;

namespace BaiResourceSystem
{
    public class ResourceSystemComponent
    {
        public static string AssetBundle_TargetDirectory_Path = Application.streamingAssetsPath + "/" + "ABFiles";
        public static string AssetBundleDependenciesPath = Application.streamingAssetsPath + "/Dependencies";
        public static string AssetBundleDependenciesName = "AssetBundles";

        public static string AssetBundleVariantName = "ab";
    }
}