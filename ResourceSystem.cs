using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UObject = UnityEngine.Object;
using UnityEngine.Networking;
using UniRx.Async;
using BaiSingleton;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace BaiResourceSystem
{
    public class ResourceSystem : MonoSingleton<ResourceSystem>
    {
#if SYNC_LOAD_BUNDLE
        private string[] m_Variants = { };
        private AssetBundleManifest manifest;
        private AssetBundle shared, assetbundle;
        private Dictionary<string, AssetBundle> bundles;

#endif
        private Dictionary<string, AssetBundle> m_bundles;

        /// <summary>
        /// 正在加载的bundle
        /// </summary>
        private List<string> m_loadingBundles = new List<string>();

        private Hashtable ht = null; //Resource.load中容器键值对集合

        private List<UniTask> m_loadBundleCoroutines;

        AssetBundleManifest m_manifest;

        // Asset bundle name dictionary
        Dictionary<string, string> m_assetWithBundle;
        void Awake()
        {
            ht = new Hashtable();
            m_bundles = new Dictionary<string, AssetBundle>();
            m_loadBundleCoroutines = new List<UniTask>();
        }
#if UNITY_EDITOR
        /// <summary>
        /// Build Assets bundle
        /// </summary>
        [MenuItem("Tools/Asset Bundle/BuildAssetsBundle")]
        static void BuildAssetsBundle()
        {
            // Bundle path
            string _path = "Assets/AssetBundles";

            //Application.streamingAssetsPath对应的StreamingAssets的子目录  
            DirectoryInfo AB_Temp_Directory = new DirectoryInfo(_path);
            if (!AB_Temp_Directory.Exists)
            {
                AB_Temp_Directory.Create();
            }

            FileInfo[] filesAB = AB_Temp_Directory.GetFiles();
            foreach (var item in filesAB)
            {
                Debug.Log("******删除旧文件：" + item.FullName + "******");
                item.Delete();
            }

            // Build
            BuildTarget _target = BuildTarget.StandaloneWindows64;
#if UNITY_ANDROID
            _target = BuildTarget.Android;
#elif UNITY_IOS
            _target = BuildTarget.iOS;
#endif
            AssetBundleManifest _manifest =
                BuildPipeline.BuildAssetBundles(_path, BuildAssetBundleOptions.UncompressedAssetBundle, _target);

            Debug.Log("******AssetBundle打包完成******");

            Debug.Log("将要转移的文件夹是：" + ResourceSystemComponent.AssetBundle_TargetDirectory_Path);


            // 删除StreamAsset文件
            DirectoryInfo streaming_Directory = new DirectoryInfo(ResourceSystemComponent.AssetBundle_TargetDirectory_Path);
            if (!streaming_Directory.Exists)
            {
                streaming_Directory.Create();
            }

            FileInfo[] streaming_files = streaming_Directory.GetFiles();
            foreach (var item in streaming_files)
            {
                item.Delete();
            }

            // 检查Dependencies
            DirectoryInfo _dependenciesPath = new DirectoryInfo(ResourceSystemComponent.AssetBundleDependenciesPath);
            if (!_dependenciesPath.Exists)
            {
                _dependenciesPath.Create();
            }

            FileInfo[] _dependenciesFiles = _dependenciesPath.GetFiles();
            foreach (var item in _dependenciesFiles)
            {
                item.Delete();
            }

            FileInfo[] filesAB_temp = AB_Temp_Directory.GetFiles();
            foreach (var item in filesAB_temp)
            {
                // 拷贝Dependencies到StreamAsset
                if (item.Extension == "")
                {
                    item.CopyTo(ResourceSystemComponent.AssetBundleDependenciesPath + "/" + item.Name, true);
                    FileInfo _mFile = new FileInfo(_path + "/" + item.Name + ".manifest");
                    string _mFileInfo = ResourceSystemComponent.AssetBundleDependenciesPath + "/" + item.Name + ".manifest";
                    _mFile.CopyTo(_mFileInfo, true);
                    _mFile.Delete();
                }
                else
                {
                    if (item.Name == "AssetBundles.manifest")
                        continue;

                    StringBuilder _pathDuilder = new StringBuilder(ResourceSystemComponent.AssetBundle_TargetDirectory_Path);
                    _pathDuilder.Append("/");
                    _pathDuilder.Append(item.Name);
                    item.CopyTo(_pathDuilder.ToString(), true);
                }

                item.Delete();
            }

            AB_Temp_Directory.Delete();

            AssetDatabase.Refresh();
            Debug.Log("******文件传输完成******");
        }


        private static string _dirName = "";

        [MenuItem("Tools/Asset Bundle/Set Asset Bunble With folder Name")]
        static void SetSelectedFoldersBunbleName()
        {
            UObject[] _selObj = Selection.GetFiltered(typeof(UObject), SelectionMode.DeepAssets);

            for (int i = 0; i < _selObj.Length; ++i)
            {
                string _assetsTypeName = _selObj[i].GetType().Name;
                if (_assetsTypeName != "DefaultAsset")
                {
                    string _objPath = AssetDatabase.GetAssetPath(_selObj[i]);
                    DirectoryInfo _dirInfo = new DirectoryInfo(_objPath);
                    if (_dirInfo == null)
                    {
                        Debug.LogError("******请检查，是否选中了非文件对象******");
                        return;
                    }

                    string filePath = _dirInfo.FullName.Replace('\\', '/');
                    filePath = filePath.Replace(Application.dataPath, "Assets");
                    SetObjAssetbundleName(filePath, _dirInfo.Parent.Name);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("******批量设置AssetBundle名称成功******");
        }

        /// <summary>  
        /// 批量命名所选文件夹下资源的AssetBundleName.  
        /// </summary>  
        [MenuItem("Tools/Asset Bundle/Set Asset Bundle Name")]
        static void SetSelectFileBundleName()
        {
            UObject[] selObj = Selection.GetFiltered(typeof(UObject), SelectionMode.Unfiltered);
            foreach (UObject item in selObj)
            {
                string objPath = AssetDatabase.GetAssetPath(item);
                DirectoryInfo dirInfo = new DirectoryInfo(objPath);
                if (dirInfo == null)
                {
                    Debug.LogError("******请检查，是否选中了对象******");
                    return;
                }

                _dirName = dirInfo.Name;

                string filePath = dirInfo.FullName.Replace('\\', '/');
                filePath = filePath.Replace(Application.dataPath, "Assets");
                SetObjAssetbundleName(filePath, _dirName);
            }

            AssetDatabase.Refresh();
            Debug.Log("******批量设置AssetBundle名称成功******");
        }

        static void SetAssetBundleName(DirectoryInfo dirInfo, string _name = null)
        {
            if (!dirInfo.Exists)
                return;

            if (_name != null)
                _dirName = _name;

            FileSystemInfo[] files = dirInfo.GetFileSystemInfos();
            foreach (FileSystemInfo file in files)
            {
                if (file is FileInfo && file.Extension != ".meta" && file.Extension != ".txt")
                {
                    string filePath = file.FullName.Replace('\\', '/');
                    filePath = filePath.Replace(Application.dataPath, "Assets");
                    SetObjAssetbundleName(filePath, _dirName);
                }
                else if (file is DirectoryInfo)
                {
                    string filePath = file.FullName.Replace('\\', '/');
                    filePath = filePath.Replace(Application.dataPath, "Assets");
                    SetObjAssetbundleName(filePath, _dirName);
                    SetAssetBundleName(file as DirectoryInfo);
                }
            }
        }

        /// <summary>  
        /// 批量清空所选文件夹下资源的AssetBundleName.  
        /// </summary>  
        [MenuItem("Tools/Asset Bundle/Reset Asset Bundle Name")]
        static void ResetSelectFolderFileBundleName()
        {
            UnityEngine.Object[] selObj = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Unfiltered);
            foreach (UnityEngine.Object item in selObj)
            {
                string objPath = AssetDatabase.GetAssetPath(item);
                DirectoryInfo dirInfo = new DirectoryInfo(objPath);
                if (dirInfo == null)
                {
                    Debug.LogError("******请检查，是否选中了非文件夹对象******");
                    return;
                }

                _dirName = null;

                string filePath = dirInfo.FullName.Replace('\\', '/');
                filePath = filePath.Replace(Application.dataPath, "Assets");
                SetObjAssetbundleName(filePath, _dirName);

                SetAssetBundleName(dirInfo);
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
            AssetDatabase.Refresh();
            Debug.Log("******批量清除AssetBundle名称成功******");
        }

        /// <summary>
        /// 设置asset name
        /// </summary>
        /// <param name="path"></param>
        /// <param name="name"></param>
        static void SetObjAssetbundleName(string path, string name = null)
        {
            AssetImporter ai = AssetImporter.GetAtPath(path);
            ai.assetBundleName = name;
            ai.assetBundleVariant = ResourceSystemComponent.AssetBundleVariantName;
        }

        [MenuItem("Tools/清除playerPrefab")]
        public static void ClearAllPlayerPrefabs()
        {
            PlayerPrefs.DeleteAll();
        }

#endif
        /// <summary>
        /// 异步加载文件夹下所有AssetBundle
        /// </summary>
        /// <param name="_assetPath"></param>
        async public UniTask LoadAllAssetBundle()
        {
            Debug.Log("Bundle: start load bundle");
            m_loadBundleCoroutines.Clear();
            var _manifest = GetManifest();
            foreach (var _abName in _manifest.GetAllAssetBundlesWithVariant())
            {
                // 检查是否重复加载
                if (!m_bundles.ContainsKey(_abName))
                {
                    StringBuilder _assetSb = new StringBuilder(ResourceSystemComponent.AssetBundle_TargetDirectory_Path);
                    _assetSb.Append("/");
                    _assetSb.Append(_abName);
                    m_loadBundleCoroutines.Add(LoadFileAsync(_assetSb.ToString(), _abName));
                }
            }

            await UniTask.WhenAll(m_loadBundleCoroutines.ToArray());
        }

        async UniTask LoadFileAsync(string _fullSBNameWithPath, string _sbName)
        {
            if (!m_loadingBundles.Contains(_sbName))
            {
                m_loadingBundles.Add(_sbName);

                AssetBundleManifest manifest = GetManifest();
                if (manifest != null)
                {
                    // Get dependecies from the AssetBundleManifest object..
                    string[] dependencies = manifest.GetAllDependencies(_sbName);
                    for (int i = 0; i < dependencies.Length; ++i)
                    {
                        StringBuilder _assetSb = new StringBuilder(ResourceSystemComponent.AssetBundle_TargetDirectory_Path);
                        _assetSb.Append("/");
                        _assetSb.Append(dependencies[i]);

                        await LoadFileAsync(_assetSb.ToString(), dependencies[i]);
                    }
                }

                // Load asset
                var bundle = await AssetBundle.LoadFromFileAsync(_fullSBNameWithPath);

                string _name = bundle.name;
                _name = ResourceSystemUtil.GetBundleNameWithOutPrefix(_name);
                m_bundles.Add(_name, bundle);

                Debug.Log("Load assets:" + bundle.name);
            }
        }

        AssetBundleManifest GetManifest()
        {
            if (m_manifest == null)
            {
                AssetBundle manifestAB =
                    AssetBundle.LoadFromFile(ResourceSystemComponent.AssetBundleDependenciesPath + "/" +
                                             ResourceSystemComponent.AssetBundleDependenciesName);
                m_manifest = manifestAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                manifestAB.Unload(false);
            }

            return m_manifest;
        }

        /// <summary>
        /// 从给定bundle名字中加载资源
        /// </summary>
        /// <param name="_bundleName"></param>
        /// <param name="_assetName"></param>
        /// <returns></returns>
        public T LoadGameObjectFromBundle<T>(string _bundleName, string _assetName)
            where T : class
        {
            if (!m_bundles.ContainsKey(_bundleName))
            {
                Debug.Log("Has not such bundle");
                return null;
            }

            AssetBundle _ab = m_bundles[_bundleName];

            if (!_ab.Contains(_assetName))
            {
                Debug.Log(_bundleName + " dose not contains asset: " + _assetName);
                return null;
            }

            T _obj = m_bundles[_bundleName].LoadAsset(_assetName) as T;

            return _obj;
        }

        //重新设置下shader，ab包加载出来有丢失
        public static void RefreshShader(GameObject obj)
        {
            var rens = obj.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in rens)
            {
                var shaderName = r.sharedMaterial.shader.name;

                var newShader = Shader.Find(shaderName);

                if (newShader != null)
                    r.sharedMaterial.shader = newShader;
                else
                    Debug.Log("no this shader: " + shaderName);
            }
        }


#if SYNC_LOAD_BUNDLE
        /// <summary>
        /// 初始化AssetsBundle
        /// </summary>
        public void InitializeAssetsBundle()
        {
            byte[] stream = null;
            string uri = string.Empty;
            bundles = new Dictionary<string, AssetBundle>();
            uri = ResourceSystemComponent.DataPath + ResourceSystemComponent.AssetDir;
            if (!File.Exists(uri)) return;
            stream = File.ReadAllBytes(uri);
            assetbundle = AssetBundle.LoadFromMemory(stream);
            manifest = assetbundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        /// <summary>
        /// 载入素材
        /// </summary>
        public T LoadAsset<T>(string abname, string assetname) where T : UnityEngine.Object
        {
            abname = abname.ToLower();
            AssetBundle bundle = LoadAssetBundle(abname);
            return bundle.LoadAsset<T>(assetname);
        }


        public void LoadPrefab(string abName, string[] assetNames, Action func)
        {
            abName = abName.ToLower();
            List<UObject> result = new List<UObject>();
            for (int i = 0; i < assetNames.Length; i++)
            {
                UObject go = LoadAsset<UObject>(abName, assetNames[i]);
                if (go != null) result.Add(go);
            }
            if (func != null)
            {
                func();    //资源获取成功，回调func，执行后续操作 
            }
        }

        public void LoadAudioClip(string abName, string assetNames, Action<UObject> fun)
        {
            abName = abName.ToLower();
            List<UObject> result = new List<UObject>();
            UObject go = LoadAsset<UObject>(abName, assetNames);
            if (fun != null)
            {
                fun(go);
            }
        }

        /// <summary>
        /// 载入AssetBundle
        /// </summary>
        /// <param name="abname"></param>
        /// <returns></returns>
        public AssetBundle LoadAssetBundle(string abname)
        {
            if (!abname.EndsWith(ResourceSystemComponent.ExtName))
            {
                abname += ResourceSystemComponent.ExtName;
            }
            AssetBundle bundle = null;
            if (!bundles.ContainsKey(abname))
            {
                byte[] stream = null;
                string uri = AppConst.DataPath + abname;
                // Debug.LogWarning("LoadFile::>> " + uri);
                LoadDependencies(abname);

                stream = File.ReadAllBytes(uri);
                bundle = AssetBundle.LoadFromMemory(stream); //关联数据的素材绑定
                bundles.Add(abname, bundle);
            }
            else
            {
                bundles.TryGetValue(abname, out bundle);
            }
            return bundle;
        }

        Dictionary<string, AssetBundle> m_LoadAB = new Dictionary<string, AssetBundle>();
        public GameObject MyLoadPrefab(string bundleName, string prefabName, Action func)
        {
            GameObject newPrefab = null;
            string uri = AppConst.DataPath + bundleName + AppConst.ExtName;
            Debug.LogWarning("@LoadPrefab:LoadFile::>> " + uri);
            AssetBundle ab = null;
            //如果为空 表示第一次载入  //already loaded.  直接读取
            if (!m_LoadAB.TryGetValue(bundleName, out ab))
            {
                ab = AssetBundle.LoadFromFile(uri);
                m_LoadAB.Add(bundleName, ab);
            }

            newPrefab = ab.LoadAsset(prefabName, typeof(GameObject)) as GameObject;


            if (func != null)
            {
                func();    //资源获取成功，回调func，执行后续操作 
            }
            return newPrefab;
        }



        /// <summary>
        /// 载入依赖
        /// </summary>
        /// <param name="name"></param>
        void LoadDependencies(string name)
        {
            if (manifest == null)
            {
                Debug.LogError("Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
                return;
            }
            // Get dependecies from the AssetBundleManifest object..
            string[] dependencies = manifest.GetAllDependencies(name);
            if (dependencies.Length == 0) return;

            for (int i = 0; i < dependencies.Length; i++)
                dependencies[i] = RemapVariantName(dependencies[i]);

            // Record and load all dependencies.
            for (int i = 0; i < dependencies.Length; i++)
            {
                LoadAssetBundle(dependencies[i]);
            }
        }
        // Remaps the asset bundle name to the best fitting asset bundle variant.
        string RemapVariantName(string assetBundleName)
        {
            string[] bundlesWithVariant = manifest.GetAllAssetBundlesWithVariant();

            // If the asset bundle doesn't have variant, simply return.
            if (System.Array.IndexOf(bundlesWithVariant, assetBundleName) < 0)
                return assetBundleName;

            string[] split = assetBundleName.Split('.');

            int bestFit = int.MaxValue;
            int bestFitIndex = -1;
            // Loop all the assetBundles with variant to find the best fit variant assetBundle.
            for (int i = 0; i < bundlesWithVariant.Length; i++)
            {
                string[] curSplit = bundlesWithVariant[i].Split('.');
                if (curSplit[0] != split[0])
                    continue;

                int found = System.Array.IndexOf(m_Variants, curSplit[1]);
                if (found != -1 && found < bestFit)
                {
                    bestFit = found;
                    bestFitIndex = i;
                }
            }
            if (bestFitIndex != -1)
                return bundlesWithVariant[bestFitIndex];
            else
                return assetBundleName;
        }
#endif

        #region Resource Load

        /// <summary>
        /// Resource load资源（带对象缓冲技术）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="isCatch"></param>
        /// <returns></returns>
        public T LoadResource<T>(string path, bool isCatch = true) where T : UnityEngine.Object
        {
            if (ht.Contains(path))
            {
                return ht[path] as T;
            }

            T TResource = Resources.Load<T>(path);
            if (TResource == null)
            {
                Debug.LogError(GetType() + "/GetInstance()/TResource 提取的资源找不到，请检查。 path=" + path);
            }
            else if (isCatch)
            {
                ht.Add(path, TResource);
            }

            return TResource;
        }

        /// <summary>
        /// Resource加载并返回实例化GameObject（带对象缓冲技术）
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isCatch"></param>
        /// <returns></returns>
        public GameObject LoadGameObject(string path, bool isCatch = true, Transform parent = null)
        {
            GameObject goObj = LoadResource<GameObject>(path, isCatch);
            GameObject goObjClone = GameObject.Instantiate<GameObject>(goObj, parent);
            if (goObjClone == null)
            {
                Debug.LogError(GetType() + "/LoadAsset()/克隆资源不成功，请检查。 path=" + path);
            }

            //goObj = null;//??????????
            return goObjClone;
        }

        #endregion

        /// <summary>
        /// 销毁资源
        /// </summary>
        new void OnDestroy()
        {
#if SYNC_LOAD_BUNDLE
            if (shared != null) shared.Unload(true);
            if (manifest != null) manifest = null;
            Debug.Log("~ResourceManager was destroy!");

#endif
            // upload ht resource
            if (ht != null && ht.Count > 0)
            {
                Resources.UnloadUnusedAssets();
            }

            if (m_bundles != null && m_bundles.Count > 0)
            {
                AssetBundle.UnloadAllAssetBundles(false);
            }
        }

        /// <summary>
        /// read csv with path to string
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void ReadCSVData(string path, Action<string> callback)
        {
            // Start coroutine
            GameObject _obj = new GameObject("ReadCSVTool");
            // StartCoroutine(Util.Utils.CheckPath(path, (bool exists) =>
            // {
            //     if (exists)
            //     {
            //         _mono.StartCoroutine(ReadCSVCoroutine(path, (string _data) =>
            //         {
            //             callback(_data);
            //             Destroy(_mono.gameObject);
            //         }));
            //     }
            //     else
            //     {
            //         Destroy(_mono.gameObject);
            //         Debug.Log("File not exists :" + path);
            //     }
            // }));
        }

        static IEnumerator ReadCSVCoroutine(string path, Action<string> callback)
        {
            var _request = UnityWebRequest.Get(path);
            yield return _request.SendWebRequest();
            if (_request.isNetworkError || _request.isHttpError)
            {
                _request.Dispose();
                callback(string.Empty);
            }
            else
            {
                callback(_request.downloadHandler.text);
            }
        }

        /// <summary>
        /// Loads the assets bundle for web.
        /// </summary>
        /// <param name="url">URL.</param>
        async public UniTask LoadAssetsBundleForWeb(string url, Action<AssetBundle> callback)
        {
            await LoadWebAssetsBundleCoroutine(url, callback);
        }

        IEnumerator LoadWebAssetsBundleCoroutine(string url, Action<AssetBundle> callback, bool isLocal = false)
        {
            // Load bundle 
            var _url = url;
            if (isLocal)
                _url = GetUrl(url);

            // Get Image
            UnityWebRequest _request = UnityWebRequestAssetBundle.GetAssetBundle(_url);

            yield return _request.SendWebRequest();
            if (_request.isNetworkError || _request.isHttpError)
            {
                Debug.Log(_request.error);
                _request.Dispose();
            }
            else
            {
                callback(DownloadHandlerAssetBundle.GetContent(_request));
            }
        }

        string GetUrl(string url)
        {
            String path = "";
#if UNITY_IOS && !UNITY_EDITOR
            path = "file:///" + url;
#elif UNITY_ANDROID && !UNITY_EDITOR
            path = url;
#elif UNITY_EDITOR
            path = "file:///" + url;
#endif
            return path;
        }
    }

}