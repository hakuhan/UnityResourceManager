using System.Collections.Generic;

namespace BaiResourceSystem
{
    public class ResourceSystemUtil
    {
        public static string GetBundleNameWithOutPrefix(string name)
        {
            return name.Remove(name.Length - ResourceSystemComponent.AssetBundleVariantName.Length - 1);
        }
    }
}