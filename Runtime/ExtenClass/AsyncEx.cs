using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    public static class AsyncEx
    {
        public static TaskAwaiter GetAwaiter(this AsyncOperation asyncOp)
        {
            var tcs = new TaskCompletionSource<object>();
            asyncOp.completed += obj => { tcs.SetResult(null); };
            return ((Task)tcs.Task).GetAwaiter();
        }

        /// <summary>
        /// 等待指定秒数后继续（UniTask 快捷封装）
        /// </summary>
        public static async UniTask Delay(this object _this, float seconds)
        {
            await UniTask.Delay(Mathf.Max(0, (int)(seconds * 1000)));
        }

        /// <summary>
        /// 将 AsyncOperation 转为 UniTask 等待完成
        /// </summary>
        public static async UniTask ToUniTask(this AsyncOperation asyncOp)
        {
            await UniTask.WaitUntil(() => asyncOp.isDone);
        }
    }
}
