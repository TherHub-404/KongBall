using UnityEngine;

namespace KongBall
{
    // UI navigation sound effects — separate from SfxManager's procedurally-generated gameplay SFX
    // on purpose: those exist specifically to need no external audio assets, these ARE external
    // assets (CC0 "organic" pack from uisfx, see Assets/Resources/Sfx/UISFX_LICENSE.txt), so
    // conflating the two would make SfxManager's own header comment a lie.
    //
    // Lazily self-installing, like SfxManager: any screen can call UiSfx.Open()/Back()/Hover() etc.
    // without first finding or creating anything.
    public static class UiSfx
    {
        static AudioSource _src;
        static AudioClip _open, _back, _hover, _loading;
        static bool _loaded;

        static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;

            var go = new GameObject("UiSfx");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;

            _open = Resources.Load<AudioClip>("Sfx/UiOpen");
            _back = Resources.Load<AudioClip>("Sfx/UiBack");
            _hover = Resources.Load<AudioClip>("Sfx/UiHover");
            _loading = Resources.Load<AudioClip>("Sfx/UiLoading");
        }

        // "avanti" — opening a sub-screen, confirming a choice, moving forward in a flow.
        public static void Open() { Ensure(); if (_open != null) _src.PlayOneShot(_open, 0.8f); }

        // "indietro" — every INDIETRO/ABBANDONA-style back action.
        public static void Back() { Ensure(); if (_back != null) _src.PlayOneShot(_back, 0.8f); }

        // Pointer entering a button's hit area. Quiet on purpose — this plays far more often than
        // Open/Back and has to stay in the background, not compete with them.
        public static void Hover() { Ensure(); if (_hover != null) _src.PlayOneShot(_hover, 0.35f); }

        // Once per connecting screen shown, not looped — ConnectingScreen's own pulsing dots already
        // carry the "still working" read; this is just the attention beat at the start.
        public static void Loading() { Ensure(); if (_loading != null) _src.PlayOneShot(_loading, 0.7f); }
    }
}
