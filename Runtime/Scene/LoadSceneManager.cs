using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 场景加载管理器
    /// </summary>
    public class LoadSceneManager : SingletonBase<LoadSceneManager>
    {
        /// <summary>
        /// 重新切换到当前场景
        /// </summary>
        public void LoadActiveScene()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// 切换到下一个场景
        /// </summary>
        public void LoadNextScene(bool isCyclical = false)
        {
            int buildIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (buildIndex > SceneManager.sceneCountInBuildSettings - 1)
            {
                if (isCyclical)
                    buildIndex = 0;
                else
                {
                    LogManager.Warning($"加载场景失败！索引 {buildIndex} 越界");
                    return;
                }
            }
            SceneManager.LoadScene(buildIndex);
        }

        /// <summary>
        /// 切换到上一个场景
        /// </summary>
        public void LoadPreviousScene(bool isCyclical = false)
        {
            int buildIndex = SceneManager.GetActiveScene().buildIndex - 1;
            if (buildIndex < 0)
            {
                if (isCyclical)
                    buildIndex = SceneManager.sceneCountInBuildSettings - 1;
                else
                {
                    LogManager.Warning($"加载场景失败！索引 {buildIndex} 不能为负数");
                    return;
                }
            }
            SceneManager.LoadScene(buildIndex);
        }

        #region UniTask 异步加载

        /// <summary>
        /// 异步加载场景（UniTask 版本）
        /// 返回 AsyncOperation 句柄；allowSceneActivation=false 时由调用方持句柄自行激活，
        /// 否则场景会一直停在加载中（progress≈0.9）无法完成。
        /// </summary>
        public async UniTask<AsyncOperation> LoadSceneAsync(string sceneName, IProgress<float> progress = null,
            bool allowSceneActivation = true, LoadSceneMode mode = LoadSceneMode.Single)
        {
            AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneName, mode);
            asyncOperation.allowSceneActivation = false;
            await LoadSceneAsyncCore(asyncOperation, progress, allowSceneActivation);
            return asyncOperation;
        }

        /// <summary>
        /// 异步加载场景（UniTask 版本，按索引）
        /// 返回 AsyncOperation 句柄；allowSceneActivation=false 时由调用方持句柄自行激活，
        /// 否则场景会一直停在加载中（progress≈0.9）无法完成。
        /// </summary>
        public async UniTask<AsyncOperation> LoadSceneAsync(int sceneIndex, IProgress<float> progress = null,
            bool allowSceneActivation = true, LoadSceneMode mode = LoadSceneMode.Single)
        {
            AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneIndex, mode);
            asyncOperation.allowSceneActivation = false;
            await LoadSceneAsyncCore(asyncOperation, progress, allowSceneActivation);
            return asyncOperation;
        }

        /// <summary>
        /// UniTask 加载核心逻辑（进度轮询 + 激活）
        /// allowSceneActivation=false 时进度停在 0.9 后返回，由调用方持 AsyncOperation 激活后自行收尾进度。
        /// </summary>
        private async UniTask LoadSceneAsyncCore(AsyncOperation asyncOperation, IProgress<float> progress, bool allowSceneActivation)
        {
            while (asyncOperation.progress < 0.9f)
            {
                progress?.Report(asyncOperation.progress);
                await UniTask.Yield();
            }

            progress?.Report(0.9f);
            asyncOperation.allowSceneActivation = allowSceneActivation;

            if (allowSceneActivation)
            {
                await UniTask.WaitUntil(() => asyncOperation.isDone);
                progress?.Report(1f);
            }
        }

        #endregion
    }
}
