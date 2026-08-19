using UnityEngine;

namespace KongBall
{
    // Who made the build you are looking at.
    //
    // A build comes from exactly one merge into `dev`, so there is always one pull request behind it,
    // and that pull request has a branch shaped `name/feature` — which is precisely "author and
    // feature". The pipeline writes that branch into Assets/Resources/BuildStamp.txt before Unity
    // runs.
    //
    // The file is committed EMPTY on purpose. A build made outside the pipeline then shows nothing at
    // all, rather than whatever name happened to be baked in last: a stamp that lies about who made
    // the build is worse than no stamp.
    public static class BuildStamp
    {
        static string _label;
        static bool _loaded;

        // "luca · portiere-automatico", or null when there is nothing to show.
        //
        // The branch is kept verbatim rather than prettified into "Portiere Automatico": it is the
        // string you type to find the work behind the build, and a stamp you cannot search with is
        // half a stamp.
        public static string Label
        {
            get
            {
                if (_loaded) return _label;
                _loaded = true;

                // Il nome scritto come letterale e non tramite una costante: asset_sanity.py verifica
                // che ogni nome passato a Resources.Load abbia un asset dietro, e sa leggere solo i
                // letterali. Una costante avrebbe reso il controllo cieco proprio qui.
                var asset = Resources.Load<TextAsset>("BuildStamp");
                string raw = asset != null ? asset.text.Trim() : null;
                if (string.IsNullOrEmpty(raw)) return _label = null;

                int slash = raw.IndexOf('/');
                _label = slash > 0 && slash < raw.Length - 1
                    ? raw.Substring(0, slash) + "   ·   " + raw.Substring(slash + 1)
                    : raw;
                return _label;
            }
        }
    }
}
