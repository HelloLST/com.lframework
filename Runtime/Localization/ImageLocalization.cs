using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    public class ImageLocalization : UILocalization<Image>
    {
        public enum SrcMode
        {
            // 自动通过配置表中的 url 识别
            Auto = 0,

            // Resources方式加载
            Resources,

            // 网络加载
            Net,
        }

        [SerializeField]
        protected SrcMode m_SrcMode = SrcMode.Auto;

        // 当前持有的资源句柄（Resources 模式：句柄钉住引用计数，防止被缓存淘汰。
        // Phase 5 / 5.6 起用句柄替代手工 AddRef/ReleaseRef 配对）
        private IResourceHandle m_RefHandle;

        // 网络精灵缓存：url -> Sprite（同一 url 只下载一次，进程生命周期内复用）
        private static readonly Dictionary<string, Sprite> _netSpriteCache = new Dictionary<string, Sprite>();

        // 网络加载去重：同一 url 的并发请求复用同一个任务，避免重复下载
        private static readonly Dictionary<string, UniTask<Sprite>> _netLoadingTasks = new Dictionary<string, UniTask<Sprite>>();

        /// <summary>
        /// 关闭 Domain Reload 时静态缓存会跨 Play 会话残留（持有上一次会话的假 null Sprite）。
        /// 进入 Play 时强制清空（Phase 1 / P0-2 修复）。
        /// Domain Reload 开启时静态本就会被清空，此方法幂等无害。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForNewSession()
        {
            _netSpriteCache.Clear();
            _netLoadingTasks.Clear();
        }

        protected override void RefreshShow()
        {
            string url = GetLocalizationData();
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            switch (m_SrcMode)
            {
                case SrcMode.Auto:
                    LoadAuto(url, m_Target);
                    break;

                case SrcMode.Resources:
                    LoadResources(url, m_Target).Forget();
                    break;

                case SrcMode.Net:
                    LoadNet(url, m_Target).Forget();
                    break;
            }
        }

        private void LoadAuto(string url, Image target)
        {
            if (url.StartsWith("Res://"))
            {
                LoadResources(url.Substring("Res://".Length), target).Forget();
            }
            else if (url.StartsWith("Net://"))
            {
                LoadNet(url.Substring("Net://".Length), target).Forget();
            }
            else
            {
                // 默认走 Resources
                LoadResources(url, target).Forget();
            }
        }

        // 通过 ResourcesManager 加载（统一走框架资源系统，带缓存与引用计数）
        private async UniTaskVoid LoadResources(string url, Image target)
        {
            if (target == null) return;

            Sprite sprite = await ResourcesManager.Instance.LoadAsync<Sprite>(url);
            if (sprite == null || target == null) return;

            // 以句柄钉住引用，防止被缓存淘汰（Phase 5 / 5.6：替代手工 AddRef/ReleaseRef 配对）
            IResourceHandle handle = ResourcesManager.Instance.Pin<Sprite>(url);
            if (target == null)
            {
                handle.Dispose();
                return;
            }

            ApplySprite(sprite, handle);
        }

        // 网络加载：带全局缓存与并发去重
        private async UniTaskVoid LoadNet(string url, Image target)
        {
            if (target == null) return;

            Sprite sprite = await LoadOrFetchNetSprite(url);
            if (sprite != null && target != null)
            {
                ApplySprite(sprite, null);
            }
        }

        private static async UniTask<Sprite> LoadOrFetchNetSprite(string url)
        {
            if (_netSpriteCache.TryGetValue(url, out Sprite cached) && cached != null)
            {
                return cached;
            }

            if (_netLoadingTasks.TryGetValue(url, out UniTask<Sprite> pending))
            {
                return await pending;
            }

            var task = FetchNetSprite(url);
            _netLoadingTasks[url] = task;
            try
            {
                Sprite sprite = await task;
                if (sprite != null && !_netSpriteCache.ContainsKey(url))
                {
                    _netSpriteCache[url] = sprite;
                }
                return sprite;
            }
            finally
            {
                _netLoadingTasks.Remove(url);
            }
        }

        private static async UniTask<Sprite> FetchNetSprite(string url)
        {
            try
            {
                using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
                {
                    await req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        LogManager.Error($"[Localization] 加载网络图片失败: {req.error} ({url})");
                        return null;
                    }

                    Texture2D texture = DownloadHandlerTexture.GetContent(req);
                    if (texture == null) return null;

                    return Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                }
            }
            catch (System.Exception e)
            {
                LogManager.Error($"[Localization] LoadNet 异常: {e}");
                return null;
            }
        }

        private void ApplySprite(Sprite sprite, IResourceHandle handle)
        {
            // 先释放上一张持有的引用计数
            ReleaseHeldAssets();

            m_RefHandle = handle;
            if (m_Target != null)
            {
                m_Target.sprite = sprite;
            }
        }

        private void ReleaseHeldAssets()
        {
            if (m_RefHandle != null)
            {
                m_RefHandle.Dispose();
                m_RefHandle = null;
            }
        }

        protected override void OnDestroy()
        {
            // 释放持有的资源引用（网络精灵由全局缓存持有，无需逐实例销毁）
            ReleaseHeldAssets();
            base.OnDestroy();
        }
    }
}
