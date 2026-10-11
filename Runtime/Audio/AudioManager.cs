using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 音频管理器
    /// Phase 5 / 5.3：SFX 回收改 UniTask 驱动（CTS 可取消），不再使用协程。
    /// </summary>
    public class AudioManager : UnitySingleton<AudioManager>, IModule
    {
        // 回收中的 SFX 任务（按 GameObject 跟踪，防止同一对象重复播放时旧任务掐断新声音）
        private readonly Dictionary<GameObject, CancellationTokenSource> _sfxRecycle = new Dictionary<GameObject, CancellationTokenSource>();

        // 每个 SFX 池的可用对象队列（已回收待复用的对象）
        private readonly Dictionary<GameObject, Queue<GameObject>> _sfxFreeQueues = new Dictionary<GameObject, Queue<GameObject>>();

        private AudioSource _bgmSource;
        private AudioSource _bgsSource;
        private AudioSource _voiceSource;

        private GameObject _bgmController;
        private GameObject _bgsController;
        private GameObject _voiceController;
        private GameObject _sfxPool2D;
        private GameObject _sfxPool3D;

        private const string SFX_POOL_2D_NAME = "SFXPool2D";
        private const string SFX_CLIP_2D_NAME = "SFXClip2D";
        private const string SFX_POOL_3D_NAME = "SFXPool3D";
        private const string SFX_CLIP_3D_NAME = "SFXClip3D";

        private float _bgmVolume = 1f;
        private float _bgsVolume = 1f;
        private float _voiceVolume = 1f;
        private float _sfxVolume = 1f;

        protected override void Awake()
        {
            base.Awake();
            if (_isDuplicate) return;
            InitAudioSources();
        }

        private void InitAudioSources()
        {
            _bgmController = CreateController("BgmController", transform);
            _bgmSource = _bgmController.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;
            _bgmSource.bypassEffects = false;

            _bgsController = CreateController("BgsController", transform);
            _bgsSource = _bgsController.AddComponent<AudioSource>();
            _bgsSource.playOnAwake = false;
            _bgsSource.loop = true;

            _voiceController = CreateController("VoiceController", transform);
            _voiceSource = _voiceController.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;

            var sfxController = CreateController("SFXController", transform);
            _sfxPool2D = CreateController(SFX_POOL_2D_NAME, sfxController.transform);
            _sfxPool3D = CreateController(SFX_POOL_3D_NAME, sfxController.transform);
        }

        private GameObject CreateController(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            return go;
        }

        #region BGM 控制

        public void PlayBGM(AudioClip bgm, bool loop = true)
        {
            if (bgm == null) { Debug.LogWarning("[AudioManager] PlayBGM failed: clip is null"); return; }
            _bgmSource.clip = bgm;
            _bgmSource.loop = loop;
            _bgmSource.volume = _bgmVolume;
            _bgmSource.Play();
        }

        public void PauseBGM() => _bgmSource.Pause();
        public void UnPauseBGM() => _bgmSource.UnPause();
        public void StopBGM() { _bgmSource.Stop(); _bgmSource.clip = null; }

        /// <summary>
        /// 淡入 BGM（UniTask 版本）
        /// </summary>
        public async UniTask FadeInBGMAsync(AudioClip bgm, float duration = 1f, bool loop = true)
        {
            PlayBGM(bgm, loop);
            await FadeVolumeAsync(_bgmSource, 0, _bgmVolume, duration);
        }

        /// <summary>
        /// 淡出 BGM（UniTask 版本）
        /// </summary>
        public async UniTask FadeOutBGMAsync(float duration = 1f)
        {
            await FadeVolumeAsync(_bgmSource, _bgmVolume, 0, duration);
            StopBGM();
        }

        #endregion

        #region BGS 控制

        public void PlayBGS(AudioClip bgs, bool loop = true)
        {
            if (bgs == null) { Debug.LogWarning("[AudioManager] PlayBGS failed: clip is null"); return; }
            _bgsSource.clip = bgs;
            _bgsSource.loop = loop;
            _bgsSource.volume = _bgsVolume;
            _bgsSource.Play();
        }

        public void PauseBGS() => _bgsSource.Pause();
        public void UnPauseBGS() => _bgsSource.UnPause();
        public void StopBGS() { _bgsSource.Stop(); _bgsSource.clip = null; }

        #endregion

        #region Voice 控制

        public void PlayVoice(AudioClip voice)
        {
            if (voice == null) { Debug.LogWarning("[AudioManager] PlayVoice failed: clip is null"); return; }
            _voiceSource.Stop();
            _voiceSource.clip = voice;
            _voiceSource.volume = _voiceVolume;
            _voiceSource.Play();
        }

        public void StopVoice() { _voiceSource.Stop(); _voiceSource.clip = null; }
        public void PauseVoice() => _voiceSource.Pause();
        public void UnPauseVoice() => _voiceSource.UnPause();

        #endregion

        #region SFX 控制

        public void PlaySound(AudioClip sound)
        {
            if (sound == null) { Debug.LogWarning("[AudioManager] PlaySound failed: clip is null"); return; }
            GameObject go = GetAudioObjectFromPool(_sfxPool2D, SFX_CLIP_2D_NAME);
            AudioSource source = GetOrAddAudioSource(go);
            source.clip = sound; source.loop = false; source.spatialBlend = 0f; source.volume = _sfxVolume;
            source.Play();
            StartRecycle(go, source, sound.length, is3D: false);
        }

        public void PlaySound(AudioClip sound, GameObject target)
        {
            if (sound == null || target == null) return;
            GameObject go = GetAudioObjectFromPool(_sfxPool3D, SFX_CLIP_3D_NAME);
            go.transform.SetParent(target.transform); go.transform.localPosition = Vector3.zero;
            AudioSource source = GetOrAddAudioSource(go);
            source.clip = sound; source.loop = false; source.spatialBlend = 1f; source.volume = _sfxVolume;
            source.Play();
            StartRecycle(go, source, sound.length, is3D: true);
        }

        public void PlaySound(AudioClip sound, Vector3 worldPosition, Transform parent = null)
        {
            if (sound == null) return;
            GameObject go = GetAudioObjectFromPool(_sfxPool3D, SFX_CLIP_3D_NAME);
            go.transform.position = worldPosition; go.transform.SetParent(parent ?? _sfxPool3D.transform);
            AudioSource source = GetOrAddAudioSource(go);
            source.clip = sound; source.loop = false; source.spatialBlend = 1f; source.volume = _sfxVolume;
            source.Play();
            StartRecycle(go, source, sound.length, is3D: true);
        }

        /// <summary>UI 音效相对于通用音效的音量放大系数（工程可调）</summary>
        public float UISoundVolumeScale { get; set; } = 1.2f;

        public void PlayUISound(AudioClip sound)
        {
            if (sound == null) return;
            GameObject go = GetAudioObjectFromPool(_sfxPool2D, "UIClip");
            AudioSource source = GetOrAddAudioSource(go);
            source.clip = sound; source.loop = false; source.spatialBlend = 0f; source.volume = _sfxVolume * UISoundVolumeScale;
            source.Play();
            StartRecycle(go, source, sound.length, is3D: false);
        }

        #endregion

        #region 音量控制

        public void SetBGMVolume(float volume) { _bgmVolume = Mathf.Clamp01(volume); _bgmSource.volume = _bgmVolume; }
        public void SetBGSVolume(float volume) { _bgsVolume = Mathf.Clamp01(volume); _bgsSource.volume = _bgsVolume; }
        public void SetVoiceVolume(float volume) { _voiceVolume = Mathf.Clamp01(volume); _voiceSource.volume = _voiceVolume; }
        public void SetSFXVolume(float volume) { _sfxVolume = Mathf.Clamp01(volume); }
        public void SetMasterVolume(float volume)
        {
            float v = Mathf.Clamp01(volume);
            _bgmVolume = v; _bgsVolume = v; _voiceVolume = v; _sfxVolume = v;
            _bgmSource.volume = _bgmVolume; _bgsSource.volume = _bgsVolume; _voiceSource.volume = _voiceVolume;
        }

        #endregion

        #region 内部方法

        private GameObject GetAudioObjectFromPool(GameObject pool, string clipName)
        {
            // 优先从该池的可用队列中取已回收对象
            if (_sfxFreeQueues.TryGetValue(pool, out Queue<GameObject> freeQueue) && freeQueue.Count > 0)
            {
                GameObject recycled = freeQueue.Dequeue();
                if (recycled != null) { recycled.SetActive(true); return recycled; }
            }

            for (int i = 0; i < pool.transform.childCount; i++)
            {
                var child = pool.transform.GetChild(i).gameObject;
                if (!child.activeSelf) { child.SetActive(true); return child; }
            }
            var go = new GameObject(clipName);
            go.transform.SetParent(pool.transform);
            return go;
        }

        private AudioSource GetOrAddAudioSource(GameObject go)
        {
            if (!go.TryGetComponent(out AudioSource source))
            {
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false;
            }
            return source;
        }

        /// <summary>
        /// 播放结束后回收 SFX 对象（UniTask + CTS：同一对象重复播放时取消旧任务，避免旧任务掐断新声音）
        /// </summary>
        private async UniTaskVoid RecycleAfterPlay(GameObject go, AudioSource source, float clipLength, bool is3D, CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(clipLength), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);

            _sfxRecycle.Remove(go);
            if (go == null) return;   // 目标销毁时对象随之销毁（Unity 假 null）

            source.clip = null;
            if (is3D)
            {
                go.transform.SetParent(_sfxPool3D.transform);
                go.transform.localPosition = Vector3.zero;
            }
            go.SetActive(false);
            EnqueueFree(is3D ? _sfxPool3D : _sfxPool2D, go);
        }

        /// <summary>
        /// 将回收对象登记到对应池的可用队列
        /// </summary>
        private void EnqueueFree(GameObject pool, GameObject go)
        {
            if (!_sfxFreeQueues.TryGetValue(pool, out Queue<GameObject> freeQueue))
            {
                freeQueue = new Queue<GameObject>();
                _sfxFreeQueues[pool] = freeQueue;
            }
            freeQueue.Enqueue(go);
        }

        /// <summary>
        /// 启动 SFX 回收任务；若同一对象已有回收任务则先取消，避免旧任务掐断新声音
        /// </summary>
        private void StartRecycle(GameObject go, AudioSource source, float clipLength, bool is3D)
        {
            if (_sfxRecycle.TryGetValue(go, out var old))
            {
                old.Cancel();
                old.Dispose();
            }
            var cts = new CancellationTokenSource();
            _sfxRecycle[go] = cts;
            RecycleAfterPlay(go, source, clipLength, is3D, cts.Token).Forget();
        }

        /// <summary>取消全部回收任务（销毁/Dispose 时调用）</summary>
        private void CancelAllRecycle()
        {
            foreach (var cts in _sfxRecycle.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _sfxRecycle.Clear();
        }

        /// <summary>
        /// 音量渐变（UniTask 版本）
        /// </summary>
        private async UniTask FadeVolumeAsync(AudioSource source, float from, float to, float duration)
        {
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                source.volume = Mathf.Lerp(from, to, elapsed / duration);
                await UniTask.Yield();
            }
            source.volume = to;
        }

        #endregion

        protected override void OnDestroy()
        {
            CancelAllRecycle();
            _sfxFreeQueues.Clear();
            base.OnDestroy();
        }

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Dispose()
        {
            CancelAllRecycle();
            _sfxFreeQueues.Clear();
        }

        #endregion
    }
}
