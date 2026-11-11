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

        // 当前从网络缓存持有着的 url（网络模式：每个实例算一次持有，销毁时归还）
        private string m_HeldNetUrl;

        // 请求代号：每次刷新自增。异步回来时若代号已变，说明这次的请求过期了
        //（组件已切到别的 key / 已销毁），结果不能贴上去，持有也必须还回去。
        private int m_RequestGeneration;

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
            _netLoadingTasks.Clear();
            // 上一会话的 Sprite / Texture2D 已被引擎随场景卸载销毁，
            // 这里只需把条目清掉（不能再 Destroy 一遍假 null 对象）
            NetSpriteCache.Clear();
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

            int generation = ++m_RequestGeneration;

            Sprite sprite = await LoadOrFetchNetSprite(url);

            // 等待期间本组件可能已被销毁、或已切到别的 url（语言切换会立刻触发新的刷新）。
            // 后到的结果既不能贴上去，也不能白占一次持有 —— 否则该条目永远淘汰不掉。
            if (sprite == null || target == null || generation != m_RequestGeneration)
            {
                if (sprite != null) NetSpriteCache.Release(url);
                return;
            }

            ApplySprite(sprite, null, url);
        }

        /// <summary>
        /// 取一张网络精灵并**占一次持有**。命中缓存则不下载；未命中则下载并登记。
        /// 返回非 null 时，调用方必须保证最终调用 <see cref="NetSpriteCache.Release"/>。
        /// </summary>
        private static async UniTask<Sprite> LoadOrFetchNetSprite(string url)
        {
            // 1. 命中缓存：直接加一次持有
            if (NetSpriteCache.TryAcquire(url, out Sprite cached))
            {
                return cached;
            }

            // 2. 已有同 url 的下载在进行：等它，然后补一次持有
            if (_netLoadingTasks.TryGetValue(url, out UniTask<Sprite> pending))
            {
                Sprite awaited = await pending;

                // 下载者已把结果登记进缓存；这里补持有。
                // 若登记后立刻被淘汰（极端情况），退化为「本次重新登记 + 持有」。
                if (awaited != null && !NetSpriteCache.Acquire(url))
                {
                    NetSpriteCache.RegisterAndHold(url, awaited);
                }
                return awaited;
            }

            // 3. 首个请求：下载 + 登记（登记持有数为 0）+ 自己占一次持有
            UniTask<Sprite> task = FetchNetSprite(url);
            _netLoadingTasks[url] = task;
            try
            {
                Sprite sprite = await task;
                if (sprite != null)
                {
                    NetSpriteCache.RegisterAndHold(url, sprite);
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

        private void ApplySprite(Sprite sprite, IResourceHandle handle, string netUrl = null)
        {
            // 先释放上一张持有的引用计数 / 网络精灵持有
            ReleaseHeldAssets();

            m_RefHandle = handle;
            m_HeldNetUrl = netUrl;

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

            // 网络精灵归还一次持有：持有数归零后条目才可被淘汰（H3）
            if (!string.IsNullOrEmpty(m_HeldNetUrl))
            {
                string url = m_HeldNetUrl;
                m_HeldNetUrl = null;
                NetSpriteCache.Release(url);
            }
        }

        protected override void OnDestroy()
        {
            // 作废所有在途请求：回来时不会再有实例来归还持有
            m_RequestGeneration++;

            // 释放持有的资源引用（Resources 句柄 + 网络精灵持有数）
            ReleaseHeldAssets();
            base.OnDestroy();
        }
    }
}
